using System.Security.Claims;

namespace NemediClinic.Api.Controllers;

/// <summary>Lo que los controllers nuevos leen del JWT: usuario, rol y sede.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    public static string? Role(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;

    /// <summary>Sede del usuario (claim branch_id); null si no tiene.</summary>
    public static Guid? BranchId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("branch_id")?.Value, out var id) ? id : null;
}
