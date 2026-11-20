namespace NemediClinic.Api.Services.Import;

/// <summary>Cómo se lee y se limpia el valor de un campo (ImportEngine.ParseValue).</summary>
public enum ImportFieldType
{
    /// <summary>Texto con los espacios limpios.</summary>
    Text,
    /// <summary>Nombre propio: capitalización consistente ("MARÍA JOSÉ" → "María José").</summary>
    Name,
    /// <summary>Número de documento: sin puntos, guiones ni espacios.</summary>
    Document,
    /// <summary>Uno de los valores de <see cref="ImportField.Options"/> (por código o por nombre).</summary>
    Option,
    Date,
    /// <summary>Solo dígitos, sin el +57 de un celular.</summary>
    Phone,
    Email,
    /// <summary>Código: sin espacios, en mayúsculas (DANE, EPS, país).</summary>
    Code,
    /// <summary>Precio en pesos ≥ 0: "$ 150.000", "150.000,50", celda numérica (decimal).</summary>
    Money,
    /// <summary>Cantidad &gt; 0, con decimales (stock de un lote).</summary>
    Quantity,
    /// <summary>IVA: 0, 5 o 19 ("19 %", 0,19, "exento" = 0). Vacío = el de la clínica (decimal).</summary>
    Iva,
    /// <summary>Sí / no (bool).</summary>
    Boolean,
    /// <summary>Duración en minutos: 60, "1 h 30", "1:30" (int).</summary>
    Minutes,
    /// <summary>Días: 180, "6 meses", "1 año" (int).</summary>
    Days,
    /// <summary>Entero &gt; 0: "8", "8 sesiones" (int).</summary>
    Count,
}

/// <summary>
/// Un campo del sistema que se puede llenar desde una columna del archivo. Es todo lo que el motor
/// necesita saber de una entidad para leer, limpiar, proponer el mapeo y validar lo básico; las reglas
/// del negocio (duplicados, RIPS…) son de la definición de la entidad (<see cref="IImportDefinition"/>).
/// </summary>
/// <param name="Key">Clave estable (en inglés o en el dominio, sin espacios): la usa el mapeo.</param>
/// <param name="Aliases">Encabezados con los que suele venir ("celular", "cel", "teléfono"…).</param>
/// <param name="Options">Para <see cref="ImportFieldType.Option"/>: lo que puede venir en el archivo → valor del sistema.</param>
/// <param name="MaxLength">Más largo → error de la fila.</param>
/// <param name="Group">Para agrupar en la pantalla de mapeo ("RIPS").</param>
/// <param name="ColumnParser">
/// Para un campo cuyo significado depende de toda la columna (como las fechas día/mes): recibe todas
/// las celdas de la columna y devuelve cómo leer cada una. P. ej. el sexo: "M" es Masculino si la
/// columna también trae "F", y Mujer (código RIPS) si trae "H".
/// </param>
/// <param name="Expiry">Fecha de vencimiento: puede estar en el futuro y acepta mes/año (ImportDates.Parse).</param>
/// <param name="Max">Para Minutes, Days y Count: tope (error si lo pasa).</param>
public sealed record ImportField(
    string Key,
    string Label,
    ImportFieldType Type,
    bool Required = false,
    string[]? Aliases = null,
    IReadOnlyDictionary<string, string>? Options = null,
    int MaxLength = 200,
    string? Hint = null,
    string? Group = null,
    Func<IReadOnlyList<object?>, Func<object?, ImportEngine.Parsed>>? ColumnParser = null,
    bool Expiry = false,
    int Max = int.MaxValue);

public enum ImportRowStatus { Valida, Error, Duplicada }

