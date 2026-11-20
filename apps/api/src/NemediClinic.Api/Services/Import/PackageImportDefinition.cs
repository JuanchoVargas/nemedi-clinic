using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Importar paquetes. Una fila por cada procedimiento del paquete, agrupadas por el nombre del paquete:
///
///   Paquete            Precio     Vigencia  Procedimiento        Sesiones
///   Piel Luminosa      900.000    180       Limpieza facial      4
///   Piel Luminosa                           Peeling químico      2
///
/// Reglas:
///   · Cada fila: paquete, procedimiento y sesiones. El procedimiento tiene que existir ya (se busca por
///     nombre, sin tildes ni mayúsculas); si no, error en esa fila con el nombre que no encontró.
///   · Precio y vigencia (días) son del paquete: de la primera fila que los trae; alguna tiene que traerlos.
///   · Un paquete se crea completo o no se crea: si una de sus filas tiene error, las demás también
///     quedan con error (un paquete al que le falta una sesión se vendería mal).
///   · Total de sesiones = suma de las sesiones de sus procedimientos.
///   · Duplicados por nombre del paquete. Con Actualizar cambian precio, vigencia, IVA y descripción; las
///     sesiones solo si el paquete nunca se ha vendido (lo vendido guarda su copia, pero el catálogo de un
///     paquete vendido no se reescribe: igual que en la pantalla de paquetes).
/// </summary>
public class PackageImportDefinition(AppDbContext db) : IImportDefinition
{
    public string Entidad => "packages";
    public string Nombre => "Paquetes";

    public Task<IReadOnlyList<ImportField>> GetFieldsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ImportField>>(Fields());

    public static List<ImportField> Fields() =>
    [
        new("paquete", "Nombre del paquete", ImportFieldType.Text, Required: true, MaxLength: 200,
            Aliases: ["paquete", "nombre del paquete", "nombre paquete", "plan", "nombre del plan", "nombre", "combo"],
            Hint: "Repite el nombre en cada fila del mismo paquete."),
        new("descripcion", "Descripción", ImportFieldType.Text, MaxLength: 1000, Aliases: ["descripcion", "detalle", "descripcion del paquete"]),
        new("precio", "Precio del paquete", ImportFieldType.Money,
            Aliases: ["precio", "valor", "precio de venta", "precio total", "valor total", "precio del paquete", "valor del paquete", "pvp"],
            Hint: "Basta en la primera fila del paquete."),
        new("vigencia", "Vigencia (días)", ImportFieldType.Days, Max: 3650,
            Aliases: ["vigencia", "vigencia dias", "dias de vigencia", "vigencia en dias", "validez", "vence en", "duracion del paquete"],
            Hint: "Días (180) o en meses (6 meses). Basta en la primera fila del paquete."),
        new("iva", "IVA", ImportFieldType.Iva, Aliases: ["iva", "iva %", "porcentaje iva", "tarifa iva", "impuesto"], Hint: "0, 5 o 19. Vacío = el de la clínica."),
        new("procedimiento", "Procedimiento", ImportFieldType.Text, Required: true, MaxLength: 200,
            Aliases: ["procedimiento", "procedimientos", "servicio", "tratamiento", "nombre del procedimiento", "procedimiento incluido", "incluye"],
            Hint: "Tiene que existir en Procedimientos, con el mismo nombre."),
        new("sesiones", "Sesiones", ImportFieldType.Count, Required: true, Max: 1000,
            Aliases: ["sesiones", "cantidad", "cantidad de sesiones", "numero de sesiones", "no sesiones", "nro sesiones", "cant", "sesiones incluidas"]),
    ];

    private sealed class Group
    {
        public required string Nombre { get; init; }
        public List<ImportRow> Rows { get; } = [];
        public Guid? ExistingId { get; set; }
        public int Ventas { get; set; }
    }

    private List<Group> _groups = [];
    private Dictionary<string, Guid> _procedures = [];

