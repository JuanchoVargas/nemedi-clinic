using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Common;
using NemediClinic.Application.DTOs.Users;
using NemediClinic.Api.Services;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PagedRequest request)
    {
        var query = _db.Users.AsNoTracking();

        // Admin only sees users from their branch; SuperAdmin sees all
        if (!User.IsInRole("SuperAdmin"))
        {
            var branchId = GetBranchId();
            if (branchId.HasValue)
                query = query.Where(u => u.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(u =>
                u.Nombre.ToLower().Contains(search) ||
                u.Apellido.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();
        // La esteticista usa la lista para elegir con quién agendar: nombre y rol, sin correos
        var verCorreo = !User.IsInRole("Esteticista");

        var users = await query
            .OrderBy(u => u.Apellido).ThenBy(u => u.Nombre)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Nombre = u.Nombre,
                Apellido = u.Apellido,
                Email = verCorreo ? u.Email : string.Empty,
                Rol = u.Rol.ToString(),
                BranchId = u.BranchId,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResponse<UserDto>
        {
            Items = users,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var verCorreo = !User.IsInRole("Esteticista");
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Nombre = u.Nombre,
                Apellido = u.Apellido,
                Email = verCorreo ? u.Email : string.Empty,
                Rol = u.Rol.ToString(),
                BranchId = u.BranchId,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (user is null)
            return NotFound(new { error = "Usuario no encontrado." });

        return Ok(user);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, [FromServices] UserService users, CancellationToken ct)
    {
        if (GetActor() is not { } actor)
            return Forbid();
        await users.UpdateAsync(id, request, actor.Id, actor.Rol, ct);
        return NoContent();
    }

    // ── POST /api/v1/users/{id}/reset-password ── clave temporal (se muestra una vez) + cambio obligatorio
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromServices] PasswordService passwords, CancellationToken ct)
    {
        if (GetActor() is not { } actor)
            return Forbid();
        return Ok(await passwords.ResetUserPasswordAsync(id, actor.Id, actor.Rol, ct));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(Guid id, [FromServices] UserService users, CancellationToken ct)
    {
        if (GetActor() is not { } actor)
            return Forbid();
        await users.DeleteAsync(id, actor.Id, actor.Rol, ct);
        return NoContent();
    }

    /// <summary>Quién hace la petición (id y rol del JWT), o null si el token no los trae bien.</summary>
    private (Guid Id, UserRole Rol)? GetActor()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return null;
        if (!Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), ignoreCase: false, out var rol) || !Enum.IsDefined(rol))
            return null;
        return (id, rol);
    }

    private Guid? GetBranchId()
    {
        var claim = User.FindFirst("branch_id");
        return claim is not null && Guid.TryParse(claim.Value, out var bid) ? bid : null;
    }
}
