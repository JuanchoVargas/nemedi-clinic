using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Branches;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Tenants;
using NemediClinic.Api.Services;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Domain.Entities;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = "Admin")]
public class BranchesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ClinicSettingsService _settings;

    public BranchesController(AppDbContext db, ClinicSettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>Horario de atención de la sede (vacío = sin restricción).</summary>
    [HttpGet("{id:guid}/schedule")]
    public async Task<ActionResult<List<BranchScheduleDayDto>>> GetSchedule(Guid id, CancellationToken ct) =>
        Ok(await _settings.GetScheduleAsync(id, ct));

    /// <summary>Reemplaza el horario completo. Una franja por día y turno; lista vacía = sin restricción.</summary>
    [HttpPut("{id:guid}/schedule")]
    public async Task<ActionResult<List<BranchScheduleDayDto>>> SetSchedule(Guid id, [FromBody] List<BranchScheduleDayDto> dias, CancellationToken ct) =>
        Ok(await _settings.SetScheduleAsync(id, dias, ct));

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PagedRequest request)
    {
        var query = _db.Branches.AsNoTracking();

        // Admin only sees branches from their tenant (already filtered by global filter)
        // but non-SuperAdmin further filtered by their branch
        if (!User.IsInRole("SuperAdmin"))
        {
            var branchId = GetBranchId();
            if (branchId.HasValue)
                query = query.Where(b => b.Id == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(b => b.Nombre.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();

        var branches = await query
            .OrderBy(b => b.Nombre)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(b => new BranchDto
            {
                Id = b.Id,
                Nombre = b.Nombre,
                Direccion = b.Direccion,
                Telefono = b.Telefono,
                TenantId = b.TenantId,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResponse<BranchDto>
        {
            Items = branches,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var branch = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BranchDto
            {
                Id = b.Id,
                Nombre = b.Nombre,
                Direccion = b.Direccion,
                Telefono = b.Telefono,
                TenantId = b.TenantId,
                CreatedAt = b.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (branch is null)
            return NotFound(new { error = "Sede no encontrada." });

        return Ok(branch);
    }

    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateBranchRequest request)
    {
        var branch = new Branch
        {
            Nombre = request.Nombre,
            Direccion = request.Direccion,
            Telefono = request.Telefono
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = branch.Id }, new BranchDto
        {
            Id = branch.Id,
            Nombre = branch.Nombre,
            Direccion = branch.Direccion,
            Telefono = branch.Telefono,
            TenantId = branch.TenantId,
            CreatedAt = branch.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchRequest request)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch is null)
            return NotFound(new { error = "Sede no encontrada." });

        if (request.Nombre is not null) branch.Nombre = request.Nombre;
        if (request.Direccion is not null) branch.Direccion = request.Direccion;
        if (request.Telefono is not null) branch.Telefono = request.Telefono;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch is null)
            return NotFound(new { error = "Sede no encontrada." });

        branch.IsDeleted = true;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid? GetBranchId()
    {
        var claim = User.FindFirst("branch_id");
        return claim is not null && Guid.TryParse(claim.Value, out var bid) ? bid : null;
    }
}
