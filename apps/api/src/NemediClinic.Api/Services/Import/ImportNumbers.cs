using System.Globalization;
using System.Text.RegularExpressions;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Números como llegan en archivos de clínicas en Colombia: precios con signo y puntos de miles,
/// IVA con o sin %, "sí/no", duraciones ("1 h 30") y vigencias ("6 meses"). Cada función devuelve
/// el valor o un error legible; nunca lanza.
/// </summary>
public static partial class ImportNumbers
{
    public sealed record Result<T>(T? Value, string? Error = null) where T : struct;

    /// <summary>Tarifas de IVA vigentes en Colombia (las mismas del formulario).</summary>
    public static readonly decimal[] IvaRates = [0m, 5m, 19m];

    /// <summary>
    /// "$ 150.000", "150.000,50", "150,000", "1.250.000", 150000 (celda numérica). El separador decimal
    /// es el último de los dos que aparezca; si solo hay uno y lo siguen exactamente tres dígitos, es
    /// de miles ("150.000" = ciento cincuenta mil, como se escribe aquí).
    /// </summary>
    public static Result<decimal> Decimal(object? cell)
    {
        switch (cell)
        {
            case null: return new(null);
            case double d: return new((decimal)Math.Round(d, 4));
        }
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        var cleaned = Money().Replace(text, "");
        if (cleaned.Length == 0 || !Numeric().IsMatch(cleaned)) return new(null, $"«{text}» no es un número.");

        int lastDot = cleaned.LastIndexOf('.'), lastComma = cleaned.LastIndexOf(',');
        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            var dec = Math.Max(lastDot, lastComma);
            var thousands = dec == lastDot ? ',' : '.';
            normalized = cleaned.Replace(thousands.ToString(), "").Replace(cleaned[dec], '.');
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var count = cleaned.Count(c => c == sep);
            var after = cleaned.Length - cleaned.LastIndexOf(sep) - 1;
            normalized = count > 1 || after == 3 ? cleaned.Replace(sep.ToString(), "") : cleaned.Replace(sep, '.');
        }
        else normalized = cleaned;

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? new(value)
            : new(null, $"«{text}» no es un número.");
    }

    /// <summary>Precio o cantidad: número ≥ 0 (o &gt; 0).</summary>
    public static Result<decimal> NonNegative(object? cell, bool strictlyPositive = false)
    {
        var r = Decimal(cell);
        if (r.Value is not { } v) return r;
        if (v < 0) return new(null, $"«{ImportEngine.Display(cell)}» es negativo.");
        if (strictlyPositive && v == 0) return new(null, "debe ser mayor que cero.");
        return r;
    }

    /// <summary>"19", "19%", 0.19 (celda en formato porcentaje), "exento", "excluido", "no aplica" → 0. Solo 0, 5 o 19.</summary>
    public static Result<decimal> Iva(object? cell)
    {
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        var key = ImportText.Key(text);
        if (key is "exento" or "excluido" or "no aplica" or "na" or "sin iva" or "no") return new(0m);
        var r = Decimal(cell is double ? cell : text.Replace("%", ""));
        if (r.Value is not { } v) return new(null, $"«{text}» no es una tarifa de IVA (0, 5 o 19).");
        if (v is > 0 and < 1) v *= 100; // 0,19 de una celda con formato de porcentaje
        v = Math.Round(v, 2);
        return IvaRates.Contains(v) ? new(v) : new(null, $"«{text}» no es una tarifa de IVA vigente (0, 5 o 19).");
    }

    /// <summary>Sí: "sí", "si", "s", "x", "1", "verdadero", "true", "yes". No: "no", "n", "0", "falso", "false".</summary>
    public static Result<bool> Boolean(object? cell)
    {
        if (cell is bool b) return new(b);
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        return ImportText.Key(text) switch
        {
            "si" or "s" or "x" or "1" or "verdadero" or "true" or "yes" or "y" => new(true),
            "no" or "n" or "0" or "falso" or "false" => new(false),
            _ => new(null, $"«{text}» no es sí o no."),
        };
    }

    /// <summary>Minutos: 60, "60 min", "1 h", "1 hora", "1:30", "1h30", "1 h 30 min", "1,5 h", "90 minutos".</summary>
    public static Result<int> Minutes(object? cell)
    {
        if (cell is double d) return Whole(d, cell);
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        var t = text.ToLowerInvariant().Replace(" ", "");
        var colon = Clock().Match(t);
        if (colon.Success) return new(int.Parse(colon.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(colon.Groups[2].Value, CultureInfo.InvariantCulture));
        var hm = HoursMinutes().Match(t);
        if (hm.Success && (hm.Groups["h"].Success || hm.Groups["m"].Success))
        {
            var hours = hm.Groups["h"].Success ? Decimal(hm.Groups["h"].Value).Value ?? 0 : 0;
            var minutes = hm.Groups["m"].Success ? int.Parse(hm.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            return new((int)Math.Round(hours * 60) + minutes);
        }
        return new(null, $"«{text}» no es una duración (escribe los minutos, p. ej. 60, o 1 h 30).");
    }

    /// <summary>Días: 180, "180 días", "6 meses" (30 días cada uno), "1 año" (365), "2 semanas".</summary>
    public static Result<int> Days(object? cell)
    {
        if (cell is double d) return Whole(d, cell);
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        var m = Period().Match(ImportText.Key(text));
        if (!m.Success) return new(null, $"«{text}» no es un número de días (p. ej. 180, o 6 meses).");
        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Value;
        return new(unit switch
        {
            var u when u.StartsWith("mes") => n * 30,
            var u when u.StartsWith("ano") || u.StartsWith("year") => n * 365,
            var u when u.StartsWith("semana") || u.StartsWith("week") => n * 7,
            _ => n,
        });
    }

    /// <summary>Entero: 8, "8 sesiones", "8.0".</summary>
    public static Result<int> Count(object? cell)
    {
        if (cell is double d) return Whole(d, cell);
        var text = ImportText.Clean(cell);
        if (text is null) return new(null);
        var m = LeadingInteger().Match(text);
        return m.Success ? new(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : new(null, $"«{text}» no es un número entero.");
    }

    private static Result<int> Whole(double d, object? cell) =>
        d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue
            ? new((int)d)
            : new(null, $"«{ImportEngine.Display(cell)}» no es un número entero.");

    [GeneratedRegex(@"[\s$]|cop|col\$?", RegexOptions.IgnoreCase)]
    private static partial Regex Money();
    [GeneratedRegex(@"^-?[0-9][0-9.,]*$")]
    private static partial Regex Numeric();
    [GeneratedRegex(@"^(\d{1,2}):(\d{2})(?::\d{2})?$")]
    private static partial Regex Clock();
    [GeneratedRegex(@"^(?:(?<h>\d+(?:[.,]\d+)?)(?:h|hr|hrs|hora|horas))?(?:(?<m>\d+)(?:m|min|mins|minuto|minutos)?)?$")]
    private static partial Regex HoursMinutes();
    [GeneratedRegex(@"^(\d+)\s*(dias?|d|mes|meses|anos?|years?|semanas?|weeks?)?$")]
    private static partial Regex Period();
    [GeneratedRegex(@"^\s*(\d+)(?:[.,]0+)?(?:\s*[a-zA-ZáéíóúÁÉÍÓÚñÑ]+)?\s*$")]
    private static partial Regex LeadingInteger();
}
