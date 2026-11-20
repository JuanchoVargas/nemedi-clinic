using System.Net;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Auth;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services;

/// <summary>
/// Cambio y restablecimiento de contraseñas (usuarios de clínica y PlatformAdmin).
///   · Cambiar: exige la actual, aplica PasswordPolicy, apaga MustChangePassword e invalida los
///     refresh tokens.
///   · Restablecer: clave temporal que se devuelve UNA vez, MustChangePassword = true, refresh
///     tokens invalidados y, si hay SMTP configurado, correo al usuario.
/// En ambos casos el hash cambia, así que los JWT emitidos antes dejan de valer en la siguiente
/// petición (su sello pwd_stamp ya no coincide: PasswordStamp + SessionValidator); change-password
/// devuelve uno nuevo.
/// </summary>
public class PasswordService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly LoginThrottle _throttle;
    private readonly DeviceCookies _devices;

    public PasswordService(AppDbContext db, IEmailService email, LoginThrottle throttle, DeviceCookies devices)
    {
        _devices = devices;
        _db = db;
        _email = email;
        _throttle = throttle;
    }

    // ── Cambiar la propia ───────────────────────────────────────────
    public async Task<User> ChangeUserPasswordAsync(Guid userId, ChangePasswordRequest request, IPAddress? ip, string? deviceCookie, CancellationToken ct)
    {
        // Filtro global: el usuario del JWT siempre pertenece a su tenant. Un desactivado ni siquiera
        // llega aquí (SessionValidator rechaza su JWT); esto cubre la carrera entre ambos.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Sesión inválida.");

        var compartido = await IsSharedEmailAsync(user.Email, ct);
        var (hash, intento) = await NewHashAsync(LoginThrottle.UserKey(user.Id), ip, compartido ? null : deviceCookie, request, user.PasswordHash, ct);
        try
        {
            user.PasswordHash = hash;
            user.MustChangePassword = false;
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            await SaveUserAsync(ct, "La sesión cambió mientras se guardaba la contraseña. Vuelve a intentarlo.");
        }
        finally
        {
            _throttle.Release(intento);
        }
        await ResetCountersAsync(user.Email, LoginThrottle.UserKey(user.Id), ct);
        return user;
    }

    public async Task<PlatformAdmin> ChangePlatformAdminPasswordAsync(Guid adminId, ChangePasswordRequest request, IPAddress? ip, string? deviceCookie, CancellationToken ct)
    {
        var admin = await _db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == adminId && a.IsActive, ct)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Sesión inválida.");

        var compartido = await IsSharedEmailAsync(admin.Email, ct);
        var (hash, intento) = await NewHashAsync(LoginThrottle.PlatformKey(admin.Id), ip, compartido ? null : deviceCookie, request, admin.PasswordHash, ct);
        try
        {
            admin.PasswordHash = hash;
            admin.MustChangePassword = false;
            admin.RefreshToken = null;
            admin.RefreshTokenExpiry = null;
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _throttle.Release(intento);
        }
        await ResetCountersAsync(admin.Email, LoginThrottle.PlatformKey(admin.Id), ct);
        return admin;
    }

    /// <summary>
    /// Limpia los contadores de intentos de la cuenta tras cambiar o restablecer su clave, salvo los que
    /// comparte con otras cuentas por un correo repetido de antes de la regla de correo único.
    /// </summary>
    private async Task ResetCountersAsync(string email, string cuenta, CancellationToken ct)
    {
        // Con el correo repetido (cuentas de antes de la regla de correo único), los contadores del login son
        // de varias cuentas: el cambio de clave de una no los limpia (vencen solos). La clave propia de la
        // cuenta (la que usa su change-password) sí se limpia si no es la compartida.
        var (usuarios, admins) = await AccountsWithEmailAsync(email, ct);
        var deLogin = usuarios.Count > 0 ? LoginThrottle.UserKey(usuarios[0])
            : admins.Count > 0 ? LoginThrottle.PlatformKey(admins[0]) : null;
        var compartido = usuarios.Count + admins.Count > 1;
        if (!compartido || cuenta != deLogin)
            _throttle.Reset(cuenta);
    }

    /// <summary>
    /// El correo lo comparten varias cuentas (repetido de antes de la regla de correo único): los contadores
    /// del login son de todas, así que la cookie de dispositivo no exime ni se entrega (AuthController).
    /// </summary>
    public async Task<bool> IsSharedEmailAsync(string email, CancellationToken ct)
    {
        var (usuarios, admins) = await AccountsWithEmailAsync(email, ct);
        return usuarios.Count + admins.Count > 1;
    }

    /// <summary>Usuarios no borrados (el más antiguo primero) y admins de plataforma con ese correo.</summary>
    private async Task<(List<Guid> Usuarios, List<Guid> Admins)> AccountsWithEmailAsync(string email, CancellationToken ct)
    {
        var usuarios = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Email == email && !u.IsDeleted)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Id)
            .ToListAsync(ct);
        var admins = await _db.PlatformAdmins.Where(a => a.Email == email).Select(a => a.Id).ToListAsync(ct);
        return (usuarios, admins);
    }

    /// <summary>
    /// Un /auth/refresh simultáneo cambia la fila (RowVersion): en vez de un 500, 409 para reintentar. La
    /// renovación, a su vez, solo se guarda si la clave no cambió (AuthController.Refresh).
    /// </summary>
    private async Task SaveUserAsync(CancellationToken ct, string mensajeConflicto)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict(mensajeConflicto);
        }
    }

    /// <summary>
    /// La clave actual cuenta como un intento de ingreso de esa cuenta (LoginThrottle). Si es correcta, la
    /// reserva se devuelve (Release) recién después del cambio efectivo, en el llamador; un cambio inválido
    /// (política, la misma clave) también la devuelve y no limpia nada. La limpieza de contadores la hace
    /// ResetCountersAsync tras guardar, y respeta los correos compartidos.
    /// </summary>
    private async Task<(string Hash, LoginThrottle.Attempt Intento)> NewHashAsync(string cuenta, IPAddress? ip, string? deviceCookie, ChangePasswordRequest request, string currentHash, CancellationToken ct)
    {
        // Como en el login: la cookie de dispositivo de esta cuenta (con su clave actual) la exime del tope por cuenta
        var dispositivo = _devices.Validate(deviceCookie, cuenta, currentHash);
        var intento = _throttle.Begin(cuenta, ip, cuentaReal: true, dispositivo);
        if (intento.Blocked)
            throw ApiException.TooManyRequests(AuthRateLimitOptions.MensajePorCuenta, intento.RetryAfter!.Value);
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, currentHash))
        {
            if (!await _throttle.FailAndDelayAsync(intento, ct))
                throw ApiException.TooManyRequests(AuthRateLimitOptions.MensajePorCuenta, _throttle.Failed(intento));
            throw ApiException.BadRequest("La contraseña actual no es correcta.");
        }
        var invalid = PasswordPolicy.Validate(request.NewPassword);
        if (invalid is null && BCrypt.Net.BCrypt.Verify(request.NewPassword, currentHash))
            invalid = "La contraseña nueva debe ser distinta de la actual.";
        if (invalid is not null)
        {
            _throttle.Release(intento);
            throw ApiException.BadRequest(invalid);
        }

        return (BCrypt.Net.BCrypt.HashPassword(request.NewPassword), intento);
    }

    // ── Restablecer la de otro ──────────────────────────────────────
    /// <summary>SuperAdmin/Admin sobre usuarios de SU tenant. Un Admin solo restablece esteticistas (UserRules).</summary>
    public async Task<ResetPasswordResponse> ResetUserPasswordAsync(Guid targetUserId, Guid actorId, UserRole actorRol, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
            ?? throw ApiException.NotFound("Usuario no encontrado.");

        if (user.Id == actorId)
            throw ApiException.BadRequest("Para tu propia contraseña usa \"Cambiar contraseña\".");
        if (UserRules.CanResetPassword(actorRol, user.Rol) is { } prohibido)
            throw new ApiException(StatusCodes.Status403Forbidden, prohibido);

        return await ResetAsync(user, ct);
    }

    /// <summary>PlatformAdmin sobre el SuperAdmin de un tenant (el más antiguo, el que creó bootstrap-admin).</summary>
    public async Task<ResetPasswordResponse> ResetTenantAdminPasswordAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw ApiException.NotFound("Tenant no encontrado.");

        var admin = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Rol == UserRole.SuperAdmin && !u.IsDeleted)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.Conflict("Este tenant aún no tiene SuperAdmin. Usa \"Crear admin\".");

        return await ResetAsync(admin, ct);
    }

    private async Task<ResetPasswordResponse> ResetAsync(User user, CancellationToken ct)
    {
        var temporal = PasswordPolicy.GenerateTemporary();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporal);
        user.MustChangePassword = true;
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await SaveUserAsync(ct, "El usuario estaba renovando su sesión en ese momento. Vuelve a intentar el restablecimiento.");
        await ResetCountersAsync(user.Email, LoginThrottle.UserKey(user.Id), ct); // la clave nueva entra aunque la cuenta estuviera bloqueada

        var sent = await _email.SendAsync(
            user.Email,
            "Tu contraseña fue restablecida",
            $"Hola {user.Nombre},\n\nTu contraseña fue restablecida. Ingresa con esta clave temporal:\n\n    {temporal}\n\n" +
            "El sistema te pedirá cambiarla al entrar. Si no pediste este cambio, avisa al administrador de tu clínica.\n",
            ct);

        return new ResetPasswordResponse { UserId = user.Id, Email = user.Email, PasswordTemporal = temporal, EmailEnviado = sent };
    }
}
