using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Línea de una venta. La descripción, el precio y el IVA son una copia del catálogo al momento de
/// vender: editar o borrar el producto después no cambia lo vendido (ni lo facturado).
///   Subtotal = Cantidad × PrecioUnitario − Descuento  (base gravable)
///   Iva      = Subtotal × IvaPorcentaje / 100
///   Total    = Subtotal + Iva
/// </summary>
public class SaleLine : BaseEntity
{
    public Guid SaleId { get; set; }
    /// <summary>Orden en que se digitó. Los abonos a la venta se aplican a las líneas en este orden.</summary>
    public int Orden { get; set; }
    public SaleLineType Tipo { get; set; }
    /// <summary>Id del producto, procedimiento o paquete. Null en "Otro".</summary>
    public Guid? ReferenciaId { get; set; }
    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }
    /// <summary>Precio unitario sin IVA.</summary>
    public decimal PrecioUnitario { get; set; }
    /// <summary>Descuento en pesos sobre la línea (no porcentaje).</summary>
    public decimal Descuento { get; set; }
    public decimal IvaPorcentaje { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }

    /// <summary>Paquete asignado al confirmar una línea de tipo Paquete.</summary>
    public Guid? PatientPackageId { get; set; }

    public Sale Sale { get; set; } = null!;
}
