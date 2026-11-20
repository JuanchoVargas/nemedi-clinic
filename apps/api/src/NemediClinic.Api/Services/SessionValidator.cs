using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Domain.Entities;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Infrastructure.Services;

namespace NemediClinic.Api.Services;

/// <summary>
/// Un JWT dura hasta 60 min, pero la cuenta puede cambiar antes: la desactivan, la borran, le cambian
/// el rol o la clave, o la clínica deja de estar activa. En cada petición autenticada
/// (JwtBearerEvents.OnTokenValidated en Program.cs) se lee la cuenta del token en la base y, si ya no
/// coincide, el token se rechaza (401): la web intenta renovar con el refresh token, que solo sirve a
/// una cuenta activa de una clínica activa y trae el rol actual.
/// Cubre a los usuarios de clínica y a los PlatformAdmins. Una consulta por clave primaria por petición
/// (la clínica va en la misma), sin caché, para que valga igual con varias instancias del API.
/// Una clínica Suspendida no se rechaza aquí: sigue en solo lectura (TenantStatusMiddleware).
/// </summary>
public static class SessionValidator
{
    public const string TenantClaim = "tenant_id";

    /// <summary>
    /// Lo que dice la base de la cuenta del token (null = no existe o está borrada).
    /// <paramref name="SelloClave"/> = <see cref="PasswordStamp.Compute"/> de su hash actual.
    /// </summary>
    public sealed record Account(bool Activo, string Rol, Guid? TenantId, bool ClinicaActiva, string SelloClave);

    /// <summary>Puro: el motivo del rechazo o null si el token aún describe bien la cuenta.</summary>
    public static string? Check(ClaimsPrincipal principal, Account? cuenta)
    {
        if (cuenta is null)
            return "La cuenta ya no existe.";
        if (!cuenta.Activo)
            return "La cuenta está desactivada.";
        if (principal.FindFirstValue(ClaimTypes.Role) != cuenta.Rol)
            return "El rol de la cuenta cambió.";
        if (cuenta.TenantId is { } tenantId
            && !(Guid.TryParse(principal.FindFirstValue(TenantClaim), out var claim) && claim == tenantId))
            return "La cuenta no pertenece a esta clínica.";
        if (!cuenta.ClinicaActiva)
            return "La clínica ya no está activa.";
        // Sin sello (token de antes de esta regla) o con otro: la clave cambió después de emitirlo
        if (!PasswordStamp.Matches(principal.FindFirstValue(PasswordStamp.ClaimName), cuenta.SelloClave))
            return "La clave de la cuenta cambió.";
        return null;
    }

    /// <summary>
    /// Para /auth/refresh: el JWT que se presenta tiene que ser de la clave actual. Si trae pwd_stamp y no
    /// coincide, la clave cambió después de emitirlo → no se renueva. Sin el claim (emitido antes de esta
    /// regla) puede renovar mientras su refresh token valga: así el despliegue no saca a nadie; el JWT que
    /// recibe ya trae el sello.
    /// </summary>
    public static bool StampAllowsRefresh(ClaimsPrincipal principal, string passwordHash, string jwtSecret) =>
        principal.FindFirstValue(PasswordStamp.ClaimName) is not { } claim
        || PasswordStamp.Matches(claim, PasswordStamp.Compute(passwordHash, jwtSecret));

    public static async Task<string?> ValidateAsync(ClaimsPrincipal principal, AppDbContext db, string jwtSecret, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return "El token no identifica una cuenta.";

        Account? cuenta;
        if (principal.IsInRole(PlatformAdmin.RoleName))
        {
            var admin = await db.PlatformAdmins.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.IsActive, a.PasswordHash })
                .FirstOrDefaultAsync(ct);
            cuenta = admin is null ? null
                : new Account(admin.IsActive, PlatformAdmin.RoleName, null, true, PasswordStamp.Compute(admin.PasswordHash, jwtSecret));
        }
        else
        {
            // Sin JWT aceptado aún no hay tenant en el contexto: se busca por id en todas las clínicas
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == id && !u.IsDeleted)
                .Select(u => new
                {
                    u.IsActive,
                    u.Rol,
                    u.TenantId,
                    u.PasswordHash,
                    ClinicaActiva = db.Tenants.IgnoreQueryFilters().Any(t => t.Id == u.TenantId && !t.IsDeleted && t.IsActive),
                })
                .FirstOrDefaultAsync(ct);
            cuenta = user is null ? null
                : new Account(user.IsActive, user.Rol.ToString(), user.TenantId, user.ClinicaActiva, PasswordStamp.Compute(user.PasswordHash, jwtSecret));
        }
        return Check(principal, cuenta);
    }
}
