using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Importar procedimientos. Una fila = un procedimiento. Reglas:
///   · Nombre, duración (1 a 480 minutos) y precio obligatorios, como en el formulario.
///   · Duplicados por nombre (sin tildes ni mayúsculas): repetido en el archivo = error; existente =
///     duplicado (omitir o actualizar).
///   · Clínica IPS: campos RIPS. Una fila con código CUPS y sin la columna "se reporta en RIPS" se
///     toma como reportable. Mismas reglas que el formulario (RipsRules.ValidarProcedimiento).
/// </summary>
public class ProcedureImportDefinition(AppDbContext db) : IImportDefinition
{
    public string Entidad => "procedures";
    public string Nombre => "Procedimientos";

    public async Task<IReadOnlyList<ImportField>> GetFieldsAsync(CancellationToken ct) =>
        Fields(await RipsRules.TenantEsIpsAsync(db, ct));

    public static List<ImportField> Fields(bool esIps)
    {
        var fields = new List<ImportField>
        {
            new("nombre", "Nombre", ImportFieldType.Text, Required: true, MaxLength: 200,
                Aliases: ["nombre", "procedimiento", "nombre del procedimiento", "nombre procedimiento", "servicio", "nombre del servicio", "tratamiento"]),
            new("descripcion", "Descripción", ImportFieldType.Text, MaxLength: 1000,
                Aliases: ["descripcion", "detalle", "descripcion del procedimiento", "en que consiste"]),
            new("duracion", "Duración (minutos)", ImportFieldType.Minutes, Required: true, Max: 480,
                Aliases: ["duracion", "duracion min", "duracion minutos", "duracion en minutos", "minutos", "dura", "tiempo", "tiempo min",
                    "tiempo de sesion", "duracion de la sesion", "duracion sesion"],
                Hint: "Minutos (60) o con horas: 1 h 30, 1:30."),
            new("precio", "Precio", ImportFieldType.Money, Required: true,
                Aliases: ["precio", "valor", "precio de venta", "precio venta", "precio base", "tarifa", "valor sesion", "precio sesion", "pvp", "valor del procedimiento"]),
            new("iva", "IVA", ImportFieldType.Iva,
                Aliases: ["iva", "iva %", "porcentaje iva", "tarifa iva", "impuesto"], Hint: "0, 5 o 19. Vacío = el de la clínica."),
            new("areaCorporal", "Área corporal", ImportFieldType.Text, MaxLength: 100,
                Aliases: ["area", "area corporal", "zona", "zona corporal", "zona del cuerpo", "parte del cuerpo"]),
            new("consentimiento", "Requiere consentimiento", ImportFieldType.Boolean,
                Aliases: ["consentimiento", "requiere consentimiento", "consentimiento informado"]),
        };
        if (esIps)
        {
            fields.AddRange(
            [
                new("requiereRips", "Se reporta en RIPS", ImportFieldType.Boolean, Group: "RIPS",
                    Aliases: ["rips", "reporta rips", "se reporta en rips", "requiere rips", "reportable"],
                    Hint: "Sí / no. Sin esta columna, un procedimiento con código CUPS se reporta."),
                new("codigoCups", "Código CUPS", ImportFieldType.Code, MaxLength: 10, Group: "RIPS",
                    Aliases: ["cups", "codigo cups", "cod cups", "codigo"]),
                new("finalidad", "Finalidad de la consulta", ImportFieldType.Option, Group: "RIPS", Options: Finalidades,
                    Aliases: ["finalidad", "finalidad consulta", "finalidad de la consulta"]),
                new("tipoServicio", "Tipo de servicio", ImportFieldType.Option, Group: "RIPS", Options: TiposServicio,
                    Aliases: ["tipo de servicio", "tipo servicio", "servicio rips"]),
            ]);
        }
        return fields;
    }

    public async Task ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var esIps = await RipsRules.TenantEsIpsAsync(db, ct);
        var existing = (await db.Procedures.AsNoTracking().Select(p => new { p.Id, p.Nombre }).ToListAsync(ct))
            .GroupBy(p => ImportText.Key(p.Nombre)).ToDictionary(g => g.Key, g => g.First());
        var current = await db.Procedures.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var firstRow = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            if (row.Text("nombre") is not { } nombre) continue;
            var key = ImportText.Key(nombre);
            if (firstRow.TryGetValue(key, out var first))
            {
                row.Errors.Add($"«{nombre}» se repite en el archivo (también en la fila {first}).");
                continue;
            }
            firstRow[key] = row.Fila;
            if (existing.TryGetValue(key, out var found))
            {
                row.ExistingId = found.Id;
                row.ExistingLabel = found.Nombre;
            }
            if (row.Errors.Count > 0) continue;

