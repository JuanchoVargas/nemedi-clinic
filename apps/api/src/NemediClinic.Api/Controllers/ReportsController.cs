using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Api.Services;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

/// <summary>Reportes gerenciales (policy Admin). Cada uno con ?formato=xlsx para descargar en Excel.</summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = "Admin")]
public class ReportsController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ReportService _reports;
    private readonly AppDbContext _db;

    public ReportsController(ReportService reports, AppDbContext db)
    {
        _reports = reports;
        _db = db;
    }

    [HttpGet("sales")]
    public async Task<IActionResult> Sales([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] Guid? branchId,
        [FromQuery] string? formato, CancellationToken ct)
    {
        var r = await _reports.SalesAsync(desde, hasta, branchId, ct);
        return Excel(formato) ? File(ReportService.SalesExcel(r, await ClinicAsync(ct)), Xlsx, $"ventas-{r.Desde:yyyy-MM-dd}-a-{r.Hasta:yyyy-MM-dd}.xlsx") : Ok(r);
    }

    [HttpGet("procedures")]
    public async Task<IActionResult> Procedures([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] Guid? branchId,
        [FromQuery] string? formato, CancellationToken ct)
    {
        var r = await _reports.ProceduresAsync(desde, hasta, branchId, ct);
        return Excel(formato) ? File(ReportService.ProceduresExcel(r, await ClinicAsync(ct)), Xlsx, $"procedimientos-{r.Desde:yyyy-MM-dd}-a-{r.Hasta:yyyy-MM-dd}.xlsx") : Ok(r);
    }

    [HttpGet("production")]
    public async Task<IActionResult> Production([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] Guid? branchId,
        [FromQuery] string? formato, CancellationToken ct)
    {
        var r = await _reports.ProductionAsync(desde, hasta, branchId, ct);
        return Excel(formato) ? File(ReportService.ProductionExcel(r, await ClinicAsync(ct)), Xlsx, $"produccion-{r.Desde:yyyy-MM-dd}-a-{r.Hasta:yyyy-MM-dd}.xlsx") : Ok(r);
    }

    [HttpGet("cartera")]
    public async Task<IActionResult> Cartera([FromQuery] string? formato, CancellationToken ct)
    {
        var r = await _reports.CarteraAsync(ct);
        return Excel(formato) ? File(ReportService.CarteraExcel(r, await ClinicAsync(ct)), Xlsx, $"cartera-{r.Corte:yyyy-MM-dd}.xlsx") : Ok(r);
    }

    private static bool Excel(string? formato) => string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ClinicAsync(CancellationToken ct) =>
        await _db.Tenants.Where(t => t.Id == _db.CurrentTenantId).Select(t => t.Nombre).FirstOrDefaultAsync(ct) ?? "Clínica";
}
