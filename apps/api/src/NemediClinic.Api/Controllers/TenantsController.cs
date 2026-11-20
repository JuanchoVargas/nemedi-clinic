using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Platform;
using NemediClinic.Application.DTOs.Tenants;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
// Un SuperAdmin solo ve y edita SU tenant. Crear y eliminar tenants es exclusivo del
// PlatformAdmin (api/v1/platform/tenants).
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttachmentService _attachments;
    private readonly ClinicSettingsService _settings;

    public TenantsController(AppDbContext db, AttachmentService attachments, ClinicSettingsService settings)
    {
        _db = db;
        _attachments = attachments;
        _settings = settings;
    }

    /// <summary>Configuración de la clínica (datos, políticas, IVA). La leen todos los roles de clínica: la web la usa para avisar reglas.</summary>
    [HttpGet("current/settings")]
    [Authorize(Policy = "Esteticista")]
    public async Task<ActionResult<ClinicSettingsDto>> GetSettings(CancellationToken ct) => Ok(await _settings.GetAsync(ct));

    /// <summary>Solo el dueño cambia la configuración de su clínica.</summary>
    [HttpPut("current/settings")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<ClinicSettingsDto>> UpdateSettings([FromBody] UpdateClinicSettingsRequest request, CancellationToken ct) =>
        Ok(await _settings.UpdateAsync(request, ct));

    /// <summary>Tenant de la sesión (estado y plan). Lo usa la web para el aviso de cuenta suspendida.</summary>
    [HttpGet("current")]
    [Authorize(Policy = "Esteticista")]
    public async Task<IActionResult> GetCurrent()
    {
        var tenantId = _db.CurrentTenantId;
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantActualDto
            {
                Id = t.Id,
                Nombre = t.Nombre,
                LogoId = t.LogoId,
                Plan = t.Plan.ToString(),
                Estado = t.Estado.ToString()
            })
            .FirstOrDefaultAsync();

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado." });

        return Ok(tenant);
    }

    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetAll([FromQuery] PagedRequest request)
    {
        var tenantId = _db.CurrentTenantId;
        var query = _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(t =>
                t.Nombre.ToLower().Contains(search) ||
                t.NIT.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();

        var tenants = await query
            .OrderBy(t => t.Nombre)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new TenantDto
            {
                Id = t.Id,
                Nombre = t.Nombre,
                NIT = t.NIT,
                Telefono = t.Telefono,
                Email = t.Email,
                Logo = t.Logo,
                LogoId = t.LogoId,
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResponse<TenantDto>
        {
            Items = tenants,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tenantId = _db.CurrentTenantId;
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == id && t.Id == tenantId)
            .Select(t => new TenantDto
            {
                Id = t.Id,
                Nombre = t.Nombre,
                NIT = t.NIT,
                Telefono = t.Telefono,
                Email = t.Email,
                Logo = t.Logo,
                LogoId = t.LogoId,
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado." });

        return Ok(tenant);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request)
    {
        var tenant = id == _db.CurrentTenantId ? await _db.Tenants.FindAsync(id) : null;
        if (tenant is null)
            return NotFound(new { error = "Tenant no encontrado." });

        // Activar o desactivar la clínica es de la plataforma: una clínica inactiva no deja entrar a nadie
        // (ni a su dueño), así que si el dueño la apagara no podría volver a encenderla. Mandar el estado
        // que ya tiene no es un cambio.
        if (request.IsActive.HasValue && request.IsActive.Value != tenant.IsActive)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Solo la plataforma activa o desactiva una clínica." });

        if (request.NIT is not null && request.NIT != tenant.NIT)
        {
            var nitTaken = await _db.Tenants.AnyAsync(t => t.NIT == request.NIT && t.Id != id);
            if (nitTaken)
                return Conflict(new { error = "Ya existe un tenant con ese NIT." });
            tenant.NIT = request.NIT;
        }

        if (request.Nombre is not null) tenant.Nombre = request.Nombre;
        if (request.Telefono is not null) tenant.Telefono = request.Telefono;
        if (request.Email is not null) tenant.Email = request.Email;
        if (request.Logo is not null) tenant.Logo = request.Logo;
        if (request.LogoId.HasValue)
        {
            await _attachments.AssignImageAsync(AttachmentEntityType.Tenant, tenant.Id, request.LogoId, tenant.LogoId);
            tenant.LogoId = request.LogoId;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
