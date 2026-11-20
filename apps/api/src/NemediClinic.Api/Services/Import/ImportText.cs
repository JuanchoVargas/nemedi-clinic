using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Normalización de texto del importador: comparar encabezados con los alias de cada campo, y limpiar
/// los valores (espacios, nombres, teléfonos, documentos, correos).
/// </summary>
public static partial class ImportText
{
    /// <summary>"Nº Cédula " → "n cedula": minúsculas, sin tildes, solo letras y dígitos separados por un espacio.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Parecido entre dos claves normalizadas (0–1): igual, contenida por palabras, o por distancia de edición.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var ta = a.Split(' ');
        var tb = b.Split(' ');
        // "fecha de nacimiento del paciente" contiene "fecha de nacimiento". Lo contenido debe decir
        // algo (3+ letras): "c c" (cédula) no puede valer por la "c" de "Columna C".
        if ((Letters(tb) >= 3 && tb.All(ta.Contains)) || (Letters(ta) >= 3 && ta.All(tb.Contains))) return 0.9;
        var distance = Levenshtein(a, b);
        return 1 - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int Letters(string[] tokens) => tokens.Sum(t => t.Length);

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Texto de una celda: recortado y con los espacios internos colapsados; vacío → null.</summary>
    public static string? Clean(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            double d => d.ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Spaces().Replace(text.Replace(' ', ' '), " ").Trim();
    }

    private static readonly HashSet<string> Particles = ["de", "del", "la", "las", "los", "y", "da", "van", "von"];

    /// <summary>"MARÍA  JOSÉ de la ROSA" → "María José de la Rosa": cada palabra en mayúscula inicial, salvo partículas.</summary>
    public static string Name(string value)
    {
        var words = value.ToLower(CultureInfo.GetCultureInfo("es-CO")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0 && Particles.Contains(words[i])) continue;
            words[i] = string.Join('-', words[i].Split('-').Select(Capitalize));
        }
        return string.Join(' ', words);
    }

    private static string Capitalize(string w) => w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.GetCultureInfo("es-CO")) + w[1..];

    /// <summary>"1.023.456-7" → "10234567"; "ab 12" → "AB12": sin puntos, guiones ni espacios, en mayúsculas.</summary>
    public static string Document(string value) => NotAlphanumeric().Replace(value, "").ToUpperInvariant();

    /// <summary>
    /// "+57 (310) 555-12 34" → "3105551234": solo dígitos; sin el indicativo 57 de Colombia cuando lo trae
    /// delante de un celular (12 dígitos que empiezan por 573). Un número de Excel (3105551234.0) también.
    /// </summary>
    public static string Phone(string value)
    {
        var digits = NotDigit().Replace(value.EndsWith(".0", StringComparison.Ordinal) ? value[..^2] : value, "");
        if (digits.Length == 12 && digits.StartsWith("573", StringComparison.Ordinal)) digits = digits[2..];
        return digits;
    }

    public static bool IsEmail(string value) => EmailPattern().IsMatch(value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"[^0-9A-Za-z]")]
    private static partial Regex NotAlphanumeric();
    [GeneratedRegex(@"[^0-9]")]
    private static partial Regex NotDigit();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailPattern();
}
