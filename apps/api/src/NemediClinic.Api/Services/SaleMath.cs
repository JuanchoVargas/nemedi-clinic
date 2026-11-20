using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Aritmética de una línea de venta, en un solo lugar (la web nunca la replica: pide una cotización).
///   Subtotal = Cantidad × PrecioUnitario − Descuento   (base gravable, precios sin IVA)
///   Iva      = Subtotal × IvaPorcentaje / 100
///   Total    = Subtotal + Iva
/// Todo se redondea a 2 decimales con MidpointRounding.AwayFromZero, igual que en la factura.
/// </summary>
public static class SaleMath
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    public static decimal Round(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static (decimal Subtotal, decimal Iva, decimal Total) Line(decimal cantidad, decimal precioUnitario, decimal descuento, decimal ivaPorcentaje)
    {
        var bruto = Round(cantidad * precioUnitario);
        var subtotal = Round(bruto - descuento);
        var iva = Round(subtotal * ivaPorcentaje / 100m);
        return (subtotal, iva, subtotal + iva);
    }

    /// <summary>"$1.234.500" para mensajes de error.</summary>
    public static string Pesos(decimal valor) => "$" + valor.ToString("N0", EsCo);
}

/// <summary>Sede de una operación: la pedida (debe existir), si no la del usuario, si no la primera.</summary>
public static class BranchLookup
{
    public static async Task<Guid> ResolveAsync(AppDbContext db, Guid? requested, Guid? userBranchId, CancellationToken ct)
    {
        if (requested.HasValue)
        {
            if (!await db.Branches.AnyAsync(b => b.Id == requested.Value, ct))
                throw ApiException.BadRequest("Sede no encontrada.");
            return requested.Value;
        }
        if (userBranchId.HasValue && await db.Branches.AnyAsync(b => b.Id == userBranchId.Value, ct))
            return userBranchId.Value;

        return await db.Branches.OrderBy(b => b.CreatedAt).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
            ?? throw ApiException.BadRequest("La clínica no tiene sedes.");
    }
}