    public async Task ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        _procedures = (await db.Procedures.AsNoTracking().OrderBy(p => p.CreatedAt).Select(p => new { p.Id, p.Nombre }).ToListAsync(ct))
            .GroupBy(p => ImportText.Key(p.Nombre)).ToDictionary(g => g.Key, g => g.First().Id);
        var packages = (await db.Packages.AsNoTracking().Select(p => new { p.Id, p.Nombre }).ToListAsync(ct))
            .GroupBy(p => ImportText.Key(p.Nombre)).ToDictionary(g => g.Key, g => g.First());
        // Igual que PackagesController.RemoveProcedure: asignaciones vigentes del paquete en esta clínica
        var sold = await db.PatientPackages.GroupBy(pp => pp.PackageId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);

        var byName = new Dictionary<string, Group>();
        _groups = [];
        foreach (var row in rows)
        {
            if (row.Text("paquete") is not { } nombre) continue; // el motor ya marcó el error
            var key = ImportText.Key(nombre);
            if (!byName.TryGetValue(key, out var group))
            {
                group = new Group { Nombre = nombre };
                if (packages.TryGetValue(key, out var found))
                {
                    group.ExistingId = found.Id;
                    group.Ventas = sold.FirstOrDefault(s => s.Id == found.Id)?.Count ?? 0;
                }
                byName[key] = group;
                _groups.Add(group);
            }
            group.Rows.Add(row);
        }

