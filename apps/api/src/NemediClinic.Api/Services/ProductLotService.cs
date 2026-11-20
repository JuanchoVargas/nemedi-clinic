using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services;

/// <summary>
/// Lotes de producto: entrada, salida por FEFO y stock.
///
/// Reglas:
///   · Cada entrada de inventario crea un lote. El lote es la existencia real; Product.StockActual
///     se mantiene como la suma de lo disponible en los lotes **no vencidos**, para que el resto de
///     la aplicación (semáforo, alertas, dashboard) siga leyendo un solo número.
///   · Las salidas descuentan **FEFO** (first expired, first out): primero el lote que vence antes.
///     Es lo que hace una clínica de verdad y lo que evita que se venza el inventario en la repisa.
///   · Un lote vencido no se puede consumir: se responde 422 diciendo cuál es y cuándo venció.
/// </summary>
public class ProductLotService
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;

    public ProductLotService(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Un lote nuevo con su cantidad. No guarda: quien llama hace el SaveChanges.</summary>
    public ProductLot CreateLot(Product product, decimal cantidad, string? numeroLote, DateOnly? vencimiento,
        string? proveedor, string? numeroFactura, DateTime fechaIngreso, decimal? costoUnitario = null)
    {
        var lot = new ProductLot
        {
            ProductId = product.Id,
            NumeroLote = Limpio(numeroLote),
            FechaVencimiento = vencimiento,
            CantidadInicial = cantidad,
            CantidadDisponible = cantidad,
            FechaIngreso = fechaIngreso,
            Proveedor = Limpio(proveedor),
            NumeroFactura = Limpio(numeroFactura),
            CostoUnitario = costoUnitario,
            // Copia del registro del producto: lo reportado no cambia si mañana cambia el producto
            RegistroSanitario = product.RegistroSanitarioInvima
        };
        _db.ProductLots.Add(lot);
        return lot;
    }

    /// <summary>
    /// Reparte <paramref name="cantidad"/> entre los lotes del producto por FEFO y devuelve de cuánto
    /// salió cada uno. No guarda. Lanza 422 si no alcanza o si lo único disponible está vencido.
    /// </summary>
    public async Task<List<(ProductLot Lot, decimal Cantidad)>> TakeFefoAsync(
        Product product, decimal cantidad, CancellationToken ct = default)
    {
        var hoy = _clock.Today;

        var lotes = await _db.ProductLots
            .Where(l => l.ProductId == product.Id && l.CantidadDisponible > 0)
            .ToListAsync(ct);

        var vencidos = lotes.Where(l => l.CantidadDisponible > 0 && l.EstadoAl(hoy) == LotStatus.Vencido).ToList();
        // CantidadDisponible > 0 otra vez en memoria: dos salidas del mismo producto en una misma
        // transacción ven el lote ya descontado (EF devuelve la entidad rastreada, no la de la base)
        var utilizables = lotes
            .Where(l => l.CantidadDisponible > 0 && l.EstadoAl(hoy) != LotStatus.Vencido)
            // FEFO: el que vence antes sale primero; los que no vencen, de últimos
            .OrderBy(l => l.FechaVencimiento ?? DateOnly.MaxValue)
            .ThenBy(l => l.FechaIngreso)
            .ToList();

        var disponible = utilizables.Sum(l => l.CantidadDisponible);
        if (disponible < cantidad)
        {
            var detalleVencidos = vencidos.Count > 0
                ? $" Hay {vencidos.Sum(l => l.CantidadDisponible):0.##} en lotes vencidos ({string.Join(", ", vencidos.Select(Describir))}), que no se pueden usar."
                : string.Empty;
            throw new ApiException(StatusCodes.Status422UnprocessableEntity,
                $"No hay stock utilizable de {product.Nombre}: quedan {disponible:0.##} {product.UnidadMedida} sin vencer y se necesitan {cantidad:0.##}.{detalleVencidos}");
        }

        var reparto = new List<(ProductLot, decimal)>();
        var pendiente = cantidad;
        foreach (var lote in utilizables)
        {
            if (pendiente <= 0) break;
            var toma = Math.Min(lote.CantidadDisponible, pendiente);
            lote.CantidadDisponible -= toma;
            pendiente -= toma;
            reparto.Add((lote, toma));
        }

        return reparto;
    }

    /// <summary>
    /// Deja Product.StockActual igual a lo disponible en lotes no vencidos. Se llama después de
    /// cualquier movimiento; así el semáforo y las alertas no cuentan lo que ya no se puede usar.
    /// </summary>
    public async Task RecalculateStockAsync(Guid productId, CancellationToken ct = default)
    {
        var hoy = _clock.Today;
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return;

        var lotes = await _db.ProductLots.Where(l => l.ProductId == productId).ToListAsync(ct);
        // Un producto sin lotes es de antes de la trazabilidad: se deja su stock como está
        if (lotes.Count == 0) return;

        product.StockActual = lotes
            .Where(l => l.EstadoAl(hoy) != LotStatus.Vencido)
            .Sum(l => l.CantidadDisponible);
    }

    /// <summary>Job/arranque: recalcula el stock de todo producto con lotes (uno pudo vencerse anoche).</summary>
    public async Task<int> RecalculateAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.ProductLots.Select(l => l.ProductId).Distinct().ToListAsync(ct);
        foreach (var id in ids)
            await RecalculateStockAsync(id, ct);
        if (ids.Count > 0)
            await _db.SaveChangesAsync(ct);
        return ids.Count;
    }

    /// <summary>
    /// Da de baja lo que queda de un lote (vencido, dañado, retirado por el INVIMA): un movimiento
    /// de tipo Baja por toda la existencia, el lote queda en 0 y el stock se recalcula. Guarda.
    /// El lote no se borra: sigue siendo parte del respaldo ante la Secretaría de Salud.
    /// </summary>
    public async Task<ProductLot> WriteOffAsync(Guid productId, Guid lotId, string? motivo, Guid userId, CancellationToken ct = default)
    {
        var lote = await _db.ProductLots.Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.Id == lotId && l.ProductId == productId, ct)
            ?? throw ApiException.NotFound("Lote no encontrado.");
        if (lote.CantidadDisponible <= 0)
            throw ApiException.Conflict("El lote ya no tiene existencia: no hay nada que dar de baja.");

        var razon = string.IsNullOrWhiteSpace(motivo) ? "Vencimiento" : motivo.Trim();
        _db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = productId,
            ProductLotId = lote.Id,
            Cantidad = lote.CantidadDisponible,
            TipoMovimiento = MovementType.Baja,
            Referencia = $"Baja de {Describir(lote)} · {razon}",
            UserId = userId,
            FechaMovimiento = _clock.Now
        });
        lote.CantidadDisponible = 0;
        await RecalculateStockAsync(productId, ct);
        await _db.SaveChangesAsync(ct);
        return lote;
    }

    public static string Describir(ProductLot lote) =>
        lote.NumeroLote is { } n ? $"lote {n}" : "lote sin número";

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
