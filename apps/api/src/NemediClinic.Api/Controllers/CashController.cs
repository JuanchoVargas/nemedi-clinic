using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Cash;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

/// <summary>Caja por sede y usuario: de recepción y del dueño (policy Admin).</summary>
[ApiController]
[Route("api/v1/cash")]
[Authorize(Policy = "Admin")]
public class CashController : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly CashService _cash;
    private readonly AppDbContext _db;

    public CashController(CashService cash, AppDbContext db)
    {
        _cash = cash;
        _db = db;
    }

    /// <summary>Mi caja abierta; 204 si no tengo una.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var caja = await _cash.CurrentAsync(User.UserId(), User.Role(), ct);
        return caja is null ? NoContent() : Ok(caja);
    }

    [HttpPost("open")]
    public async Task<ActionResult<CashSessionDto>> Open([FromBody] OpenCashRequest request, CancellationToken ct) =>
        Ok(await _cash.OpenAsync(request, User.UserId(), User.BranchId(), User.Role(), ct));

    /// <summary>Cajas de la clínica (las más recientes primero). ?formato=xlsx descarga el listado.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] Guid? branchId,
        [FromQuery] Guid? usuarioId, [FromQuery] string? formato, CancellationToken ct)
    {
        var cajas = await _cash.ListAsync(desde, hasta, branchId, usuarioId, User.UserId(), User.Role(), ct);
        if (!string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase))
            return Ok(cajas);

        var rango = $"{desde?.ToString("dd/MM/yyyy") ?? "inicio"} a {hasta?.ToString("dd/MM/yyyy") ?? "hoy"}";
        return File(CashService.ListToExcel(cajas, await ClinicNameAsync(ct), rango), Xlsx,
            $"cajas-{desde:yyyy-MM-dd}-a-{hasta:yyyy-MM-dd}.xlsx");
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<CashSessionDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await _cash.GetAsync(id, User.UserId(), User.Role(), ct));

    /// <summary>Arqueo de una caja en Excel (resumen + movimientos).</summary>
    [HttpGet("sessions/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var caja = await _cash.GetAsync(id, User.UserId(), User.Role(), ct);
        return File(CashService.ToExcel(caja, await ClinicNameAsync(ct)), Xlsx,
            $"arqueo-{caja.FechaApertura:yyyy-MM-dd}-{Slug(caja.Usuario)}.xlsx");
    }

    /// <summary>Ingreso o egreso manual. Solo quien abrió la caja (o el SuperAdmin).</summary>
    [HttpPost("sessions/{id:guid}/movements")]
    public async Task<ActionResult<CashSessionDto>> AddMovement(Guid id, [FromBody] CashMovementRequest request, CancellationToken ct) =>
        Ok(await _cash.AddMovementAsync(id, request, User.UserId(), User.Role(), ct));

    /// <summary>Arqueo y cierre: congela los totales y la diferencia contra lo esperado.</summary>
    [HttpPost("sessions/{id:guid}/close")]
    public async Task<ActionResult<CashSessionDto>> Close(Guid id, [FromBody] CloseCashRequest request, CancellationToken ct) =>
        Ok(await _cash.CloseAsync(id, request, User.UserId(), User.Role(), ct));

    private async Task<string> ClinicNameAsync(CancellationToken ct) =>
        await _db.Tenants.Where(t => t.Id == _db.CurrentTenantId).Select(t => t.Nombre).FirstOrDefaultAsync(ct) ?? "Clínica";

    private static string Slug(string texto) =>
        new string(texto.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
