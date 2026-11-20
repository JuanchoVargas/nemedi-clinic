namespace NemediClinic.Application.DTOs.Invoicing;

public class ElectronicInvoiceDto
{
    public Guid Id { get; set; }
    /// <summary>Factura / NotaCredito.</summary>
    public string TipoDocumento { get; set; } = string.Empty;
    /// <summary>Pendiente / Emitida / Rechazada / Anulada.</summary>
    public string Estado { get; set; } = string.Empty;
    public string Referencia { get; set; } = string.Empty;
    public string? Numero { get; set; }
    public string? Cufe { get; set; }
    public DateTime? FechaEmision { get; set; }
    public DateTime Creada { get; set; }

    public Guid? SaleId { get; set; }
    public int? VentaNumero { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid BranchId { get; set; }
    public string BranchNombre { get; set; } = string.Empty;

    public string ClienteNombre { get; set; } = string.Empty;
    public string ClienteDocumento { get; set; } = string.Empty;
    public string? ClienteEmail { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }

    public string? Mensaje { get; set; }
    public string? Advertencias { get; set; }

    /// <summary>Nota crédito: la factura que corrige.</summary>
    public Guid? FacturaOrigenId { get; set; }
    public string? FacturaOrigenNumero { get; set; }
    public string? ConceptoCorreccion { get; set; }
    public string? ConceptoCorreccionNombre { get; set; }
    public string? Motivo { get; set; }
    /// <summary>Factura: suma de sus notas crédito emitidas o pendientes (lo que ya se devolvió).</summary>
    public decimal TotalNotasCredito { get; set; }

    public Guid? PdfId { get; set; }
    public Guid? XmlId { get; set; }
    public int Intentos { get; set; }
    public DateTime? UltimoIntento { get; set; }
    public string? CorreoEnviadoA { get; set; }
    public DateTime? FechaCorreo { get; set; }
    public string EmitidaPor { get; set; } = string.Empty;

    /// <summary>Líneas del documento tal como se enviaron (para la nota crédito parcial).</summary>
    public List<ElectronicInvoiceLineDto> Lineas { get; set; } = [];
}

public class ElectronicInvoiceLineDto
{
    public int Indice { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal IvaPorcentaje { get; set; }
    public decimal Total { get; set; }
    /// <summary>Cantidad que ya salió en notas crédito (emitidas o pendientes).</summary>
    public decimal CantidadDevuelta { get; set; }
}

public class IssueInvoiceRequest
{
    /// <summary>Una venta confirmada, o …</summary>
    public Guid? SaleId { get; set; }
    /// <summary>… un pago de paquete registrado desde la ficha (sin venta).</summary>
    public Guid? PaymentId { get; set; }
}

public class CreditNoteRequest
{
    /// <summary>Anulacion (toda la factura) o Devolucion (las líneas indicadas).</summary>
    public string Tipo { get; set; } = "Anulacion";
    public string Motivo { get; set; } = string.Empty;
    public List<CreditNoteLineRequest> Lineas { get; set; } = [];
}

public class CreditNoteLineRequest
{
    public int Indice { get; set; }
    public decimal Cantidad { get; set; }
}

public class SendInvoiceEmailRequest
{
    /// <summary>Vacío = el correo del cliente de la factura (el del paciente).</summary>
    public string? Correo { get; set; }
}

public class InvoiceListFilter
{
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
    public string? Estado { get; set; }
    public string? TipoDocumento { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? SaleId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ── Configuración ───────────────────────────────────────────────

public class InvoicingSettingsDto
{
    public bool Configurada { get; set; }
    public bool Habilitada { get; set; }
    public string Ambiente { get; set; } = "Pruebas";
    public string Proveedor { get; set; } = string.Empty;
    /// <summary>Nunca vuelven las credenciales: solo si están y una pista del usuario y del client_id.</summary>
    public bool TieneCredenciales { get; set; }
    /// <summary>Hay credenciales guardadas pero no se pueden descifrar (cambió la clave): hay que volver a escribirlas.</summary>
    public bool CredencialesIlegibles { get; set; }
    public string? UsuarioPista { get; set; }
    public string? ClientIdPista { get; set; }

    public string EmisorNit { get; set; } = string.Empty;
    public string? EmisorDv { get; set; }
    public string EmisorRazonSocial { get; set; } = string.Empty;
    public string EmisorTipoOrganizacion { get; set; } = "1";
    public string EmisorResponsabilidad { get; set; } = "R-99-PN";
    public string? EmisorMunicipio { get; set; }
    public string? EmisorDireccion { get; set; }
    public string? EmisorTelefono { get; set; }
    public string? EmisorEmail { get; set; }

    public string UnidadMedida { get; set; } = "94";
    public string CodigoEstandar { get; set; } = "999";
    public bool IvaCeroExcluido { get; set; } = true;
    public bool EnviarCorreoAlEmitir { get; set; }
    public Dictionary<string, string> CodigosMetodoPago { get; set; } = [];

    public DateTime? UltimaPrueba { get; set; }
    public bool UltimaPruebaExitosa { get; set; }
    public string? UltimaPruebaMensaje { get; set; }

    public List<InvoicingBranchDto> Sedes { get; set; } = [];
}

public class InvoicingBranchDto
{
    public Guid BranchId { get; set; }
    public string BranchNombre { get; set; } = string.Empty;
    public int? RangoFacturaId { get; set; }
    public string? PrefijoFactura { get; set; }
    public int? RangoNotaCreditoId { get; set; }
    public string? PrefijoNotaCredito { get; set; }
}

public class UpdateInvoicingSettingsRequest
{
    public bool Habilitada { get; set; }
    public string Ambiente { get; set; } = "Pruebas";
    /// <summary>Vacíos = conservar las guardadas. Para cambiarlas se envían las cuatro.</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Usuario { get; set; }
    public string? Contrasena { get; set; }

    public string EmisorNit { get; set; } = string.Empty;
    public string? EmisorDv { get; set; }
    public string EmisorRazonSocial { get; set; } = string.Empty;
    public string EmisorTipoOrganizacion { get; set; } = "1";
    public string EmisorResponsabilidad { get; set; } = "R-99-PN";
    public string? EmisorMunicipio { get; set; }
    public string? EmisorDireccion { get; set; }
    public string? EmisorTelefono { get; set; }
    public string? EmisorEmail { get; set; }

    public string UnidadMedida { get; set; } = "94";
    public string CodigoEstandar { get; set; } = "999";
    public bool IvaCeroExcluido { get; set; } = true;
    public bool EnviarCorreoAlEmitir { get; set; }
    public Dictionary<string, string>? CodigosMetodoPago { get; set; }

    public List<InvoicingBranchDto> Sedes { get; set; } = [];
}

public class TestConnectionRequest
{
    /// <summary>Opcionales: probar credenciales nuevas antes de guardarlas. Vacías = las guardadas.</summary>
    public string? Ambiente { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Usuario { get; set; }
    public string? Contrasena { get; set; }
}

public class TestConnectionResultDto
{
    public bool Exitosa { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string? EmisorNit { get; set; }
    public string? EmisorNombre { get; set; }
    /// <summary>El NIT de la cuenta no es el configurado para la clínica.</summary>
    public bool NitNoCoincide { get; set; }
    public List<NumberingRangeDto> Rangos { get; set; } = [];
}

public class NumberingRangeDto
{
    public int Id { get; set; }
    public string Documento { get; set; } = string.Empty;
    public string Prefijo { get; set; } = string.Empty;
    public long? Desde { get; set; }
    public long? Hasta { get; set; }
    public long Actual { get; set; }
    public string? Resolucion { get; set; }
    public bool Vigente { get; set; }
}

public class CatalogItemDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
}

/// <summary>Las tablas DIAN que la configuración necesita en sus selects (municipios y unidades aparte: son largas).</summary>
public class InvoicingCatalogsDto
{
    public string Fuente { get; set; } = string.Empty;
    public string Generado { get; set; } = string.Empty;
    public List<CatalogItemDto> TiposDocumento { get; set; } = [];
    public List<CatalogItemDto> TiposOrganizacion { get; set; } = [];
    public List<CatalogItemDto> Responsabilidades { get; set; } = [];
    public List<CatalogItemDto> MediosPago { get; set; } = [];
    public List<CatalogItemDto> ConceptosCorreccion { get; set; } = [];
    public List<CatalogItemDto> CodigosEstandar { get; set; } = [];
    public List<CatalogItemDto> Tributos { get; set; } = [];
    /// <summary>Método de pago de NemediClinic → código DIAN por defecto.</summary>
    public Dictionary<string, string> MediosPagoPorDefecto { get; set; } = [];
}