/// <summary>Una fila de datos del archivo ya leída con el mapeo.</summary>
public sealed class ImportRow
{
    /// <summary>Número de fila en el archivo (1 = la primera fila de la hoja), para que la persona la encuentre.</summary>
    public int Fila { get; init; }
    /// <summary>Las celdas como venían (para el Excel de errores).</summary>
    public required object?[] Raw { get; init; }
    /// <summary>Valor ya limpio por clave de campo (string, DateOnly…); un campo vacío no está.</summary>
    public Dictionary<string, object?> Values { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    /// <summary>Registro existente con la misma clave (para omitir o actualizar).</summary>
    public Guid? ExistingId { get; set; }
    public string? ExistingLabel { get; set; }

    public ImportRowStatus Status => Errors.Count > 0 ? ImportRowStatus.Error
        : ExistingId is not null ? ImportRowStatus.Duplicada
        : ImportRowStatus.Valida;

    public string? Text(string key) => Values.TryGetValue(key, out var v) ? v as string : null;
    public T? Get<T>(string key) where T : struct => Values.TryGetValue(key, out var v) && v is T t ? t : null;
}

public enum DuplicateMode { Omitir, Actualizar }

/// <param name="Detalle">Lo que no cabe en los tres números: "12 lotes con 340 unidades".</param>
public sealed record ImportApplyResult(int Creadas, int Actualizadas, int Omitidas, string? Detalle = null);

/// <summary>Con qué se guarda: modo de duplicados, columnas asignadas y quién importa (las entradas de inventario lo llevan).</summary>
public sealed record ImportApplyContext(DuplicateMode Mode, IReadOnlyCollection<string> MappedKeys, Guid UserId, DateTime Now);

/// <summary>
/// Cuántos registros se escribirían (no filas: varias filas pueden ser un solo producto o paquete). La
/// vista previa lo muestra y el botón "Importar N …" lo usa.
/// </summary>
public sealed record ImportPlan(int Nuevos, int Existentes, string? Detalle = null);

/// <summary>
/// Lo que cada entidad importable aporta al motor genérico (hoy: pacientes; después productos,
/// procedimientos y paquetes). El motor lee el archivo, detecta el encabezado, propone el mapeo y
/// limpia cada valor según su tipo; la definición pone los campos, las reglas propias de la entidad y
/// el guardado. Se registra en Program.cs como IImportDefinition y queda disponible en /imports/{Entidad}.
/// </summary>
public interface IImportDefinition
{
    /// <summary>Segmento de la URL: "patients".</summary>
    string Entidad { get; }
    /// <summary>Para el historial: "Pacientes".</summary>
    string Nombre { get; }

    /// <summary>Los campos (pueden depender de la clínica: los de RIPS solo si es IPS).</summary>
    Task<IReadOnlyList<ImportField>> GetFieldsAsync(CancellationToken ct);

    /// <summary>
    /// Reglas de la entidad sobre las filas ya limpias: obligatorios cruzados, RIPS, duplicados dentro
    /// del archivo y contra lo que existe (marca ExistingId). No guarda nada.
    /// </summary>
    Task ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct);

    /// <summary>Crea o actualiza las filas válidas (y las duplicadas si el modo es Actualizar). Sin SaveChanges: lo hace el motor dentro de su transacción.</summary>
    Task<ImportApplyResult> ApplyAsync(IReadOnlyList<ImportRow> rows, ImportApplyContext context, CancellationToken ct);

    /// <summary>Después del primer SaveChanges, dentro de la misma transacción (p. ej. recalcular el stock desde los lotes guardados).</summary>
    Task AfterSaveAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Avisos sobre el archivo entero según lo asignado (p. ej. "sin cantidad: el stock se carga aparte").</summary>
    IReadOnlyList<string> Notices(IReadOnlyCollection<string> mappedKeys) => [];

    /// <summary>Registros que se escribirían. Por defecto, una fila = un registro.</summary>
    ImportPlan Plan(IReadOnlyList<ImportRow> rows) =>
        new(rows.Count(r => r.Status == ImportRowStatus.Valida), rows.Count(r => r.Status == ImportRowStatus.Duplicada));
}

/// <summary>Opciones de un campo Option: lo que puede venir en el archivo → valor del sistema (se compara sin tildes ni signos).</summary>
public static class ImportOptions
{
    public static Dictionary<string, string> Build(params (string Value, string[] Aliases)[] options)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (value, aliases) in options)
        {
            dict[value] = value;
            foreach (var alias in aliases) dict[alias] = value;
        }
        return dict;
    }
}
