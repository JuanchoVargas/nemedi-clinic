using System.Globalization;
using System.Text.RegularExpressions;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Fechas como llegan en archivos de clínicas en Colombia. Formatos que entiende (<see cref="Formats"/>):
///   1. Celda con formato de fecha de Excel (.xlsx/.xls).
///   2. Serial de Excel: número (32952) o texto con solo ese número.
///   3. dd/mm/aaaa y d/m/aaaa         4. dd-mm-aaaa y d-m-aaaa       5. dd.mm.aaaa
///   6. dd/mm/aa, d-m-aa, dd.mm.aa (año de dos dígitos)
///   7. aaaa-mm-dd, aaaa/mm/dd, aaaa.mm.dd (ISO)
///   8. Con el mes en letras: "15 de marzo de 1990", "15 mar 1990", "15-mar-90", "marzo 15 de 1990"
///      (español e inglés, completo o abreviado)
///   9. Ocho dígitos seguidos: ddmmaaaa (15031990) o aaaammdd (19900315)
///  10. Cualquiera de los anteriores con hora detrás ("15/03/1990 00:00:00", "… 12:00 a. m.")
///
/// Ambigüedad día/mes: en Colombia se escribe día/mes. Si en la columna alguna fecha tiene el primer
/// número mayor que 12, todas se leen día/mes sin aviso; si es el segundo el que pasa de 12, la columna
/// viene mes/día (exportada de un sistema en inglés) y se avisa; si ninguna lo resuelve, cada fecha con
/// los dos números ≤ 12 y distintos se lee día/mes y se marca como ambigua. El año de dos dígitos se toma
/// del siglo actual salvo que quede en el futuro (90 → 1990, 05 → 2005) y también se avisa.
/// </summary>
public static partial class ImportDates
{
    public static readonly string[] Formats =
    [
        "Celda de fecha de Excel",
        "Serial de Excel (32952)",
        "dd/mm/aaaa y d/m/aaaa",
        "dd-mm-aaaa y d-m-aaaa",
        "dd.mm.aaaa",
        "Año de dos dígitos: dd/mm/aa, d-m-aa, dd.mm.aa",
        "ISO: aaaa-mm-dd, aaaa/mm/dd",
        "Mes en letras: 15 de marzo de 1990, 15 mar 1990, 15-mar-90, marzo 15 de 1990",
        "Ocho dígitos: ddmmaaaa o aaaammdd",
        "Cualquiera de las anteriores con la hora detrás",
    ];

    public enum Order { DayFirst, MonthFirst, Unknown }

