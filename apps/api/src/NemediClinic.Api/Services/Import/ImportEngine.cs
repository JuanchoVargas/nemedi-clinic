using System.Globalization;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// La parte del importador que no sabe de ninguna entidad: encontrar la fila de encabezados, proponer
/// qué columna va a qué campo y convertir cada celda según el tipo del campo. Puro (sin base ni red):
/// se prueba con archivos armados en memoria.
/// </summary>
public static class ImportEngine
{
    public const int MaxRows = 5000;
    private const int HeaderScanRows = 30;
    private const double MatchThreshold = 0.8;

    /// <param name="Titled">false = la columna no tiene título ("Columna C"): no se asigna sola.</param>
    public sealed record Column(int Index, string Header, IReadOnlyList<string> Samples, bool Titled = true);

    /// <summary>
    /// La fila de encabezados (índice 0-based en la hoja). Los archivos de clínicas suelen traer título,
    /// logo o fecha arriba: se miran las primeras 30 filas y gana la que más encabezados reconoce (por
    /// los alias de los campos); a igualdad, la que tiene más celdas de texto. Si ninguna reconoce nada,
    /// la primera con al menos dos celdas de texto seguida de una fila con datos.
    /// </summary>
    public static int DetectHeaderRow(IReadOnlyList<object?[]> rows, IReadOnlyList<ImportField> fields)
    {
        var best = -1;
        var bestScore = 0;
        var limit = Math.Min(rows.Count, HeaderScanRows);
        for (var r = 0; r < limit; r++)
        {
            var texts = rows[r].Select(ImportText.Clean).Where(t => t is not null && !double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _)).ToList();
            if (texts.Count < 2) continue;
            var recognized = fields.Count(f => texts.Any(t => Score(ImportText.Key(t), f) >= MatchThreshold));
            var score = recognized * 100 + texts.Count;
            if (recognized > 0 && score > bestScore)
            {
                best = r;
                bestScore = score;
            }
        }
        if (best >= 0) return best;
        for (var r = 0; r < limit - 1; r++)
        {
            var texts = rows[r].Count(c => ImportText.Clean(c) is not null);
            if (texts >= 2 && rows[r + 1].Any(c => ImportText.Clean(c) is not null)) return r;
        }
        return 0;
    }

    /// <summary>Las columnas con encabezado (o con datos) y tres ejemplos de cada una.</summary>
    public static List<Column> Columns(IReadOnlyList<object?[]> rows, int headerRow)
    {
        var data = DataRows(rows, headerRow).Take(200).ToList();
        var width = Math.Max(rows.Count > headerRow ? rows[headerRow].Length : 0, data.Count == 0 ? 0 : data.Max(d => d.Row.Length));
        var columns = new List<Column>();
        for (var c = 0; c < width; c++)
        {
            var header = rows.Count > headerRow && c < rows[headerRow].Length ? ImportText.Clean(rows[headerRow][c]) : null;
            var samples = data.Select(d => c < d.Row.Length ? Display(d.Row[c]) : null).Where(s => s is not null).Take(3).Select(s => s!).ToList();
            if (header is null && samples.Count == 0) continue;
            columns.Add(new Column(c, header ?? $"Columna {ColumnLetter(c)}", samples, header is not null));
        }
        return columns;
    }

    /// <summary>Filas de datos: las que siguen al encabezado y tienen al menos una celda con algo. Fila = número en la hoja (1-based).</summary>
    public static IEnumerable<(int Fila, object?[] Row)> DataRows(IReadOnlyList<object?[]> rows, int headerRow)
    {
        for (var r = headerRow + 1; r < rows.Count; r++)
            if (rows[r].Any(c => ImportText.Clean(c) is not null)) yield return (r + 1, rows[r]);
    }

    /// <summary>
    /// Mapeo propuesto: campo → índice de columna. Cada encabezado se compara (sin tildes, mayúsculas ni
    /// signos) con la etiqueta y los alias de cada campo; se asignan primero los pares más parecidos y
    /// ni un campo ni una columna se usan dos veces. Lo que no llega a 0,8 de parecido queda sin asignar.
    /// </summary>
    public static Dictionary<string, int> SuggestMapping(IReadOnlyList<Column> columns, IReadOnlyList<ImportField> fields)
    {
        var candidates = new List<(string Field, int Column, double Score)>();
        foreach (var col in columns.Where(c => c.Titled))
        {
            var key = ImportText.Key(col.Header);
            foreach (var f in fields)
            {
                var s = Score(key, f);
                if (s >= MatchThreshold) candidates.Add((f.Key, col.Index, s));
            }
        }
        var mapping = new Dictionary<string, int>();
        var usedColumns = new HashSet<int>();
        foreach (var (field, column, _) in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Column))
        {
            if (mapping.ContainsKey(field) || usedColumns.Contains(column)) continue;
            mapping[field] = column;
            usedColumns.Add(column);
        }
        return mapping;
    }

    /// <summary>Parecido de un encabezado ya normalizado con un campo: el mejor entre su etiqueta y sus alias.</summary>
    public static double Score(string headerKey, ImportField field)
    {
        if (headerKey.Length == 0) return 0;
        var best = ImportText.Similarity(headerKey, ImportText.Key(field.Label));
        foreach (var alias in field.Aliases ?? [])
        {
            var aliasKey = ImportText.Key(alias);
            // Un alias exacto gana a uno parecido: "cedula" es documento aunque se parezca a otra cosa
            var s = headerKey == aliasKey ? 1.0 : ImportText.Similarity(headerKey, aliasKey);
            if (s > best) best = s;
        }
        return best;
    }

    /// <summary>
    /// Lee las filas con el mapeo: cada valor se limpia según el tipo de su campo y queda en Values; lo
    /// que no se puede leer va a Errors, lo que se leyó con una suposición a Warnings. Los obligatorios
    /// vacíos también son error. Las reglas propias de la entidad las agrega después su definición.
    /// </summary>
    public static List<ImportRow> ReadRows(IReadOnlyList<object?[]> rows, int headerRow, IReadOnlyDictionary<string, int> mapping,
        IReadOnlyList<ImportField> fields, DateOnly today)
    {
        var data = DataRows(rows, headerRow).ToList();
        if (data.Count > MaxRows)
            throw new ImportFileException(400, $"El archivo tiene {data.Count:N0} filas y el máximo es {MaxRows:N0} por archivo. Pártelo en varios y súbelos uno por uno.");

        // Día/mes o mes/día se decide mirando la columna entera, no fecha por fecha; igual los campos con ColumnParser
        var dateOrders = fields.Where(f => f.Type == ImportFieldType.Date && mapping.ContainsKey(f.Key))
            .ToDictionary(f => f.Key, f => ImportDates.DetectOrder(data.Select(d => Cell(d.Row, mapping[f.Key]))));
        var columnParsers = fields.Where(f => f.ColumnParser is not null && mapping.ContainsKey(f.Key))
            .ToDictionary(f => f.Key, f => f.ColumnParser!(data.Select(d => Cell(d.Row, mapping[f.Key])).ToList()));

        var result = new List<ImportRow>(data.Count);
        foreach (var (fila, raw) in data)
        {
            var row = new ImportRow { Fila = fila, Raw = raw };
            foreach (var field in fields)
            {
                if (!mapping.TryGetValue(field.Key, out var column))
                {
                    if (field.Required) row.Errors.Add($"Falta {field.Label}: la columna no está asignada.");
                    continue;
                }
                var cell = Cell(raw, column);
                var parsed = columnParsers.TryGetValue(field.Key, out var parse)
                    ? parse(cell)
                    : ParseValue(field, cell, dateOrders.GetValueOrDefault(field.Key, ImportDates.Order.Unknown), today);
                if (parsed.Error is not null) row.Errors.Add($"{field.Label}: {parsed.Error}");
                else if (parsed.Value is null && field.Required) row.Errors.Add($"{field.Label} está vacío.");
                if (parsed.Warning is not null) row.Warnings.Add($"{field.Label}: {parsed.Warning}");
                if (parsed.Value is not null) row.Values[field.Key] = parsed.Value;
            }
            result.Add(row);
        }
        return result;
    }

    public sealed record Parsed(object? Value, string? Error = null, string? Warning = null);

    public static Parsed ParseValue(ImportField field, object? cell, ImportDates.Order dateOrder, DateOnly today)
    {
        switch (field.Type)
        {
            case ImportFieldType.Date:
                var d = ImportDates.Parse(cell, dateOrder, today, field.Expiry);
                return new Parsed(d.Value, d.Error, d.Warning);
            case ImportFieldType.Money:
                return FromNumber(ImportNumbers.NonNegative(cell));
            case ImportFieldType.Quantity:
                return FromNumber(ImportNumbers.NonNegative(cell, strictlyPositive: true));
            case ImportFieldType.Iva:
                return FromNumber(ImportNumbers.Iva(cell));
            case ImportFieldType.Boolean:
                var b = ImportNumbers.Boolean(cell);
                return new Parsed(b.Value, b.Error);
            case ImportFieldType.Minutes:
                return Bounded(ImportNumbers.Minutes(cell), field, "minutos");
            case ImportFieldType.Days:
                return Bounded(ImportNumbers.Days(cell), field, "días");
            case ImportFieldType.Count:
                return Bounded(ImportNumbers.Count(cell), field, null);
        }

        var text = field.Type == ImportFieldType.Phone && cell is double phone
            ? phone.ToString("0", CultureInfo.InvariantCulture)
            : field.Type == ImportFieldType.Document && cell is double doc
                ? doc.ToString("0", CultureInfo.InvariantCulture)
                : ImportText.Clean(cell);
        if (text is null) return new Parsed(null);

        string value;
        switch (field.Type)
        {
            case ImportFieldType.Name:
                value = ImportText.Name(text);
                break;
            case ImportFieldType.Document:
                value = ImportText.Document(text);
                if (value.Length == 0) return new Parsed(null, $"«{text}» no es un número de documento.");
                break;
            case ImportFieldType.Phone:
                value = ImportText.Phone(text);
                if (value.Length is < 7 or > 15) return new Parsed(null, $"«{text}» no parece un teléfono (7 a 15 dígitos).");
                break;
            case ImportFieldType.Email:
                value = text.ToLowerInvariant();
                if (!ImportText.IsEmail(value)) return new Parsed(null, $"«{text}» no es un correo válido.");
                break;
            case ImportFieldType.Code:
                value = text.Replace(" ", "").ToUpperInvariant();
                break;
            case ImportFieldType.Option:
                var key = ImportText.Key(text);
                var match = field.Options?.FirstOrDefault(o => ImportText.Key(o.Key) == key).Value;
                if (match is null) return new Parsed(null, $"«{text}» no es un valor válido.");
                return new Parsed(match);
            default:
                value = text;
                break;
        }
        if (value.Length > field.MaxLength) return new Parsed(null, $"tiene {value.Length} caracteres y el máximo es {field.MaxLength}.");
        return new Parsed(value);
    }

    private static Parsed FromNumber(ImportNumbers.Result<decimal> r) => new(r.Value, r.Error);

    private static Parsed Bounded(ImportNumbers.Result<int> r, ImportField field, string? unit)
    {
        if (r.Value is not { } v) return new Parsed(null, r.Error);
        if (v <= 0) return new Parsed(null, "debe ser mayor que cero.");
        if (v > field.Max) return new Parsed(null, $"{v}{(unit is null ? "" : " " + unit)} pasa del máximo ({field.Max}).");
        return new Parsed(v);
    }

    private static object? Cell(object?[] row, int column) => column >= 0 && column < row.Length ? row[column] : null;

    /// <summary>Cómo se muestra una celda en la pantalla y en el Excel de errores.</summary>
    public static string? Display(object? value) => value switch
    {
        DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        double d => d.ToString(d == Math.Floor(d) ? "0" : "0.##########", CultureInfo.InvariantCulture),
        _ => ImportText.Clean(value),
    };

    public static string ColumnLetter(int index)
    {
        var letters = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) letters = (char)('A' + (n - 1) % 26) + letters;
        return letters;
    }
}
