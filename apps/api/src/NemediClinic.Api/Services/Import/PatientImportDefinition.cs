using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Importar pacientes. Lo propio de pacientes está aquí; leer el archivo, mapear y limpiar es del
/// motor (ImportEngine). Reglas:
///   · Nombre y apellido obligatorios (o "Nombre completo", que se parte: 2 palabras = 1 + 1,
///     3 = 1 nombre + 2 apellidos, 4 = 2 + 2; con 3 o más de 4 se avisa).
///   · Número de documento obligatorio; con su tipo, se valida el formato (CC, TI, RC y CN solo dígitos).
///   · Duplicados por número de documento, que es la clave única de un paciente en la clínica (índice
///     TenantId + Cedula): dentro del archivo es error; contra lo existente la fila queda "duplicada"
///     (omitir o actualizar). Mismo número con OTRO tipo de documento es error: no pueden convivir.
///   · Clínica IPS: los datos que exige RIPS, con las mismas reglas que el formulario (RipsRules).
///   · Cada paciente nuevo nace con su historia clínica vacía, como al crearlo a mano.
/// </summary>
public class PatientImportDefinition(AppDbContext db) : IImportDefinition
{
    public string Entidad => "patients";
    public string Nombre => "Pacientes";

    private static readonly HashSet<TipoDocumento> NumericDocuments = [TipoDocumento.CC, TipoDocumento.TI, TipoDocumento.RC, TipoDocumento.CN];

    public async Task<IReadOnlyList<ImportField>> GetFieldsAsync(CancellationToken ct) =>
        Fields(await RipsRules.TenantEsIpsAsync(db, ct));

    /// <summary>Los campos; los de RIPS solo para una IPS. Estático para probar el mapeo sin base.</summary>
    public static List<ImportField> Fields(bool esIps)
    {
        var fields = new List<ImportField>
        {
            new("nombreCompleto", "Nombre completo", ImportFieldType.Name, MaxLength: 200,
                Aliases: ["nombre completo", "nombres y apellidos", "nombre y apellido", "paciente", "nombre del paciente", "nombre paciente", "cliente", "nombre cliente", "full name"],
                Hint: "Solo si nombre y apellido vienen en una misma columna: se parte en los dos."),
            new("nombre", "Nombre", ImportFieldType.Name, MaxLength: 100,
                Aliases: ["nombre", "nombres", "primer nombre", "nombre s", "name", "first name"],
                Hint: "Obligatorio, salvo que venga en Nombre completo."),
            new("apellido", "Apellido", ImportFieldType.Name, MaxLength: 100,
                Aliases: ["apellido", "apellidos", "primer apellido", "last name", "surname"],
                Hint: "Obligatorio, salvo que venga en Nombre completo."),
            new("tipoDocumento", "Tipo de documento", ImportFieldType.Option, Options: DocumentTypes,
                Aliases: ["tipo documento", "tipo de documento", "tipo doc", "tipo de doc", "tipo id", "tipo identificacion", "tipo de identificacion", "td"]),
            new("documento", "Número de documento", ImportFieldType.Document, Required: true, MaxLength: 20,
                Aliases: ["documento", "numero documento", "numero de documento", "no documento", "nro documento", "num documento",
                    "documento de identidad", "identificacion", "numero de identificacion", "no identificacion", "cedula", "cc", "c c",
                    "cedula de ciudadania", "no cedula", "numero cedula", "id", "dni", "doc"]),
            new("fechaNacimiento", "Fecha de nacimiento", ImportFieldType.Date,
                Aliases: ["fecha nacimiento", "fecha de nacimiento", "fecha nac", "f nacimiento", "fec nac", "f nac", "nacimiento",
                    "fecha nacim", "cumpleanos", "birth date", "date of birth", "dob"]),
            new("sexo", "Sexo", ImportFieldType.Option, Options: Sexes,
                Aliases: ["sexo", "genero", "sex", "gender"], ColumnParser: SexParser,
                Hint: "M/F (masculino/femenino) o H/M (hombre/mujer, como RIPS): se decide mirando la columna."),
            new("telefono", "Teléfono", ImportFieldType.Phone, MaxLength: 20,
                Aliases: ["telefono", "celular", "cel", "movil", "tel", "whatsapp", "telefono celular", "numero celular", "no celular",
                    "telefono movil", "contacto", "telefono contacto", "phone", "mobile"]),
            new("email", "Correo", ImportFieldType.Email, MaxLength: 150,
                Aliases: ["correo", "email", "e mail", "correo electronico", "mail", "correo e"]),
            new("direccion", "Dirección", ImportFieldType.Text, MaxLength: 200,
                Aliases: ["direccion", "dir", "domicilio", "direccion residencia", "direccion de residencia", "address"]),
            new("ciudad", "Ciudad", ImportFieldType.Name, MaxLength: 100,
                Aliases: ["ciudad", "municipio", "ciudad residencia", "ciudad de residencia", "city"]),
        };

        if (esIps)
        {
            fields.AddRange(
            [
                new("paisResidencia", "País de residencia", ImportFieldType.Code, MaxLength: 3, Group: "RIPS", ColumnParser: CountryParser,
                    Aliases: ["pais", "pais residencia", "pais de residencia", "codigo pais"], Hint: "Código ISO numérico (Colombia = 170) o el nombre."),
                new("municipioResidencia", "Municipio de residencia (DANE)", ImportFieldType.Code, MaxLength: 5, Group: "RIPS", ColumnParser: MunicipalityParser,
                    Aliases: ["municipio residencia", "municipio de residencia", "codigo municipio", "cod municipio", "municipio dane", "codigo dane", "dane"],
                    Hint: "5 dígitos (Bogotá = 11001); un 0 inicial que Excel quitó se repone."),
                new("zonaResidencia", "Zona de residencia", ImportFieldType.Option, Group: "RIPS", Options: Zones,
                    Aliases: ["zona", "zona residencia", "zona de residencia", "zona territorial"]),
                new("tipoUsuario", "Régimen (tipo de usuario)", ImportFieldType.Option, Group: "RIPS", Options: UserTypes,
                    Aliases: ["regimen", "tipo usuario", "tipo de usuario", "tipo afiliacion", "tipo de afiliacion", "afiliacion"]),
                new("codigoEntidad", "Código de la EPS", ImportFieldType.Code, MaxLength: 10, Group: "RIPS",
                    Aliases: ["codigo eps", "cod eps", "codigo entidad", "codigo administradora", "eps codigo"]),
                new("nombreEntidad", "Nombre de la EPS", ImportFieldType.Text, MaxLength: 150, Group: "RIPS",
                    Aliases: ["eps", "entidad", "nombre eps", "aseguradora", "administradora", "nombre entidad"]),
            ]);
        }
        return fields;
    }

