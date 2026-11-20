using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Api.Middleware;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Auth;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IClock _clock;
    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    private readonly ITenantProvider _tenantProvider;
    private readonly IWebHostEnvironment _env;
    private readonly PasswordService _passwords;
    private readonly LoginThrottle _throttle;
    private readonly DeviceCookies _devices;
    private readonly string _jwtSecret;

    public AuthController(AppDbContext db, IJwtService jwt, ITenantProvider tenantProvider, IWebHostEnvironment env, PasswordService passwords, IClock clock, LoginThrottle throttle, DeviceCookies devices, IConfiguration config)
    {
        _jwtSecret = config["Jwt:Secret"] ?? string.Empty;
        _devices = devices;
        _clock = clock;
        _passwords = passwords;
        _throttle = throttle;
        _db = db;
        _jwt = jwt;
        _tenantProvider = tenantProvider;
        _env = env;
    }

    [HttpPost("login")]
    [EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = (request.Email ?? string.Empty).Trim();

        // El correo es único en todo el sistema (AccountEmails), pero puede haber repetidos de antes de
        // esa regla entre clínicas o con un admin de plataforma: entra la cuenta cuya clave coincide,
        // no la primera que aparezca (si no, un repetido le bloqueaba o le desviaba el ingreso a otro).
        // Las cuentas se buscan ANTES de reservar el intento: SQL Server iguala variantes del correo
        // (mayúsculas, ancho, caracteres invisibles, "ß" = "ss"…), así que los contadores van por id.
        var candidatos = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Email == email && !u.IsDeleted)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();
        var platformAdmin = await _db.PlatformAdmins.FirstOrDefaultAsync(a => a.Email == email);
        var cuenta = candidatos.Count > 0 ? LoginThrottle.UserKey(candidatos[0].Id)
            : platformAdmin is not null ? LoginThrottle.PlatformKey(platformAdmin.Id)
            : LoginThrottle.UnknownKey(email);
        var hashCuenta = candidatos.Count > 0 ? candidatos[0].PasswordHash : platformAdmin?.PasswordHash;
        // Correo repetido de antes de la regla: los contadores son de VARIAS cuentas. Un ingreso correcto de
        // una solo devuelve su propia reserva y no los limpia (si no, el dueño de una cuenta repetida le
        // devolvería intentos al atacante de la otra); vencen solos.
        var compartido = candidatos.Count + (platformAdmin is null ? 0 : 1) > 1;
        // Cookie de dispositivo conocido de ESTA cuenta (firmada con su clave actual): la exime del tope por
        // cuenta. Con el correo compartido no: el tope es de todas las cuentas que lo comparten.
        var dispositivo = hashCuenta is null || compartido ? null : _devices.Validate(Request.Cookies[DeviceCookies.Name], cuenta, hashCuenta);

        // Se reserva el intento antes de verificar (LoginThrottle): demasiados fallos de esta cuenta desde
        // esta IP, o de la cuenta en todas partes sin cookie de dispositivo, → 429 aunque llegue la correcta
        var intento = _throttle.Begin(cuenta, HttpContext.Connection.RemoteIpAddress, hashCuenta is not null, dispositivo);
        if (intento.Blocked)
            return TooManyAttempts(intento.RetryAfter!.Value);

        var user = candidatos.FirstOrDefault(u => BCrypt.Net.BCrypt.Verify(request.Password, u.PasswordHash));
        if (user is null)
        {
            if (platformAdmin is not null && BCrypt.Net.BCrypt.Verify(request.Password, platformAdmin.PasswordHash))
            {
                if (compartido) _throttle.Release(intento); else _throttle.Succeeded(intento);
                if (!compartido && platformAdmin.IsActive && !platformAdmin.MustChangePassword)
                    RememberDevice(cuenta, platformAdmin.PasswordHash);
                return await LoginPlatformAdmin(platformAdmin);
            }
            // Correo que no existe: un BCrypt del mismo costo, para que el tiempo de respuesta no lo delate
            if (platformAdmin is null && candidatos.Count == 0)
                BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, DummyHash.Value);

            if (!await _throttle.FailAndDelayAsync(intento, HttpContext.RequestAborted))
                return TooManyAttempts(_throttle.Failed(intento));
            return Unauthorized(new { error = "Credenciales inválidas." });
        }
        if (compartido) _throttle.Release(intento); else _throttle.Succeeded(intento);

        if (!user.IsActive)
            return Unauthorized(new { error = "Usuario desactivado." });

        if (await TenantBlockedAsync(user.TenantId) is { } bloqueo)
            return Unauthorized(new { error = bloqueo });

        var token = _jwt.GenerateToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = _clock.UtcNow.AddDays(7);
        await _db.SaveChangesAsync();

        // Dispositivo conocido: solo con el correo de una sola cuenta y con su clave definitiva (una
        // temporal todavía no es del dueño)
        if (!compartido && !user.MustChangePassword)
            RememberDevice(cuenta, user.PasswordHash);

        return Ok(BuildLoginResponse(user, token, refreshToken));
    }

    private void RememberDevice(string cuenta, string passwordHash)
    {
        var valor = _devices.Issue(cuenta, passwordHash, out var expires);
        Response.Cookies.Append(DeviceCookies.Name, valor, _devices.Options(expires));
    }

    /// <summary>
    /// Alta de usuarios de la clínica del JWT. Antes era anónimo mientras no hubiera ningún SuperAdmin en
    /// toda la base (cualquiera creaba uno en la clínica más antigua); el primer SuperAdmin de una clínica
    /// sale de la plataforma (bootstrap-admin) o, en desarrollo, de auth/seed.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request)
    {
        if (!Enum.IsDefined(request.Rol))
            return BadRequest(new { error = "Rol inválido." });

        // El dueño crea cualquier rol; recepción (Admin) solo esteticistas: es quien da de alta
        // al personal de cabina en el día a día, pero no puede crear pares ni dueños.
        if (!User.IsInRole(nameof(UserRole.SuperAdmin)) && request.Rol != UserRole.Esteticista)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Solo el dueño puede crear usuarios de recepción o administración." });

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "Token sin tenant_id válido." });

        // En todo el sistema (todas las clínicas y la plataforma): el login busca el correo en todas
        if (await AccountEmails.InUseAsync(_db, request.Email, null, HttpContext.RequestAborted))
            return Conflict(new { error = AccountEmails.EnUso });

        if (request.BranchId.HasValue)
        {
            var branchExists = await _db.Branches
                .IgnoreQueryFilters()
                .AnyAsync(b => b.Id == request.BranchId.Value && b.TenantId == tenantId);
            if (!branchExists)
                return BadRequest(new { error = "La sede especificada no existe." });
        }

        var invalidPassword = PasswordPolicy.Validate(request.Password);
        if (invalidPassword is not null)
            return BadRequest(new { error = invalidPassword });

        // MustChangePassword queda en true (valor por defecto): la clave la eligió quien crea al usuario
        var user = new User
        {
            Nombre = request.Nombre,
            Apellido = request.Apellido,
            Email = request.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Rol = request.Rol,
            BranchId = request.BranchId,
            TenantId = tenantId
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Register), new { id = user.Id }, new UserInfo
        {
            Id = user.Id,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Email = user.Email,
            Rol = user.Rol.ToString(),
            TenantId = user.TenantId,
            BranchId = user.BranchId
        });
    }

    [HttpPost("seed")]
    [AllowAnonymous]
    [DevelopmentOnly] // 404 fuera de Development, antes de validar el cuerpo
    public async Task<IActionResult> Seed([FromBody] SeedRequest request)
    {
        // Fuera de Development los tenants los crea SOLO el PlatformAdmin (api/v1/platform/tenants).
        if (!_env.IsDevelopment())
            return NotFound();

        var anyTenant = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync();

        if (anyTenant)
            return Conflict(new { error = "Sistema ya inicializado" });

        await using var tx = await _db.Database.BeginTransactionAsync();

        var tenant = new Tenant
        {
            Nombre = request.TenantNombre,
            NIT = request.TenantNit,
            Email = request.TenantEmail,
            ChannelId = AppDbContext.NemediChannelId,
            FechaActivacion = _clock.Now
        };

        // The tenant provider override ensures SaveChangesAsync stamps the
        // freshly-created tenant's Id on Tenant, Branch and User in a single pass.
        _tenantProvider.SetTenant(tenant.Id);
        _db.Tenants.Add(tenant);

        var branch = new Branch
        {
            Nombre = "Sede Principal",
            TenantId = tenant.Id
        };
        _db.Branches.Add(branch);

        var admin = new User
        {
            Nombre = request.AdminNombre,
            Apellido = request.AdminApellido,
            Email = request.AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
            Rol = UserRole.SuperAdmin,
            TenantId = tenant.Id,
            BranchId = branch.Id,
            MustChangePassword = false // la eligió quien hace el seed
        };
        _db.Users.Add(admin);

        var token = _jwt.GenerateToken(admin);
        var refreshToken = _jwt.GenerateRefreshToken();
        admin.RefreshToken = refreshToken;
        admin.RefreshTokenExpiry = _clock.UtcNow.AddDays(7);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(BuildLoginResponse(admin, token, refreshToken));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var principal = _jwt.ValidateToken(request.Token);
        if (principal is null)
            return Unauthorized(new { error = "Token inválido." });

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var uid))
            return Unauthorized(new { error = "Token inválido." });

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == uid && !u.IsDeleted);

        if (user is null && principal.IsInRole(PlatformAdmin.RoleName))
            return await RefreshPlatformAdmin(principal, uid, request.RefreshToken);

        // El JWT presentado tiene que ser de la clave actual (pwd_stamp): uno de antes de un cambio de clave
        // no renueva aunque traiga un refresh token vigente
        if (user is null
            || user.RefreshToken != request.RefreshToken
            || user.RefreshTokenExpiry < _clock.UtcNow
            || !SessionValidator.StampAllowsRefresh(principal, user.PasswordHash, _jwtSecret))
        {
            return Unauthorized(new { error = "Refresh token inválido o expirado." });
        }

        // Como en el login: un usuario desactivado no renueva (desactivarlo ya revoca su refresh token;
        // esto cubre el que se desactivó por otro camino, p. ej. directo en la base)
        if (!user.IsActive)
            return Unauthorized(new { error = "Usuario desactivado." });

        // Misma puerta que el login: si la clínica se suspendió, la sesión no se renueva
        if (await TenantBlockedAsync(user.TenantId) is { } bloqueo)
            return Unauthorized(new { error = bloqueo });

        var newToken = _jwt.GenerateToken(user);
        var newRefreshToken = _jwt.GenerateRefreshToken();
        var expiry = _clock.UtcNow.AddDays(7);

        // Rotación condicional: solo si el refresh token y la clave siguen siendo los que se leyeron. Un
        // cambio o restablecimiento de clave simultáneo ya revocó este token; sin la condición, esta
        // escritura lo pisaría con uno nuevo y la sesión vieja reviviría.
        var renovados = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id && !u.IsDeleted && u.IsActive
                && u.RefreshToken == request.RefreshToken && u.PasswordHash == user.PasswordHash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.RefreshToken, newRefreshToken)
                .SetProperty(u => u.RefreshTokenExpiry, expiry));
        if (renovados == 0)
            return Unauthorized(new { error = "Refresh token inválido o expirado." });

        return Ok(BuildLoginResponse(user, newToken, newRefreshToken));
    }

    // ── POST /api/v1/auth/change-password ───────────────────────────
    // Cualquier rol, incluido PlatformAdmin. Invalida los refresh tokens y devuelve una sesión
    // nueva (JWT sin el claim pwd_change) para que la web siga sin volver a pedir credenciales.
    [HttpPost("change-password")]
    [EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized(new { error = "Sesión inválida." });

        var ip = HttpContext.Connection.RemoteIpAddress;
        var deviceCookie = Request.Cookies[DeviceCookies.Name];
        // Tras cambiar la clave (también la temporal) se entrega una cookie de dispositivo nueva: las de
        // antes llevaban la firma de la clave vieja y ya no valen
        if (User.IsInRole(PlatformAdmin.RoleName))
        {
            var admin = await _passwords.ChangePlatformAdminPasswordAsync(id, request, ip, deviceCookie, ct);
            if (!await _passwords.IsSharedEmailAsync(admin.Email, ct))
                RememberDevice(LoginThrottle.PlatformKey(admin.Id), admin.PasswordHash);
            return Ok(await IssuePlatformTokens(admin));
        }

        var user = await _passwords.ChangeUserPasswordAsync(id, request, ip, deviceCookie, ct);
        var token = _jwt.GenerateToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = _clock.UtcNow.AddDays(7);
        await _db.SaveChangesAsync(ct);
        if (!await _passwords.IsSharedEmailAsync(user.Email, ct))
            RememberDevice(LoginThrottle.UserKey(user.Id), user.PasswordHash);
        return Ok(BuildLoginResponse(user, token, refreshToken));
    }

    // ── PlatformAdmin (fuera de todo tenant) ────────────────────────
    /// <summary>La clave ya se verificó en Login.</summary>
    private async Task<IActionResult> LoginPlatformAdmin(PlatformAdmin admin)
    {
        if (!admin.IsActive)
            return Unauthorized(new { error = "Usuario desactivado." });

        return Ok(await IssuePlatformTokens(admin));
    }

    private async Task<IActionResult> RefreshPlatformAdmin(ClaimsPrincipal principal, Guid adminId, string refreshToken)
    {
        var admin = await _db.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == adminId && a.IsActive);
        if (admin is null || admin.RefreshToken != refreshToken || admin.RefreshTokenExpiry < _clock.UtcNow
            || !SessionValidator.StampAllowsRefresh(principal, admin.PasswordHash, _jwtSecret))
            return Unauthorized(new { error = "Refresh token inválido o expirado." });

        // Rotación condicional, como la de los usuarios (PlatformAdmin no tiene RowVersion)
        var token = _jwt.GeneratePlatformToken(admin);
        var newRefreshToken = _jwt.GenerateRefreshToken();
        var expiry = _clock.UtcNow.AddDays(7);
        var renovados = await _db.PlatformAdmins
            .Where(a => a.Id == admin.Id && a.IsActive && a.RefreshToken == refreshToken && a.PasswordHash == admin.PasswordHash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.RefreshToken, newRefreshToken)
                .SetProperty(a => a.RefreshTokenExpiry, expiry));
        if (renovados == 0)
            return Unauthorized(new { error = "Refresh token inválido o expirado." });

        return Ok(PlatformResponse(admin, token, newRefreshToken));
    }

    private async Task<LoginResponse> IssuePlatformTokens(PlatformAdmin admin)
    {
        var token = _jwt.GeneratePlatformToken(admin);
        var refreshToken = _jwt.GenerateRefreshToken();
        admin.RefreshToken = refreshToken;
        admin.RefreshTokenExpiry = _clock.UtcNow.AddDays(7);
        await _db.SaveChangesAsync();
        return PlatformResponse(admin, token, refreshToken);
    }

    private static LoginResponse PlatformResponse(PlatformAdmin admin, string token, string refreshToken) =>
        new LoginResponse
        {
            MustChangePassword = admin.MustChangePassword,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = ExpirationOf(token),
            UserInfo = new UserInfo
            {
                Id = admin.Id,
                Nombre = admin.Nombre,
                Apellido = string.Empty,
                Email = admin.Email,
                Rol = PlatformAdmin.RoleName,
                TenantId = Guid.Empty,
                BranchId = null
            }
        };

    /// <summary>Hash de una clave que nadie conoce, con el costo por defecto (el mismo de las claves reales).</summary>
    private static readonly Lazy<string> DummyHash = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")));

    private ObjectResult TooManyAttempts(TimeSpan retryAfter)
    {
        Response.Headers.RetryAfter = LoginThrottle.Seconds(retryAfter);
        return StatusCode(StatusCodes.Status429TooManyRequests, new { error = AuthRateLimitOptions.MensajePorCuenta });
    }

    private static LoginResponse BuildLoginResponse(User user, string token, string refreshToken) =>
        new()
        {
            MustChangePassword = user.MustChangePassword,
            Token = token,
            RefreshToken = refreshToken,
            Expiration = ExpirationOf(token),
            UserInfo = new UserInfo
            {
                Id = user.Id,
                Nombre = user.Nombre,
                Apellido = user.Apellido,
                Email = user.Email,
                Rol = user.Rol.ToString(),
                TenantId = user.TenantId,
                BranchId = user.BranchId
            }
        };

    /// <summary>
    /// Vencimiento real del JWT (su claim exp). Antes se informaba un fijo de 1 h aunque el token
    /// durara lo configurado en Jwt:ExpirationMinutes (480 en Development). Va en UTC; el conversor
    /// de fechas lo serializa en hora local.
    /// </summary>
    private static DateTime ExpirationOf(string token) =>
        DateTime.SpecifyKind(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo, DateTimeKind.Utc);

    /// <summary>
    /// Motivo por el que la clínica no puede iniciar sesión, o null si puede. Una clínica eliminada o
    /// inactiva no entra nunca; una suspendida por mora tampoco abre sesión nueva (la sesión ya
    /// abierta sigue en modo lectura hasta que expire: de eso se encarga TenantStatusMiddleware).
    /// Va con IgnoreQueryFilters porque aún no hay JWT y el filtro global no tiene tenant.
    /// </summary>
    private async Task<string?> TenantBlockedAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.IsDeleted, t.IsActive, t.Estado })
            .FirstOrDefaultAsync();

        if (tenant is null || tenant.IsDeleted || !tenant.IsActive)
            return "Esta clínica ya no está activa. Comunícate con el administrador.";

        if (tenant.Estado == TenantEstado.Suspendido)
            return "Cuenta suspendida por mora. Comunícate con soporte para reactivarla.";

        return null;
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id");
        return claim is not null && Guid.TryParse(claim.Value, out var tid) ? tid : Guid.Empty;
    }
}
