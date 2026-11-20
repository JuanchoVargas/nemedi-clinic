using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>Ingreso o egreso manual de una caja (compra de insumos menores, devolución, base extra…).</summary>
public class CashMovement : BaseEntity
{
    public Guid CashSessionId { get; set; }
    public CashMovementType Tipo { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; }
    public Guid UsuarioId { get; set; }
    /// <summary>Venta que lo originó (devolución en efectivo al anularla).</summary>
    public Guid? SaleId { get; set; }

    public CashSession CashSession { get; set; } = null!;
}
