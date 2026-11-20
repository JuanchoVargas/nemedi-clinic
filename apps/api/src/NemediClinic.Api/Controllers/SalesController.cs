using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Sales;
using NemediClinic.Domain.Enums;

namespace NemediClinic.Api.Controllers;

/// <summary>Ventas: de recepción y del dueño (policy Admin). La esteticista no vende ni ve ventas.</summary>
[ApiController]
[Route("api/v1/sales")]
[Authorize(Policy = "Admin")]
public class SalesController : ControllerBase
{
    private readonly SaleService _sales;

    public SalesController(SaleService sales) => _sales = sales;

    // ── GET /api/v1/sales?desde=&hasta=&estado=&branchId=&patientId=&conSaldo=&search=&page=&pageSize= ──
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SaleDto>>> List(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] SaleStatus? estado,
        [FromQuery] Guid? branchId, [FromQuery] Guid? patientId, [FromQuery] bool? conSaldo,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await _sales.ListAsync(new SaleListFilter
        {
            Desde = desde, Hasta = hasta, Estado = estado, BranchId = branchId, PatientId = patientId,
            ConSaldo = conSaldo, Search = search, Page = page, PageSize = pageSize
        }, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDto>> Get(Guid id, CancellationToken ct) => Ok(await _sales.GetAsync(id, ct));

    /// <summary>Buscador de ítems vendibles (productos con precio, procedimientos y paquetes activos).</summary>
    [HttpGet("catalog")]
    public async Task<ActionResult<List<SaleCatalogItemDto>>> Catalog([FromQuery] string? search, CancellationToken ct) =>
        Ok(await _sales.CatalogAsync(search, ct));

    /// <summary>Calcula base, IVA y totales de unas líneas sin guardar nada.</summary>
    [HttpPost("quote")]
    public async Task<ActionResult<SaleQuoteDto>> Quote([FromBody] QuoteSaleRequest request, CancellationToken ct) =>
        Ok(await _sales.QuoteAsync(request, ct));

    [HttpPost]
    public async Task<ActionResult<SaleDto>> Create([FromBody] SaveSaleRequest request, CancellationToken ct)
    {
        var dto = await _sales.CreateAsync(request, User.UserId(), User.BranchId(), ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>Edita un borrador (y lo confirma si Confirmar = true).</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SaleDto>> Update(Guid id, [FromBody] SaveSaleRequest request, CancellationToken ct) =>
        Ok(await _sales.UpdateDraftAsync(id, request, User.UserId(), User.BranchId(), ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<SaleDto>> Confirm(Guid id, [FromBody] ConfirmSaleRequest request, CancellationToken ct) =>
        Ok(await _sales.ConfirmAsync(id, request, User.UserId(), ct));

    /// <summary>Abono a una venta confirmada con saldo. 422 si supera el saldo.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<SaleDto>> AddPayment(Guid id, [FromBody] SalePaymentRequest request, CancellationToken ct) =>
        Ok(await _sales.AddPaymentAsync(id, request, User.UserId(), ct));

    /// <summary>Anula y revierte inventario, paquetes y pagos. Idempotente.</summary>
    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<VoidSaleResultDto>> Void(Guid id, [FromBody] VoidSaleRequest request, CancellationToken ct) =>
        Ok(await _sales.VoidAsync(id, request, User.UserId(), ct));

    /// <summary>Descarta un borrador. Una venta confirmada no se borra: se anula.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken ct)
    {
        await _sales.DeleteDraftAsync(id, ct);
        return NoContent();
    }

    /// <summary>Factura electrónica. 501 mientras no haya un IElectronicInvoiceProvider registrado.</summary>
    [HttpPost("{id:guid}/invoice")]
    public async Task<ActionResult<SaleDto>> Invoice(Guid id, CancellationToken ct) =>
        Ok(await _sales.IssueInvoiceAsync(id, User.UserId(), ct));
}
