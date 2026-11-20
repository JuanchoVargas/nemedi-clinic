using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Un documento electrónico ante la DIAN: una factura (de una venta o de un pago suelto de paquete)
/// o una nota crédito que corrige una factura. Lo emite el proveedor (Factus) y aquí queda lo que
/// devolvió: número, CUFE, PDF y XML (adjuntos) y el mensaje si lo rechazó.
///
/// Estados (InvoiceService):
///   · Pendiente: aún no hay respuesta definitiva (caída de red, 5xx, sin token). El job
///     facturas-reintento la reenvía con la MISMA referencia: el proveedor devuelve el documento ya
///     validado si alcanzó a registrarlo, así que reintentar nunca duplica.
///   · Emitida: validada por la DIAN.
///   · Rechazada: el proveedor o la DIAN la rechazaron; Mensaje trae el motivo. Se corrige el dato y
///     se reintenta con una referencia nueva.
///   · Anulada: una factura a la que una nota crédito de anulación le quitó el efecto.
/// </summary>
public class ElectronicInvoice : BaseEntity
{
    public ElectronicDocumentType TipoDocumento { get; set; } = ElectronicDocumentType.Factura;
    public ElectronicInvoiceStatus Estado { get; set; } = ElectronicInvoiceStatus.Pendiente;

    /// <summary>La venta facturada (o la de la factura que corrige una nota crédito).</summary>
    public Guid? SaleId { get; set; }
    /// <summary>Un pago de paquete registrado desde la ficha, sin venta.</summary>
    public Guid? PaymentId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid BranchId { get; set; }
    /// <summary>En una nota crédito, la factura que corrige.</summary>
    public Guid? FacturaOrigenId { get; set; }

    /// <summary>reference_code ante el proveedor: único, y el mismo en cada reintento.</summary>
    public string Referencia { get; set; } = string.Empty;
    public int? RangoNumeracionId { get; set; }
    public string? Numero { get; set; }
    public string? Cufe { get; set; }
    public DateTime? FechaEmision { get; set; }

    public string ClienteNombre { get; set; } = string.Empty;
    public string ClienteDocumento { get; set; } = string.Empty;
    public string? ClienteEmail { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }

    /// <summary>Motivo del rechazo, o el último error de conexión si está pendiente.</summary>
    public string? Mensaje { get; set; }
    /// <summary>Notificaciones de la DIAN que no impiden la validación (FAJ44b, RUT01…).</summary>
    public string? Advertencias { get; set; }

    /// <summary>Nota crédito: código del concepto de corrección DIAN y el motivo que escribió quien la hizo.</summary>
    public string? ConceptoCorreccion { get; set; }
    public string? Motivo { get; set; }

    /// <summary>Attachment (Kind Factura) con el XML firmado y con la representación gráfica.</summary>
    public Guid? XmlId { get; set; }
    public Guid? PdfId { get; set; }

    public int Intentos { get; set; }
    public DateTime? UltimoIntento { get; set; }
    /// <summary>
    /// El documento tal como se armó (JSON de ElectronicInvoiceDocument). Un reintento reenvía
    /// exactamente esto, aunque la venta o el paciente hayan cambiado después.
    /// </summary>
    public string Documento { get; set; } = string.Empty;

    public string? CorreoEnviadoA { get; set; }
    public DateTime? FechaCorreo { get; set; }
    public Guid EmitidaPorId { get; set; }

    public Sale? Sale { get; set; }
    public Payment? Payment { get; set; }
    public Patient? Patient { get; set; }
    public ElectronicInvoice? FacturaOrigen { get; set; }
}