    public sealed record Result(DateOnly? Value, string? Error, string? Warning);

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enero"] = 1, ["ene"] = 1, ["january"] = 1, ["jan"] = 1,
        ["febrero"] = 2, ["feb"] = 2, ["february"] = 2,
        ["marzo"] = 3, ["mar"] = 3, ["march"] = 3,
        ["abril"] = 4, ["abr"] = 4, ["april"] = 4, ["apr"] = 4,
        ["mayo"] = 5, ["may"] = 5,
        ["junio"] = 6, ["jun"] = 6, ["june"] = 6,
        ["julio"] = 7, ["jul"] = 7, ["july"] = 7,
        ["agosto"] = 8, ["ago"] = 8, ["august"] = 8, ["aug"] = 8,
        ["septiembre"] = 9, ["setiembre"] = 9, ["sep"] = 9, ["sept"] = 9, ["set"] = 9, ["september"] = 9,
        ["octubre"] = 10, ["oct"] = 10, ["october"] = 10,
        ["noviembre"] = 11, ["nov"] = 11, ["november"] = 11,
        ["diciembre"] = 12, ["dic"] = 12, ["december"] = 12, ["dec"] = 12,
    };

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Mira toda la columna: ¿día/mes, mes/día, o no se puede saber?</summary>
    public static Order DetectOrder(IEnumerable<object?> values)
    {
        var dayFirst = false;
        var monthFirst = false;
        foreach (var value in values)
        {
            if (value is not string s) continue;
            var m = Numeric().Match(StripTime(s.Trim()));
            if (!m.Success) continue;
            var a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (a > 12 && b <= 12) dayFirst = true;
            if (b > 12 && a <= 12) monthFirst = true;
        }
        if (dayFirst) return Order.DayFirst; // con evidencias contradictorias manda la costumbre colombiana
        return monthFirst ? Order.MonthFirst : Order.Unknown;
    }

    /// <param name="expiry">
    /// Fecha de vencimiento (de un lote): puede estar en el futuro, el año de dos dígitos es de este siglo
    /// ("03/04/28" = 2028) y se acepta mes/año ("03/2027", "mar-2027") como el último día de ese mes.
    /// </param>
    public static Result Parse(object? value, Order order, DateOnly today, bool expiry = false)
    {
        switch (value)
        {
            case null: return new(null, null, null);
            case DateTime dt: return Check(DateOnly.FromDateTime(dt), today, null, expiry);
            case double d: return FromNumber(d, today, expiry);
        }
        var text = ImportText.Clean(value);
        if (text is null) return new(null, null, null);
        text = StripTime(text);

        if (expiry)
        {
            var my = MonthYear().Match(text);
            if (my.Success)
            {
                int month;
                if (my.Groups["mn"].Success)
                {
                    if (!Months.TryGetValue(my.Groups["mn"].Value.TrimEnd('.'), out month)) return new(null, $"Fecha ilegible: «{text}».", null);
                }
                else month = int.Parse(my.Groups["m"].Value, CultureInfo.InvariantCulture);
                if (month is < 1 or > 12) return new(null, $"Fecha inexistente: «{text}».", null);
                var (y, _) = Year(my.Groups["y"].Value, today, expiry);
                return Check(new DateOnly(y, month, DateTime.DaysInMonth(y, month)), today, $"«{text}» no trae el día: se tomó el último día del mes.", expiry);
            }
        }

        // Un número solo: serial de Excel (en un CSV) u ocho dígitos seguidos
        if (OnlyDigits().IsMatch(text))
        {
            if (text.Length == 8) return EightDigits(text, today, expiry);
            if (text.Length is >= 4 and <= 5) return FromNumber(double.Parse(text, CultureInfo.InvariantCulture), today, expiry);
            return new(null, $"Fecha ilegible: «{text}».", null);
        }

        var iso = IsoDate().Match(text);
        if (iso.Success)
            return Build(Int(iso.Groups[1]), Int(iso.Groups[2]), Int(iso.Groups[3]), today, text, null, expiry);

        var num = Numeric().Match(text);
        if (num.Success)
        {
            int a = Int(num.Groups[1]), b = Int(num.Groups[2]);
            var (year, yearWarning) = Year(num.Groups[3].Value, today, expiry);
            int day, month;
            string? warning = yearWarning;
            if (a > 12) (day, month) = (a, b);
            else if (b > 12) { (day, month) = (b, a); warning = Join(warning, "Venía como mes/día (formato en inglés): se leyó como " + Describe(year, month, day) + "."); }
            else if (order == Order.MonthFirst)
            {
                (day, month) = (b, a);
                if (a != b) warning = Join(warning, "La columna viene mes/día: se leyó como " + Describe(year, month, day) + ".");
            }
            else
            {
                (day, month) = (a, b);
                if (a != b && order == Order.Unknown)
                    warning = Join(warning, $"Fecha ambigua: «{text}» se leyó día/mes, como {Describe(year, month, day)}. Si era mes/día, corrígela en el archivo.");
            }
            return Build(year, month, day, today, text, warning, expiry);
        }

        var words = WithMonthName().Match(text);
        if (words.Success)
        {
            var dayText = words.Groups["d1"].Success ? words.Groups["d1"].Value : words.Groups["d2"].Value;
            var monthText = (words.Groups["m1"].Success ? words.Groups["m1"].Value : words.Groups["m2"].Value).TrimEnd('.');
            if (!Months.TryGetValue(monthText, out var month)) return new(null, $"Fecha ilegible: «{text}» (mes «{monthText}»).", null);
            var (year, yearWarning) = Year(words.Groups["y"].Value, today, expiry);
            return Build(year, month, int.Parse(dayText, CultureInfo.InvariantCulture), today, text, yearWarning, expiry);
        }

        return new(null, $"Fecha ilegible: «{text}». Usa día/mes/año (15/03/1990).", null);
    }

    private static Result FromNumber(double serial, DateOnly today, bool expiry)
    {
        if (serial >= 1_000_000 && serial == Math.Floor(serial))
            return EightDigits(((long)serial).ToString(CultureInfo.InvariantCulture).PadLeft(8, '0'), today, expiry);
        // Serial de Excel: días desde 1899-12-30 (el 60 es el 29/02/1900 que no existió)
        if (serial < 1 || serial > 2_958_465) return new(null, $"Fecha ilegible: «{serial.ToString(CultureInfo.InvariantCulture)}».", null);
        return Check(DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial))), today, null, expiry);
    }

    private static Result EightDigits(string text, DateOnly today, bool expiry)
    {
        // aaaammdd si empieza por 19 o 20 y forma una fecha válida; si no, ddmmaaaa
        int y1 = int.Parse(text[..4], CultureInfo.InvariantCulture), m1 = int.Parse(text[4..6], CultureInfo.InvariantCulture), d1 = int.Parse(text[6..], CultureInfo.InvariantCulture);
        if (y1 is >= 1900 and <= 2100 && Valid(y1, m1, d1)) return Build(y1, m1, d1, today, text, null, expiry);
        return Build(int.Parse(text[4..], CultureInfo.InvariantCulture), int.Parse(text[2..4], CultureInfo.InvariantCulture),
            int.Parse(text[..2], CultureInfo.InvariantCulture), today, text, null, expiry);
    }

    private static (int Year, string? Warning) Year(string text, DateOnly today, bool expiry = false)
    {
        var y = int.Parse(text, CultureInfo.InvariantCulture);
        if (text.Length != 2) return (y, null);
        var century = today.Year / 100 * 100;
        var full = century + y;
        // Nacimiento: nunca en el futuro. Vencimiento: de este siglo
        if (!expiry && full > today.Year) full -= 100;
        return (full, $"Año de dos dígitos: «{text}» se leyó como {full}.");
    }

    private static Result Build(int year, int month, int day, DateOnly today, string text, string? warning, bool expiry) =>
        Valid(year, month, day)
            ? Check(new DateOnly(year, month, day), today, warning, expiry)
            : new(null, $"Fecha inexistente: «{text}».", null);

    private static Result Check(DateOnly date, DateOnly today, string? warning, bool expiry = false)
    {
        if (!expiry && date > today) return new(null, $"Fecha en el futuro: {Describe(date.Year, date.Month, date.Day)}.", null);
        if (date.Year < 1900) return new(null, $"Fecha demasiado antigua: {Describe(date.Year, date.Month, date.Day)}.", null);
        return new(date, null, warning);
    }

    private static bool Valid(int y, int m, int d) => y is >= 1 and <= 9999 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m);

    private static string Describe(int y, int m, int d) =>
        Valid(y, m, d) ? new DateOnly(y, m, d).ToString("d 'de' MMMM 'de' yyyy", Es) : $"{d}/{m}/{y}";

    private static string? Join(string? a, string b) => a is null ? b : $"{a} {b}";

    private static int Int(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);

    private static string StripTime(string text) => TimeSuffix().Replace(text, "").Trim();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex OnlyDigits();
    [GeneratedRegex(@"^(?:(?<m>\d{1,2})|(?<mn>[a-zA-ZáéíóúÁÉÍÓÚ]+\.?))(?:[-/.\s]+|\s+de\s+)(?<y>\d{4}|\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthYear();
    [GeneratedRegex(@"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$")]
    private static partial Regex IsoDate();
    [GeneratedRegex(@"^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2}|\d{4})$")]
    private static partial Regex Numeric();
    [GeneratedRegex(@"^(?:(?<d1>\d{1,2})(?:\s+de\s+|[-/.\s]+)(?<m1>[a-zA-ZáéíóúÁÉÍÓÚ]+\.?)|(?<m2>[a-zA-ZáéíóúÁÉÍÓÚ]+\.?)\s+(?<d2>\d{1,2}))(?:,)?(?:\s+de\s+|\s+del\s+|[-/.\s]+)(?<y>\d{4}|\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex WithMonthName();
    [GeneratedRegex(@"[ T]\d{1,2}:\d{2}(:\d{2}(\.\d+)?)?(\s*[ap]\.?\s*m\.?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeSuffix();
}
