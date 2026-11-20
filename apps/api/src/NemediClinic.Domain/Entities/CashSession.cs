using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Caja de un usuario en una sede: se abre con una base, recibe los pagos que ese usuario registra
/// mientras está abierta, anota ingresos y egresos manuales y se cierra con el arqueo.
///
///   Efectivo esperado = base + pagos en efectivo + ingresos manuales − egresos
///   Diferencia        = efectivo contado − efectivo esperado  (negativa = faltante)
///
/// Un usuario tiene como máximo una caja abierta. Al cerrar se congelan los totales: lo que pase
/// después (p. ej. anular una venta cobrada en esa caja) ya no cambia un arqueo cerrado, sino que
/// se registra como egreso en la caja abierta de quien anula.
/// </summary>
public class CashSession : BaseEntity
{
    public Guid BranchId { get; set; }
    public Guid UsuarioId { get; set; }
    public CashSessionStatus Estado { get; set; } = CashSessionStatus.Abierta;

    public DateTime FechaApertura { get; set; }
    /// <summary>Base en efectivo con la que se abre.</summary>
    public decimal MontoApertura { get; set; }
    public string? ObservacionApertura { get; set; }

    public DateTime? FechaCierre { get; set; }
    public Guid? CerradaPorId { get; set; }
    public string? ObservacionCierre { get; set; }

    // ── Congelados al cerrar (null mientras está abierta: se calculan en vivo) ──
    public decimal? TotalEfectivo { get; set; }
    public decimal? TotalTransferencia { get; set; }
    public decimal? TotalTarjeta { get; set; }
    public decimal? TotalOtro { get; set; }
    public decimal? TotalIngresos { get; set; }
    public decimal? TotalEgresos { get; set; }
    public decimal? EfectivoEsperado { get; set; }
    public decimal? EfectivoContado { get; set; }
    public decimal? Diferencia { get; set; }

    public Branch Branch { get; set; } = null!;
    public User Usuario { get; set; } = null!;
    public ICollection<CashMovement> Movimientos { get; set; } = [];
}
