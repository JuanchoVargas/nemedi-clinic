using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Un pago recibido. Se aplica a un paquete asignado, a una venta o a ambos: el abono de un paquete
/// que salió de una venta lleva los dos ids, así el saldo del paquete (ficha del paciente) y el de la
/// venta se leen con una simple suma y nunca se cuentan dos veces.
///
/// Antes se llamaba PatientPayment y solo existía para paquetes (tabla renombrada en la migración
/// AddSalesAndCash; los pagos anteriores conservan su PatientPackageId).
///
/// Anular la venta anula sus pagos: FechaAnulacion + IsDeleted, de modo que desaparecen de todas las
/// sumas (filtro global) pero la caja y el detalle de la venta los siguen mostrando tachados.
/// </summary>
public class Payment : BaseEntity
{
    public Guid? PatientPackageId { get; set; }
    public Guid? SaleId { get; set; }
    /// <summary>Paciente que pagó. Null en una venta de mostrador sin paciente.</summary>
    public Guid? PatientId { get; set; }
    /// <summary>Caja abierta de quien lo registró en ese momento. Null si no había caja abierta.</summary>
    public Guid? CashSessionId { get; set; }

    public decimal Monto { get; set; }
    public DateOnly FechaPago { get; set; }
    public PaymentMethod MetodoPago { get; set; }
    public string? Observacion { get; set; }

    /// <summary>Número de transferencia, voucher del datáfono, recibo de caja…</summary>
    public string? Referencia { get; set; }
    /// <summary>Attachment (imagen o PDF) con el soporte del pago. Kind = Comprobante.</summary>
    public Guid? ComprobanteId { get; set; }
    /// <summary>Usuario que registró el pago. Null en los pagos anteriores a esta columna y en el seed.</summary>
    public Guid? RegistradoPorId { get; set; }

    /// <summary>Cuándo se anuló junto con su venta. Un pago anulado además queda IsDeleted.</summary>
    public DateTime? FechaAnulacion { get; set; }

    public PatientPackage? PatientPackage { get; set; }
    public Sale? Sale { get; set; }
    public CashSession? CashSession { get; set; }
    public User? RegistradoPor { get; set; }
}
