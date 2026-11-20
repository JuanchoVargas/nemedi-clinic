namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Para entidades que ocupan varias filas (un producto con varios lotes, un paquete con varias
/// sesiones): el dato del registro se toma de la primera fila que lo trae, y si otra fila del mismo
/// grupo trae uno distinto se le avisa a esa fila cuál quedó.
/// </summary>
public static class ImportGrouping
{
    public static object? Merge(IEnumerable<ImportRow> rows, string key, string label)
    {
        object? chosen = null;
        var chosenRow = 0;
        foreach (var row in rows)
        {
            if (!row.Values.TryGetValue(key, out var value) || value is null) continue;
            if (chosen is null)
            {
                chosen = value;
                chosenRow = row.Fila;
            }
            else if (!Same(chosen, value))
            {
                row.Warnings.Add($"{label}: se usa «{Show(chosen)}» de la fila {chosenRow}, no «{Show(value)}».");
            }
        }
        return chosen;
    }

    public static T? Merge<T>(IEnumerable<ImportRow> rows, string key, string label) where T : struct =>
        Merge(rows, key, label) is T t ? t : null;

    public static string? MergeText(IEnumerable<ImportRow> rows, string key, string label) => Merge(rows, key, label) as string;

    private static bool Same(object a, object b) =>
        a is string sa && b is string sb ? ImportText.Key(sa) == ImportText.Key(sb) : a.Equals(b);

    private static string Show(object value) => value switch
    {
        DateOnly d => d.ToString("dd/MM/yyyy"),
        decimal m => m.ToString("#,0.##", System.Globalization.CultureInfo.GetCultureInfo("es-CO")),
        bool b => b ? "Sí" : "No",
        _ => value.ToString() ?? "",
    };
}
