using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Una venta: la capa sobre la que se monta la facturación electrónica. Puede ser de un paciente o
/// de mostrador (sin paciente), con líneas de producto, procedimiento, paquete u "otro".
///
/// Reglas (SaleService):
///   · Borrador no tiene efectos. Al confirmar, los productos salen del inventario por FEFO, los
///     paquetes se asignan al paciente y los pagos se registran (en la caja abierta de quien cobra).
///   · Anular revierte todo en una transacción y es idempotente.
///   · Los totales se guardan ya calculados (subtotal sin IVA, IVA y total): es lo que se factura.
/// </summary>
public class Sale : BaseEntity
{
    /// <summary>Consecutivo interno por clínica. No es el número de la factura electrónica.</summary>
    public int Numero { get; set; }
    public Guid BranchId { get; set; }
    public Guid? PatientId { get; set; }
    /// <summary>Datos del cliente de mostrador (sin ficha de paciente). Opcionales.</summary>
    public string? ClienteNombre { get; set; }
    public string? ClienteDocumento { get; set; }

    public DateTime Fecha { get; set; }
    public SaleStatus Estado { get; set; } = SaleStatus.Borrador;

    /// <summary>Base gravable: Σ (cantidad × precio − descuento) de las líneas.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>Σ de los descuentos de las líneas (informativo: ya está restado del subtotal).</summary>
    public decimal Descuento { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }

    public Guid UsuarioId { get; set; }
    public string? Observacion { get; set; }

    public DateTime? FechaAnulacion { get; set; }
    public Guid? AnuladaPorId { get; set; }
    public string? MotivoAnulacion { get; set; }

    // ── Facturación electrónica: punto de extensión (IElectronicInvoiceProvider) ──
    // Quedan vacíos hasta que exista un proveedor. Se llenan con lo que devuelva la DIAN.
    public ElectronicInvoiceStatus FacturaEstado { get; set; } = ElectronicInvoiceStatus.SinFacturar;
    public string? FacturaNumero { get; set; }
    public string? FacturaCufe { get; set; }
    public DateTime? FacturaFecha { get; set; }
    /// <summary>Attachment (EntityType Sale, Kind Factura) con el XML firmado.</summary>
    public Guid? FacturaXmlId { get; set; }
    /// <summary>Attachment (EntityType Sale, Kind Factura) con la representación gráfica.</summary>
    public Guid? FacturaPdfId { get; set; }
    /// <summary>Último mensaje del proveedor (rechazos, validaciones).</summary>
    public string? FacturaMensaje { get; set; }

    public Branch Branch { get; set; } = null!;
    public Patient? Patient { get; set; }
    public User Usuario { get; set; } = null!;
    public ICollection<SaleLine> Lineas { get; set; } = [];
    public ICollection<Payment> Pagos { get; set; } = [];
}