            var procedure = row.ExistingId is { } id && current.TryGetValue(id, out var p) ? Clone(p) : new Procedure();
            Apply(procedure, row, isNew: row.ExistingId is null);
            try
            {
                RipsRules.ValidarProcedimiento(procedure, esIps);
            }
            catch (ApiException e)
            {
                row.Errors.Add(e.Message);
            }
        }
    }

    public async Task<ImportApplyResult> ApplyAsync(IReadOnlyList<ImportRow> rows, ImportApplyContext context, CancellationToken ct)
    {
        int creadas = 0, actualizadas = 0, omitidas = 0;
        var ids = rows.Where(r => r.Status == ImportRowStatus.Duplicada).Select(r => r.ExistingId!.Value).ToList();
        var tracked = context.Mode == DuplicateMode.Actualizar && ids.Count > 0
            ? await db.Procedures.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];
        foreach (var row in rows)
        {
            switch (row.Status)
            {
                case ImportRowStatus.Valida:
                    var procedure = new Procedure();
                    Apply(procedure, row, isNew: true);
                    db.Procedures.Add(procedure);
                    creadas++;
                    break;
                case ImportRowStatus.Duplicada when tracked.TryGetValue(row.ExistingId!.Value, out var current):
                    Apply(current, row, isNew: false);
                    actualizadas++;
                    break;
                case ImportRowStatus.Duplicada:
                    omitidas++;
                    break;
            }
        }
        return new ImportApplyResult(creadas, actualizadas, omitidas);
    }

    /// <summary>Al actualizar solo cambia lo que la fila trae; el nombre nunca (es la clave).</summary>
    private static void Apply(Procedure p, ImportRow row, bool isNew)
    {
        if (isNew && row.Text("nombre") is { } nombre) p.Nombre = nombre;
        if (row.Text("descripcion") is { } descripcion) p.Descripcion = descripcion;
        if (row.Get<int>("duracion") is { } duracion) p.DuracionMinutos = duracion;
        if (row.Get<decimal>("precio") is { } precio) p.PrecioBase = precio;
        if (row.Get<decimal>("iva") is { } iva) p.IvaPorcentaje = iva;
        if (row.Text("areaCorporal") is { } area) p.AreaCorporal = area;
        if (row.Get<bool>("consentimiento") is { } consentimiento) p.RequiereConsentimiento = consentimiento;
        if (row.Text("codigoCups") is { } cups) p.CodigoCups = RipsRules.Codigo(cups);
        if (Enum.TryParse<FinalidadConsulta>(row.Text("finalidad"), out var finalidad)) p.FinalidadConsulta = finalidad;
        if (Enum.TryParse<TipoServicioRips>(row.Text("tipoServicio"), out var tipo)) p.TipoServicio = tipo;
        if (row.Get<bool>("requiereRips") is { } rips) p.RequiereRips = rips;
        else if (row.Text("codigoCups") is not null) p.RequiereRips = true;
    }

    private static Procedure Clone(Procedure p) => new()
    {
        Nombre = p.Nombre, Descripcion = p.Descripcion, DuracionMinutos = p.DuracionMinutos, PrecioBase = p.PrecioBase,
        IvaPorcentaje = p.IvaPorcentaje, AreaCorporal = p.AreaCorporal, RequiereConsentimiento = p.RequiereConsentimiento,
        RequiereRips = p.RequiereRips, CodigoCups = p.CodigoCups, FinalidadConsulta = p.FinalidadConsulta, TipoServicio = p.TipoServicio,
    };

    private static readonly Dictionary<string, string> Finalidades = ImportOptions.Build(
        ("ValoracionIntegral", ["11", "valoracion integral", "valoracion"]),
        ("DeteccionTemprana", ["12", "deteccion temprana"]),
        ("ProteccionEspecifica", ["14", "proteccion especifica"]),
        ("Diagnostico", ["15", "diagnostico"]),
        ("Tratamiento", ["16", "tratamiento"]),
        ("Rehabilitacion", ["17", "rehabilitacion"]),
        ("Paliacion", ["18", "paliacion"]));

    private static readonly Dictionary<string, string> TiposServicio = ImportOptions.Build(
        ("Consulta", ["1", "01", "consulta"]),
        ("Procedimiento", ["2", "02", "procedimiento"]),
        ("OtroServicio", ["3", "03", "otro", "otro servicio", "otros servicios"]));
}
