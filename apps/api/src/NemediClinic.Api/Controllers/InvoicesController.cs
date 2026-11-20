using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Invoicing;

namespace NemediClinic.Api.Controllers;

/// <summary>
/// Facturación electrónica (policy Admin: recepción y dueño). La configuración (credenciales,
/// emisor, rangos) es solo del dueño. 501 en todo lo que emite mientras la clínica no la configure.
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
[Authorize(Policy = "Admin")]
public class InvoicesController : ControllerBase
{
    private readonly InvoiceService _invoices;
    private readonly InvoicingSettingsService _settings;

    public InvoicesController(InvoiceService invoices, InvoicingSettingsService settings)
    {
        _invoices = invoices;
        _settings = settings;
    }

    // ── GET /api/v1/invoices?desde=&hasta=&estado=&tipoDocumento=&branchId=&patientId=&saleId=&search=&page=&pageSize=&formato=xlsx ──
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] string? estado, [FromQuery] string? tipoDocumento,
        [FromQuery] Guid? branchId, [FromQuery] Guid? patientId, [FromQuery] Guid? saleId, [FromQuery] string? search,
        [FromQuery] string? formato, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var filter = new InvoiceListFilter
        {
            Desde = desde, Hasta = hasta, Estado = estado, TipoDocumento = tipoDocumento, BranchId = branchId,
            PatientId = patientId, SaleId = saleId, Search = search, Page = page, PageSize = pageSize,
        };
        if (string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var (contenido, nombre) = await _invoices.ExportAsync(filter, ct);
            return File(contenido, InvoiceService.XlsxContentType, nombre);
        }
        return Ok(await _invoices.ListAsync(filter, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ElectronicInvoiceDto>> Get(Guid id, CancellationToken ct) => Ok(await _invoices.GetAsync(id, ct));

    /// <summary>Emite la factura de una venta confirmada o de un pago de paquete sin venta.</summary>
    [HttpPost]
    public async Task<ActionResult<ElectronicInvoiceDto>> Issue([FromBody] IssueInvoiceRequest request, CancellationToken ct) =>
        Ok(await _invoices.IssueAsync(request, User.UserId(), ct));

    /// <summary>Pendiente: reenvía con la misma referencia. Rechazada: emite de nuevo con los datos actuales.</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<ElectronicInvoiceDto>> Retry(Guid id, CancellationToken ct) =>
        Ok(await _invoices.RetryAsync(id, User.UserId(), ct));

    [HttpPost("{id:guid}/credit-note")]
    public async Task<ActionResult<ElectronicInvoiceDto>> CreditNote(Guid id, [FromBody] CreditNoteRequest request, CancellationToken ct) =>
        Ok(await _invoices.CreditNoteAsync(id, request, User.UserId(), ct));

    [HttpPost("{id:guid}/send-email")]
    public async Task<ActionResult<ElectronicInvoiceDto>> SendEmail(Guid id, [FromBody] SendInvoiceEmailRequest request, CancellationToken ct) =>
        Ok(await _invoices.SendEmailAsync(id, request, ct));

    // ── Configuración ──

    [HttpGet("settings")]
    public async Task<ActionResult<InvoicingSettingsDto>> GetSettings(CancellationToken ct) => Ok(await _settings.GetAsync(ct));

    [HttpPut("settings")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<InvoicingSettingsDto>> UpdateSettings([FromBody] UpdateInvoicingSettingsRequest request, CancellationToken ct) =>
        Ok(await _settings.UpdateAsync(request, ct));

    /// <summary>Autentica con las credenciales del cuerpo (o las guardadas) y trae emisor y rangos.</summary>
    [HttpPost("settings/test")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<TestConnectionResultDto>> Test([FromBody] TestConnectionRequest request, CancellationToken ct) =>
        Ok(await _settings.TestAsync(request, ct));

    /// <summary>Tablas DIAN de los selects de la configuración.</summary>
    [HttpGet("catalogs")]
    public ActionResult<InvoicingCatalogsDto> Catalogs() => Ok(_settings.Catalogs());

    /// <summary>municipios | unidades, buscando por código o nombre.</summary>
    [HttpGet("catalogs/{tabla}")]
    public ActionResult<List<CatalogItemDto>> SearchCatalog(string tabla, [FromQuery] string? search) =>
        Ok(_settings.SearchCatalog(tabla, search));
}