    public async Task ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var esIps = await RipsRules.TenantEsIpsAsync(db, ct);

        // Lo existente, por número de documento normalizado (a mano se pudo guardar con puntos)
        var existing = (await db.Patients.AsNoTracking()
                .Select(p => new { p.Id, p.Cedula, p.TipoDocumento, p.Nombre, p.Apellido })
                .ToListAsync(ct))
            .GroupBy(p => ImportText.Document(p.Cedula))
            .ToDictionary(g => g.Key, g => g.First());
        var firstRowByDocument = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            SplitFullName(row);
            if (row.Text("nombre") is null) row.Errors.Add("Falta el nombre (o el nombre completo).");
            if (row.Text("apellido") is null) row.Errors.Add("Falta el apellido (o el nombre completo).");

            var documento = row.Text("documento");
            var tipo = ParseEnum<TipoDocumento>(row.Text("tipoDocumento"));
            if (documento is null) continue;
            CheckDocumentFormat(row, documento, tipo);

            if (firstRowByDocument.TryGetValue(documento, out var first))
                row.Errors.Add($"El documento {documento} se repite en el archivo (también en la fila {first}).");
            else
                firstRowByDocument[documento] = row.Fila;

            if (existing.TryGetValue(documento, out var found))
            {
                if (tipo is not null && found.TipoDocumento is not null && found.TipoDocumento != tipo)
                    row.Errors.Add($"Ya existe {found.Nombre} {found.Apellido} con el número {documento} como {found.TipoDocumento}, no como {tipo}: revisa el tipo.");
                else
                {
                    row.ExistingId = found.Id;
                    row.ExistingLabel = $"{found.Nombre} {found.Apellido}".Trim();
                }
            }
        }

        // RIPS y formatos con las mismas reglas que el formulario, sobre el paciente como quedaría
        var existingIds = rows.Where(r => r.ExistingId is not null && r.Errors.Count == 0).Select(r => r.ExistingId!.Value).ToList();
        var current = existingIds.Count == 0
            ? []
            : await db.Patients.AsNoTracking().Where(p => existingIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var row in rows.Where(r => r.Errors.Count == 0))
        {
            var patient = row.ExistingId is { } id && current.TryGetValue(id, out var p) ? Clone(p) : new Patient();
            Apply(patient, row, isNew: row.ExistingId is null);
            try
            {
                RipsRules.ValidarFormatoPaciente(patient);
            }
            catch (ApiException e)
            {
                row.Errors.Add(e.Message);
                continue;
            }
            if (esIps)
            {
                var faltan = RipsRules.FaltantesPaciente(patient);
                if (faltan.Count > 0)
                    row.Errors.Add($"La clínica es IPS y RIPS exige: {string.Join(", ", faltan)}{(row.ExistingId is null ? "" : " (contando lo que ya tiene el paciente)")}.");
            }
        }
    }

    public async Task<ImportApplyResult> ApplyAsync(IReadOnlyList<ImportRow> rows, ImportApplyContext context, CancellationToken ct)
    {
        var mode = context.Mode;
        int creadas = 0, actualizadas = 0, omitidas = 0;
        var toUpdate = rows.Where(r => r.Status == ImportRowStatus.Duplicada).Select(r => r.ExistingId!.Value).ToList();
        var tracked = mode == DuplicateMode.Actualizar && toUpdate.Count > 0
            ? await db.Patients.Where(p => toUpdate.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];

        foreach (var row in rows)
        {
            switch (row.Status)
            {
                case ImportRowStatus.Valida:
                    var patient = new Patient();
                    Apply(patient, row, isNew: true);
                    db.Patients.Add(patient);
                    db.ClinicalRecords.Add(new ClinicalRecord { PatientId = patient.Id });
                    creadas++;
                    break;
                case ImportRowStatus.Duplicada when mode == DuplicateMode.Actualizar && tracked.TryGetValue(row.ExistingId!.Value, out var current):
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

    /// <summary>Copia al paciente los valores de la fila. Al actualizar solo toca lo que la fila trae (una celda vacía no borra nada).</summary>
    private static void Apply(Patient p, ImportRow row, bool isNew)
    {
        if (row.Text("nombre") is { } nombre) p.Nombre = nombre;
        if (row.Text("apellido") is { } apellido) p.Apellido = apellido;
        if (isNew && row.Text("documento") is { } documento) p.Cedula = documento;
        if (ParseEnum<TipoDocumento>(row.Text("tipoDocumento")) is { } tipo) p.TipoDocumento = tipo;
        if (row.Get<DateOnly>("fechaNacimiento") is { } nacimiento) p.FechaNacimiento = nacimiento;
        if (ParseEnum<Sexo>(row.Text("sexo")) is { } sexo) p.Sexo = sexo;
        if (row.Text("telefono") is { } telefono) p.Telefono = telefono;
        if (row.Text("email") is { } email) p.Email = email;
        if (row.Text("direccion") is { } direccion) p.Direccion = direccion;
        if (row.Text("ciudad") is { } ciudad) p.Ciudad = ciudad;
        if (row.Text("paisResidencia") is { } pais) p.PaisResidencia = RipsRules.Codigo(pais);
        if (row.Text("municipioResidencia") is { } municipio) p.MunicipioResidencia = RipsRules.Codigo(municipio);
        if (ParseEnum<ZonaResidencia>(row.Text("zonaResidencia")) is { } zona) p.ZonaResidencia = zona;
        if (ParseEnum<TipoUsuarioRips>(row.Text("tipoUsuario")) is { } regimen)
        {
            p.TipoUsuario = regimen;
            // Igual que el formulario: un régimen sin entidad (particular, no afiliado…) no guarda EPS
            if (!RipsRules.RequiereEntidad(regimen)) { p.CodigoEntidad = null; p.NombreEntidad = null; }
        }
        if (row.Text("codigoEntidad") is { } codigo && RipsRules.RequiereEntidad(p.TipoUsuario)) p.CodigoEntidad = RipsRules.Codigo(codigo);
        if (row.Text("nombreEntidad") is { } entidad && RipsRules.RequiereEntidad(p.TipoUsuario)) p.NombreEntidad = entidad;
    }

    private static Patient Clone(Patient p) => new()
    {
        Nombre = p.Nombre, Apellido = p.Apellido, Cedula = p.Cedula, Telefono = p.Telefono, Email = p.Email,
        FechaNacimiento = p.FechaNacimiento, Direccion = p.Direccion, Ciudad = p.Ciudad, TipoDocumento = p.TipoDocumento,
        Sexo = p.Sexo, PaisResidencia = p.PaisResidencia, MunicipioResidencia = p.MunicipioResidencia,
        ZonaResidencia = p.ZonaResidencia, TipoUsuario = p.TipoUsuario, CodigoEntidad = p.CodigoEntidad, NombreEntidad = p.NombreEntidad,
    };

    /// <summary>"Nombre completo" → nombre y apellido, solo si no vinieron aparte.</summary>
    private static void SplitFullName(ImportRow row)
    {
        if (row.Text("nombreCompleto") is not { } full || (row.Text("nombre") is not null && row.Text("apellido") is not null)) return;
        var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Las partículas van con lo que sigue: "María de los Ángeles" no se parte en "de"
        var parts = new List<string>();
        var pending = "";
        foreach (var w in words)
        {
            if (w is "de" or "del" or "la" or "las" or "los" or "y") { pending += w + " "; continue; }
            parts.Add(pending + w);
            pending = "";
        }
        if (parts.Count < 2)
        {
            row.Errors.Add($"«{full}» tiene una sola palabra: no se puede separar nombre y apellido.");
            return;
        }
        var names = parts.Count switch { 2 or 3 => 1, _ => 2 };
        var nombre = string.Join(' ', parts.Take(names));
        var apellido = string.Join(' ', parts.Skip(names));
        row.Values.TryAdd("nombre", nombre);
        row.Values.TryAdd("apellido", apellido);
        if (parts.Count is 3 or > 4)
            row.Warnings.Add($"Nombre completo: se tomó «{nombre}» como nombre y «{apellido}» como apellido. Revísalo si no es así.");
    }

    private static void CheckDocumentFormat(ImportRow row, string documento, TipoDocumento? tipo)
    {
        if (tipo is TipoDocumento.AS or TipoDocumento.MS or TipoDocumento.SI) return;
        if (tipo is { } t && NumericDocuments.Contains(t))
        {
            if (!documento.All(char.IsAsciiDigit)) row.Errors.Add($"Número de documento: una {t} lleva solo dígitos y vino «{documento}».");
            else if (documento.Length is < 4 or > 12) row.Errors.Add($"Número de documento: {documento} tiene {documento.Length} dígitos (una {t} tiene entre 4 y 12).");
        }
        else if (documento.Length is < 3 or > 20)
            row.Errors.Add($"Número de documento: «{documento}» tiene {documento.Length} caracteres (entre 3 y 20).");
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        value is not null && Enum.TryParse<T>(value, out var e) ? e : null;

    // ── Opciones: lo que puede venir en el archivo → valor del sistema (se compara sin tildes ni signos) ──

    private static readonly Dictionary<string, string> DocumentTypes = ImportOptions.Build(
        ("CC", ["cc", "c.c", "cedula", "cedula de ciudadania", "cedula ciudadania", "ciudadania"]),
        ("TI", ["ti", "t.i", "tarjeta de identidad", "tarjeta identidad"]),
        ("RC", ["rc", "registro civil", "registro civil de nacimiento"]),
        ("CE", ["ce", "c.e", "cedula de extranjeria", "cedula extranjeria", "extranjeria"]),
        ("PA", ["pa", "pasaporte", "pas", "pp"]),
        ("PT", ["pt", "ppt", "permiso por proteccion temporal", "proteccion temporal"]),
        ("PE", ["pe", "pep", "permiso especial de permanencia", "permiso especial"]),
        ("CD", ["cd", "carne diplomatico", "carnet diplomatico"]),
        ("SC", ["sc", "salvoconducto", "salvoconducto de permanencia"]),
        ("CN", ["cn", "certificado de nacido vivo", "nacido vivo"]),
        ("AS", ["as", "adulto sin identificacion", "adulto sin identificar"]),
        ("MS", ["ms", "menor sin identificacion", "menor sin identificar"]),
        ("DE", ["de", "documento extranjero", "documento de identificacion extranjero"]),
        ("SI", ["si", "sin identificacion"]));

    // Sin "m" sola: depende de la columna (SexParser)
    private static readonly Dictionary<string, string> Sexes = ImportOptions.Build(
        ("Hombre", ["hombre", "h", "masculino", "masc", "varon", "male"]),
        ("Mujer", ["mujer", "femenino", "fem", "f", "female"]),
        ("Indeterminado", ["indeterminado", "i", "intersexual", "otro", "no binario"]));

    private static readonly Dictionary<string, string> Zones = ImportOptions.Build(
        ("Urbana", ["urbana", "urbano", "u", "02", "2"]),
        ("Rural", ["rural", "r", "01", "1"]));

    private static readonly Dictionary<string, string> UserTypes = ImportOptions.Build(
        ("ContributivoCotizante", ["01", "1", "contributivo cotizante", "cotizante", "contributivo"]),
        ("ContributivoBeneficiario", ["02", "2", "contributivo beneficiario", "beneficiario"]),
        ("ContributivoAdicional", ["03", "3", "contributivo adicional", "adicional"]),
        ("Subsidiado", ["04", "4", "subsidiado"]),
        ("NoAfiliado", ["05", "5", "no afiliado", "sin afiliacion", "vinculado"]),
        ("EspecialCotizante", ["06", "6", "especial cotizante", "especial o excepcion cotizante", "excepcion cotizante"]),
        ("EspecialBeneficiario", ["07", "7", "especial beneficiario", "especial o excepcion beneficiario", "excepcion beneficiario"]),
        ("PrivadoLibertadFondoSalud", ["08", "8", "privado de la libertad", "fondo de salud", "ppl"]),
        ("TomadorArl", ["09", "9", "arl", "tomador arl"]),
        ("TomadorSoat", ["10", "soat", "tomador soat"]),
        ("TomadorPlanVoluntario", ["11", "plan voluntario", "tomador plan voluntario", "prepagada", "medicina prepagada"]),
        ("Particular", ["12", "particular", "privado"]),
        ("EspecialNoCotizante", ["13", "especial no cotizante", "especial o excepcion no cotizante"]));

    /// <summary>
    /// "M" es ambigua: Masculino en la costumbre M/F y Mujer en el código RIPS (H/M). Se decide por la
    /// columna: si trae alguna "F", M = Hombre; si trae alguna "H", M = Mujer; si solo trae "M", es error.
    /// </summary>
    private static Func<object?, ImportEngine.Parsed> SexParser(IReadOnlyList<object?> column)
    {
        var letters = column.Select(c => ImportText.Key(ImportText.Clean(c))).Where(k => k.Length == 1).ToHashSet();
        string? mMeans = letters.Contains("f") ? "Hombre" : letters.Contains("h") ? "Mujer" : null;
        var field = new ImportField("sexo", "Sexo", ImportFieldType.Option, Options: Sexes);
        return cell =>
        {
            var key = ImportText.Key(ImportText.Clean(cell));
            if (key != "m") return ImportEngine.ParseValue(field, cell, ImportDates.Order.Unknown, default);
            return mMeans is null
                ? new ImportEngine.Parsed(null, "«M» es ambigua (Masculino, o Mujer en RIPS): escribe Masculino/Femenino o H/M en la columna.")
                : new ImportEngine.Parsed(mMeans);
        };
    }

    private static Func<object?, ImportEngine.Parsed> CountryParser(IReadOnlyList<object?> column) => cell =>
    {
        var text = ImportText.Clean(cell);
        if (text is null) return new ImportEngine.Parsed(null);
        var key = ImportText.Key(text);
        if (key is "colombia" or "co" or "col") return new ImportEngine.Parsed(RipsRules.Colombia);
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is >= 1 and <= 3 && digits.Length == text.Trim().Length) return new ImportEngine.Parsed(digits.PadLeft(3, '0'));
        return new ImportEngine.Parsed(null, $"«{text}» no es un código de país (Colombia = 170).");
    };

    private static Func<object?, ImportEngine.Parsed> MunicipalityParser(IReadOnlyList<object?> column) => cell =>
    {
        var text = cell is double d ? d.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : ImportText.Clean(cell);
        if (text is null) return new ImportEngine.Parsed(null);
        if (text.All(char.IsAsciiDigit) && text.Length == 4)
            return new ImportEngine.Parsed("0" + text, null, $"«{text}» venía sin el 0 inicial (Excel lo quita): se leyó 0{text}.");
        return new ImportEngine.Parsed(text.Replace(" ", ""));
    };
}
