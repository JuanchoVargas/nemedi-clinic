using System.ComponentModel.DataAnnotations;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Application.DTOs.Sales;

// ── Requests ─────────────────────────────────────────────────────────

public class SaleLineRequest
{
    [Required]
    public SaleLineType Tipo { get; set; }

    /// <summary>Id del producto, procedimiento o paquete. Null solo en "Otro".</summary>
    public Guid? ReferenciaId { get; set; }

    /// <summary>Obligatoria en "Otro"; en el resto se copia del catálogo.</summary>
    [MaxLength(250)]
    public string? Descripcion { get; set; }

    [Range(0.0001, double.MaxValue)]
    public decimal Cantidad { get; set; } = 1m;

    /// <summary>Precio unitario sin IVA. Null = el del catálogo.</summary>
    [Range(0, double.MaxValue)]
    public decimal? PrecioUnitario { get; set; }

    /// <summary>Descuento en pesos sobre la línea.</summary>
    [Range(0, double.MaxValue)]
    public decimal Descuento { get; set; }

    /// <summary>Solo en "Otro": el IVA de lo demás lo decide el catálogo y la clínica.</summary>
    [Range(0, 100)]
    public decimal? IvaPorcentaje { get; set; }
}

public class SalePaymentRequest
{
    [Range(0.01, double.MaxValue)]
    public decimal Monto { get; set; }

    [Required]
    public PaymentMethod MetodoPago { get; set; }

    [MaxLength(100)]
    public string? Referencia { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }

    /// <summary>Adjunto subido antes como pendiente (entityType Payment, kind Comprobante).</summary>
    public Guid? ComprobanteId { get; set; }
}

public class SaveSaleRequest
{
    /// <summary>Null = la sede del usuario (o la primera de la clínica).</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Null = venta de mostrador. Obligatorio si hay un paquete.</summary>
    public Guid? PatientId { get; set; }

    [MaxLength(200)]
    public string? ClienteNombre { get; set; }

    [MaxLength(30)]
    public string? ClienteDocumento { get; set; }

    [MaxLength(500)]
    public string? Observacion { get; set; }

    [MinLength(1, ErrorMessage = "La venta necesita al menos una línea.")]
    public List<SaleLineRequest> Lineas { get; set; } = [];

    /// <summary>Solo al confirmar. Pueden ser varios métodos; la suma no puede pasar del total.</summary>
    public List<SalePaymentRequest> Pagos { get; set; } = [];

    /// <summary>true = confirmar de una vez (lo normal en caja); false = dejarla en borrador.</summary>
    public bool Confirmar { get; set; } = true;
}

public class ConfirmSaleRequest
{
    public List<SalePaymentRequest> Pagos { get; set; } = [];
}

public class VoidSaleRequest
{
    [Required(ErrorMessage = "Indica el motivo de la anulación."), MaxLength(500)]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Cotización: las mismas líneas que una venta, sin guardar nada. Así la web no calcula IVA por su cuenta.</summary>
public class QuoteSaleRequest
{
    public List<SaleLineRequest> Lineas { get; set; } = [];
}

// ── Responses ────────────────────────────────────────────────────────

public class SaleLineDto
{
    public Guid Id { get; set; }
    public int Orden { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public Guid? ReferenciaId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal IvaPorcentaje { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public Guid? PatientPackageId { get; set; }
}

public class SalePaymentDto
{
    public Guid Id { get; set; }
    public decimal Monto { get; set; }
    public DateOnly FechaPago { get; set; }
    public DateTime RegistradoEl { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public string? Observacion { get; set; }
    public string? RegistradoPor { get; set; }
    public Guid? ComprobanteId { get; set; }
    /// <summary>Paquete al que se aplicó el abono (null = al resto de la venta).</summary>
    public Guid? PatientPackageId { get; set; }
    public string? AplicadoA { get; set; }
    public bool Anulado { get; set; }
}

public class SaleDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public DateTime Fecha { get; set; }
    public string Estado { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchNombre { get; set; } = string.Empty;
    public Guid? PatientId { get; set; }
    /// <summary>Paciente, o el cliente de mostrador ("Cliente de mostrador" si no se dio nombre).</summary>
    public string Cliente { get; set; } = string.Empty;
    public string? ClienteDocumento { get; set; }
    public bool EsMostrador { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    public decimal TotalPagado { get; set; }
    public decimal Saldo { get; set; }
    /// <summary>SinPagos / Parcial / Pagado (misma regla que los paquetes).</summary>
    public string EstadoPago { get; set; } = string.Empty;
    public string Vendedor { get; set; } = string.Empty;
    public string? Observacion { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    public string? AnuladaPor { get; set; }
    public string? MotivoAnulacion { get; set; }
    /// <summary>"2 productos · 1 procedimiento": resumen para el listado.</summary>
    public string Resumen { get; set; } = string.Empty;

    // Facturación electrónica (SinFacturar hasta que se emita; el detalle está en /invoices?saleId=)
    public string FacturaEstado { get; set; } = string.Empty;
    public string? FacturaNumero { get; set; }
    public string? FacturaCufe { get; set; }
    public DateTime? FacturaFecha { get; set; }
    public string? FacturaMensaje { get; set; }
    public Guid? FacturaPdfId { get; set; }
    public Guid? FacturaXmlId { get; set; }

    public List<SaleLineDto> Lineas { get; set; } = [];
    public List<SalePaymentDto> Pagos { get; set; } = [];
}

public class SaleQuoteDto
{
    public List<SaleLineDto> Lineas { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Resultado de la anulación: qué se revirtió.</summary>
public class VoidSaleResultDto
{
    public SaleDto Venta { get; set; } = null!;
    /// <summary>true si ya estaba anulada (la llamada no cambió nada).</summary>
    public bool YaEstabaAnulada { get; set; }
    public int MovimientosRevertidos { get; set; }
    public int PaquetesRevertidos { get; set; }
    public int PagosAnulados { get; set; }
    /// <summary>Efectivo devuelto como egreso en la caja abierta de quien anula.</summary>
    public decimal DevolucionEfectivo { get; set; }
    /// <summary>Efectivo de cajas ya cerradas que no se pudo registrar como egreso por no haber caja abierta.</summary>
    public decimal DevolucionFueraDeCaja { get; set; }
}

/// <summary>Un ítem vendible del buscador de "Nueva venta".</summary>
public class SaleCatalogItemDto
{
    public string Tipo { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    /// <summary>Sin IVA.</summary>
    public decimal Precio { get; set; }
    public decimal IvaPorcentaje { get; set; }
    /// <summary>Solo productos: existencia utilizable.</summary>
    public decimal? Stock { get; set; }
    public string? UnidadMedida { get; set; }
    public Guid? ImagenId { get; set; }
}