        foreach (var group in _groups) ValidateGroup(group, packages.Values.FirstOrDefault(p => p.Id == group.ExistingId)?.Nombre);
    }

    private void ValidateGroup(Group group, string? existingName)
    {
        var seen = new Dictionary<Guid, int>();
        foreach (var row in group.Rows)
        {
            row.ExistingId = group.ExistingId;
            row.ExistingLabel = existingName;
            if (row.Text("procedimiento") is not { } procedimiento) continue;
            if (!_procedures.TryGetValue(ImportText.Key(procedimiento), out var procedureId))
            {
                row.Errors.Add($"No existe el procedimiento «{procedimiento}». Créalo (o impórtalo) primero, con ese nombre, y vuelve a subir el archivo.");
                continue;
            }
            if (seen.TryGetValue(procedureId, out var first))
            {
                row.Errors.Add($"«{procedimiento}» ya está en este paquete (fila {first}): pon todas sus sesiones en una sola fila.");
                continue;
            }
            seen[procedureId] = row.Fila;
            row.Values["procedureId"] = procedureId;
        }

        // Datos del paquete: de la primera fila que los trae
        var precio = ImportGrouping.Merge<decimal>(group.Rows, "precio", "Precio");
        var vigencia = ImportGrouping.Merge<int>(group.Rows, "vigencia", "Vigencia");
        ImportGrouping.Merge(group.Rows, "iva", "IVA");
        ImportGrouping.Merge(group.Rows, "descripcion", "Descripción");
        if (group.ExistingId is null)
        {
            if (precio is null) group.Rows[0].Errors.Add($"Falta el precio del paquete «{group.Nombre}» (en cualquiera de sus filas).");
            if (vigencia is null) group.Rows[0].Errors.Add($"Falta la vigencia en días del paquete «{group.Nombre}» (en cualquiera de sus filas).");
            var total = group.Rows.Sum(r => r.Get<int>("sesiones") ?? 0);
            if (total > 1000) group.Rows[0].Errors.Add($"El paquete suma {total} sesiones y el máximo es 1.000.");
        }
        else if (group.Ventas > 0)
        {
            group.Rows[0].Warnings.Add($"Ya se vendió a {group.Ventas} {(group.Ventas == 1 ? "paciente" : "pacientes")}: con Actualizar cambian precio, vigencia, IVA y descripción, pero no sus sesiones.");
        }

        // Todo o nada: un paquete al que le falta una fila no se crea
        var failed = group.Rows.Where(r => r.Errors.Count > 0).Select(r => r.Fila).ToList();
        if (failed.Count > 0)
            foreach (var row in group.Rows.Where(r => r.Errors.Count == 0))
                row.Errors.Add($"El paquete «{group.Nombre}» no se importa porque {(failed.Count == 1 ? $"la fila {failed[0]} tiene" : $"las filas {string.Join(", ", failed)} tienen")} error.");
    }

    public ImportPlan Plan(IReadOnlyList<ImportRow> rows)
    {
        var nuevos = _groups.Where(g => g.ExistingId is null && g.Rows.All(r => r.Status == ImportRowStatus.Valida)).ToList();
        var existentes = _groups.Count(g => g.ExistingId is not null && g.Rows.All(r => r.Status == ImportRowStatus.Duplicada));
        var sesiones = nuevos.Sum(g => g.Rows.Sum(r => r.Get<int>("sesiones") ?? 0));
        return new ImportPlan(nuevos.Count, existentes, nuevos.Count == 0 ? null : $"{sesiones} sesiones en {nuevos.Count} {(nuevos.Count == 1 ? "paquete nuevo" : "paquetes nuevos")}.");
    }

    public async Task<ImportApplyResult> ApplyAsync(IReadOnlyList<ImportRow> rows, ImportApplyContext context, CancellationToken ct)
    {
        int creadas = 0, actualizadas = 0, omitidas = 0, sesiones = 0;
        var ids = _groups.Where(g => g.ExistingId is not null).Select(g => g.ExistingId!.Value).ToList();
        var tracked = context.Mode == DuplicateMode.Actualizar && ids.Count > 0
            ? await db.Packages.Include(p => p.PackageProcedures).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];

        foreach (var group in _groups)
        {
            if (group.Rows.Any(r => r.Status == ImportRowStatus.Error)) continue;
            if (group.ExistingId is null)
            {
                var package = new Package { Nombre = group.Nombre, DiasAlertaVencimiento = 15 };
                Apply(package, group.Rows);
                db.Packages.Add(package);
                sesiones += SetSessions(package, group.Rows, replace: false);
                creadas++;
            }
            else if (tracked.TryGetValue(group.ExistingId.Value, out var current))
            {
                Apply(current, group.Rows);
                if (group.Ventas == 0) sesiones += SetSessions(current, group.Rows, replace: true);
                actualizadas++;
            }
            else omitidas++;
        }
        return new ImportApplyResult(creadas, actualizadas, omitidas, sesiones == 0 ? null : $"{sesiones} sesiones en total");
    }

    private static void Apply(Package p, List<ImportRow> rows)
    {
        T? First<T>(string key) where T : struct => rows.Select(r => r.Get<T>(key)).FirstOrDefault(v => v is not null);
        if (rows.Select(r => r.Text("descripcion")).FirstOrDefault(v => v is not null) is { } descripcion) p.Descripcion = descripcion;
        if (First<decimal>("precio") is { } precio) p.PrecioTotal = precio;
        if (First<int>("vigencia") is { } vigencia) p.VigenciaDias = vigencia;
        if (First<decimal>("iva") is { } iva) p.IvaPorcentaje = iva;
    }

    /// <summary>
    /// Los procedimientos del paquete y el total de sesiones. Al reemplazar, el que sigue en el archivo
    /// conserva su fila (solo cambia la cantidad) y el que ya no está se borra (soft delete): borrar y
    /// volver a insertar el mismo en un SaveChanges podría chocar con el índice único según el orden de los comandos.
    /// </summary>
    private int SetSessions(Package package, List<ImportRow> rows, bool replace)
    {
        var current = replace ? package.PackageProcedures.Where(pp => !pp.IsDeleted).ToDictionary(pp => pp.ProcedureId) : [];
        var total = 0;
        foreach (var row in rows)
        {
            if (row.Values.GetValueOrDefault("procedureId") is not Guid procedureId || row.Get<int>("sesiones") is not { } cantidad) continue;
            if (current.Remove(procedureId, out var kept)) kept.CantidadSesiones = cantidad;
            else db.PackageProcedures.Add(new PackageProcedure { PackageId = package.Id, ProcedureId = procedureId, CantidadSesiones = cantidad });
            total += cantidad;
        }
        foreach (var gone in current.Values) gone.IsDeleted = true;
        package.SesionesTotales = total;
        return total;
    }
}
