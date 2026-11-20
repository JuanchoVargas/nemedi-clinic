namespace NemediClinic.Application.Interfaces;

/// <summary>
/// Proveedor tecnológico de facturación electrónica ante la DIAN (hoy Factus: FactusInvoiceProvider).
///
/// El documento que recibe es neutro: los datos de NemediClinic (tipo de documento RIPS, medio de
/// pago, municipio DANE, líneas ya calculadas con base, IVA y total). El proveedor los traduce a sus
/// códigos y NO recalcula nada. La conexión viaja en cada llamada porque cada clínica tiene su cuenta.
///
/// Cómo reporta el resultado (ElectronicInvoiceResult.Estado):
///   · Emitida — validado por la DIAN.
///   · Rechazada — el proveedor o la DIAN lo rechazaron por sus datos; Mensaje dice qué corregir.
///   · Pendiente — no hubo respuesta definitiva (red, 5xx, credenciales): se puede reintentar con la
///     misma referencia sin riesgo de duplicar.
/// </summary>
public interface IElectronicInvoiceProvider
{
    /// <summary>Nombre del proveedor, para mostrar ("Facturado con …").</summary>
    string Nombre { get; }

    Task<ElectronicInvoiceResult> EmitirAsync(InvoicingConnection conexion, ElectronicInvoiceDocument documento, CancellationToken ct = default);

    /// <summary>Nota crédito que referencia una factura emitida (anulación o devolución parcial).</summary>
    Task<ElectronicInvoiceResult> NotaCreditoAsync(InvoicingConnection conexion, ElectronicInvoiceDocument documento, CancellationToken ct = default);

    /// <summary>Busca un documento por su referencia: sirve para recuperar uno que se envió pero cuya respuesta se perdió.</summary>
    Task<ElectronicInvoiceResult?> BuscarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string referencia, CancellationToken ct = default);

    /// <summary>
    /// Descarta un documento que el proveedor registró pero la DIAN no validó. Mientras exista, el
    /// proveedor no deja emitir otro (409): se llama antes de reemitir una rechazada con referencia nueva.
    /// </summary>
    Task DescartarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string referencia, CancellationToken ct = default);

    /// <summary>PDF y XML de un documento emitido.</summary>
    Task<ElectronicDocumentFiles> DescargarAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string numero, CancellationToken ct = default);

    Task EnviarCorreoAsync(InvoicingConnection conexion, ElectronicDocumentKind tipo, string numero, string correo, CancellationToken ct = default);

    /// <summary>Autentica y trae el emisor de la cuenta y sus rangos de numeración.</summary>
    Task<InvoicingConnectionTest> ProbarConexionAsync(InvoicingConnection conexion, CancellationToken ct = default);
}

public enum ElectronicDocumentKind
{
    Factura,
    NotaCredito
}

/// <summary>La cuenta de una clínica ante el proveedor y cómo quiere que se armen sus documentos.</summary>
public class InvoicingConnection
{
    /// <summary>Clave del caché de tokens: el token de una clínica nunca sirve para otra.</summary>
    public Guid TenantId { get; set; }
    public bool Produccion { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Contrasena { get; set; } = string.Empty;

    public string UnidadMedida { get; set; } = string.Empty;
    public string CodigoEstandar { get; set; } = string.Empty;
    public bool IvaCeroExcluido { get; set; }
    /// <summary>Código de medio de pago que la clínica cambió (PaymentMethod → código DIAN).</summary>
    public Dictionary<string, string> CodigosMetodoPago { get; set; } = [];
}

public class ElectronicInvoiceDocument
{
    /// <summary>reference_code: único por documento y el mismo en cada reintento.</summary>
    public string Referencia { get; set; } = string.Empty;
    public int? RangoNumeracionId { get; set; }
    public DateTime Fecha { get; set; }
    public string? Observacion { get; set; }
    public bool EnviarCorreo { get; set; }

    public ElectronicInvoiceCustomer Cliente { get; set; } = new();
    public List<ElectronicInvoiceLine> Lineas { get; set; } = [];
    public List<ElectronicInvoicePayment> Pagos { get; set; } = [];

    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    /// <summary>Lo que quedó sin pagar: la factura va a crédito con esta fecha de vencimiento.</summary>
    public decimal Saldo { get; set; }
    public DateOnly? Vencimiento { get; set; }

    /// <summary>Solo nota crédito: número de la factura que corrige y concepto DIAN de corrección.</summary>
    public string? FacturaNumero { get; set; }
    public string? ConceptoCorreccion { get; set; }
}

public class ElectronicInvoiceCustomer
{
    /// <summary>Consumidor final: sin documento, o con un tipo que no tiene equivalente DIAN (AS, MS, SI).</summary>
    public bool ConsumidorFinal { get; set; }
    /// <summary>Tipo de documento como lo guarda NemediClinic (CC, CE, PA… o NIT).</summary>
    public string? TipoDocumento { get; set; }
    public string Documento { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool PersonaJuridica { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    /// <summary>Código DANE del municipio (el mismo del RIPS).</summary>
    public string? Municipio { get; set; }
}

public class ElectronicInvoiceLine
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    /// <summary>Precio unitario antes de IVA y de descuento.</summary>
    public decimal PrecioUnitario { get; set; }
    /// <summary>Descuento de la línea en pesos.</summary>
    public decimal Descuento { get; set; }
    public decimal Base { get; set; }
    public decimal IvaPorcentaje { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    /// <summary>Producto / Procedimiento / Paquete / Otro / Abono.</summary>
    public string Tipo { get; set; } = string.Empty;
    /// <summary>Nota crédito: posición de la línea en la factura que corrige (el proveedor no lo usa).</summary>
    public int? IndiceOrigen { get; set; }
}

public class ElectronicInvoicePayment
{
    /// <summary>PaymentMethod de NemediClinic (Efectivo, Transferencia, Tarjeta, Otro).</summary>
    public string Metodo { get; set; } = string.Empty;
    public decimal Monto { get; set; }
}

public enum ElectronicResultStatus
{
    Emitida,
    Rechazada,
    Pendiente
}

public class ElectronicInvoiceResult
{
    public ElectronicResultStatus Estado { get; set; }
    public string? Numero { get; set; }
    public string? Cufe { get; set; }
    public DateTime? Fecha { get; set; }
    /// <summary>Motivo del rechazo o del error de conexión.</summary>
    public string? Mensaje { get; set; }
    /// <summary>Notificaciones de la DIAN que no rechazan el documento.</summary>
    public string? Advertencias { get; set; }
}

public class ElectronicDocumentFiles
{
    public byte[]? Pdf { get; set; }
    public string? PdfNombre { get; set; }
    public byte[]? Xml { get; set; }
    public string? XmlNombre { get; set; }
}

public class InvoicingConnectionTest
{
    public bool Exitosa { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string? EmisorNit { get; set; }
    public string? EmisorNombre { get; set; }
    public List<InvoicingNumberingRange> Rangos { get; set; } = [];
}

public class InvoicingNumberingRange
{
    public int Id { get; set; }
    /// <summary>Factura / NotaCredito / Otro (según el documento del rango).</summary>
    public string Documento { get; set; } = string.Empty;
    public string Prefijo { get; set; } = string.Empty;
    public long? Desde { get; set; }
    public long? Hasta { get; set; }
    public long Actual { get; set; }
    public string? Resolucion { get; set; }
    public bool Vigente { get; set; }
}
