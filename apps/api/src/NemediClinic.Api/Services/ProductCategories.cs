using Microsoft.EntityFrameworkCore;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Categoría de producto: texto libre, pero sin variantes de lo mismo. "facial", "FACIAL " y "Facial"
/// son una sola: si la clínica ya usa una con esas letras (sin distinguir mayúsculas, tildes ni
/// espacios), se guarda esa; si es nueva, con la primera letra en mayúscula.
/// </summary>
public static class ProductCategories
{
    public const int MaxLength = 100;

    public static async Task<string?> NormalizeAsync(AppDbContext db, string? value, CancellationToken ct = default)
    {
        var existing = await db.Products.AsNoTracking().Where(p => p.Categoria != null).Select(p => p.Categoria!).Distinct().ToListAsync(ct);
        return Normalize(value, existing);
    }

    public static string? Normalize(string? value, IEnumerable<string> existing)
    {
        var clean = string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return null;
        var key = Import.ImportText.Key(clean);
        var match = existing.FirstOrDefault(e => Import.ImportText.Key(e) == key);
        if (match is not null) return match;
        if (clean.Length > MaxLength) clean = clean[..MaxLength];
        return char.ToUpper(clean[0], System.Globalization.CultureInfo.GetCultureInfo("es-CO")) + clean[1..];
    }
}
