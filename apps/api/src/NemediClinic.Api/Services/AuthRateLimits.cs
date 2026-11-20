using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace NemediClinic.Api.Services;

/// <summary>
/// Límites de intentos en el ingreso (sección RateLimiting:Auth). Los contadores viven en memoria del
/// proceso (con varias instancias del API cada una lleva su cuenta; un reinicio los pone en cero):
///   · Por IP: login, refresh y change-password comparten <see cref="PorIpPorMinuto"/> peticiones por
///     minuto (política <see cref="PolicyName"/> del rate limiter de ASP.NET Core; IPv6 por /64). Frena
///     el relleno de credenciales desde una misma máquina. La IP es la de la conexión; X-Forwarded-For
///     solo cuenta si viene de un proxy declarado (ForwardedHeaders en Program.cs).
///   · <see cref="LoginThrottle"/>, por CUENTA (su id: las variantes de un correo que SQL Server
///     considera iguales caen en el mismo contador), sin dejar por fuera al dueño legítimo:
///       – (cuenta, IP): <see cref="FallosPorCorreoEIp"/> claves equivocadas en <see cref="MinutosBloqueo"/>
///         minutos bloquean esa cuenta desde esa IP (429), aunque llegue la correcta;
///       – cuenta (todas las IPs): cada fallo responde con un retardo que se duplica
///         (<see cref="RetardoBaseMs"/> × 2^n, hasta <see cref="RetardoMaximoMs"/>) y con
///         <see cref="FallosPorCorreoPorHora"/> fallos en una hora la cuenta se bloquea, SALVO para un
///         intento que trae la cookie de dispositivo conocido de esa cuenta (<see cref="DeviceCookies"/>,
///         firmada, sin estado en el servidor: sobrevive a reinicios). Ese dispositivo tiene su propio
///         bloqueo corto: <see cref="FallosPorDispositivo"/> en <see cref="MinutosBloqueo"/> minutos.
///     Peor caso para el dueño: durante un ataque con muchas IPs, entrar desde un navegador SIN la cookie
///     (uno nuevo, otro equipo, cookies borradas) da 429 mientras el ataque mantenga la cuenta sobre su tope
///     por hora. Una sesión ya abierta sigue (la renovación por refresh no pasa por aquí). Tras un
///     restablecimiento entra con la clave temporal (el restablecimiento limpia los contadores) y al
///     cambiarla recibe una cookie nueva. Nada recuerda IPs.
///     Restablecer o cambiar la clave limpia los contadores de la cuenta (salvo los compartidos por un
///     correo repetido de antes de la regla) e invalida sus cookies.
/// </summary>
public class AuthRateLimitOptions
{
    public const string Section = "RateLimiting:Auth";
    public const string PolicyName = "auth";

    /// <summary>Sin configurar: 30 en producción; 1000 en Development, donde las pruebas e2e entran
    /// decenas de veces por minuto desde la misma máquina.</summary>
    public int? PorIpPorMinuto { get; set; }
    public int FallosPorCorreoEIp { get; set; } = 10;
    public int MinutosBloqueo { get; set; } = 15;
    public int FallosPorCorreoPorHora { get; set; } = 100;
    public int FallosPorDispositivo { get; set; } = 10;
    /// <summary>Vigencia de la cookie de dispositivo conocido.</summary>
    public int DiasDispositivo { get; set; } = 60;
    public int RetardoBaseMs { get; set; } = 500;
    public int RetardoMaximoMs { get; set; } = 10_000;
    /// <summary>Peticiones esperando su retardo a la vez; por encima se responde 429 sin esperar.</summary>
    public int MaxEsperando { get; set; } = 100;
    /// <summary>Lo mismo por cuenta, para que una sola cuenta no ocupe todos los cupos.</summary>
    public int MaxEsperandoPorCuenta { get; set; } = 10;
    /// <summary>Tope de contadores en memoria (ver <see cref="LoginThrottle"/> para qué se descarta primero).</summary>
    public int MaxEntradas { get; set; } = 10_000;

    public const string MensajePorIp = "Demasiados intentos desde esta conexión. Espera un minuto y vuelve a intentar.";
    public const string MensajePorCuenta =
        "Demasiados intentos fallidos con este correo. Espera unos minutos o pide al administrador que restablezca tu contraseña.";
}

public static class AuthRateLimitSetup
{
    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = config.GetSection(AuthRateLimitOptions.Section).Get<AuthRateLimitOptions>() ?? new AuthRateLimitOptions();
        var porIp = options.PorIpPorMinuto ?? (env.IsDevelopment() ? 1000 : 30);
        services.AddSingleton(options);
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<DeviceCookies>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AuthRateLimitOptions.PolicyName, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    LoginThrottle.IpKey(http.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = porIp, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.OnRejected = async (context, ct) =>
            {
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : TimeSpan.FromMinutes(1);
                context.HttpContext.Response.Headers.RetryAfter = LoginThrottle.Seconds(retry);
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = AuthRateLimitOptions.MensajePorIp }, ct);
            };
        });
        return services;
    }
}

/// <summary>
/// Cuenta las claves equivocadas por (cuenta, IP), por cuenta y por (cuenta, dispositivo). La cuenta se
/// busca en la base ANTES de llamar aquí y la clave es su id (<see cref="UserKey"/>, <see cref="PlatformKey"/>);
/// un correo que no existe usa un hash de tamaño fijo (<see cref="UnknownKey"/>), solo para frenar y
/// gastar el mismo tiempo.
/// El intento se reserva antes de verificar la clave (<see cref="Begin"/>: comparar e incrementar bajo un
/// mismo lock), así N peticiones simultáneas no verifican más del cupo; si la clave era correcta,
/// <see cref="Succeeded"/> devuelve la reserva.
/// Memoria acotada: las claves tienen tamaño fijo, un intento bloqueado no crea contadores, y por encima
/// de <see cref="AuthRateLimitOptions.MaxEntradas"/> se descartan, en este orden, los vencidos, los de
/// correos que no existen que NO están bloqueados y los de cuentas reales que no llegan a la mitad de su
/// umbral (los menos recientes primero). Un contador bloqueado, de cualquier tipo, no se descarta antes
/// de vencer (si se olvidara el de un correo inexistente y no el de una cuenta real, inundar la tabla
/// diría cuáles de los correos bloqueados existen). Tampoco uno de cuenta real por encima de la mitad.
/// Queda un oráculo con los contadores NO bloqueados (la inundación borra antes los pocos fallos de un
/// correo inexistente que los de una cuenta real): es una limitación conocida. Si todo lo que queda es
/// de ese tipo, la tabla pasa del tope (cada uno cuesta varios fallos y el cupo por IP los frena).
/// </summary>
public sealed class LoginThrottle
{
    private static readonly TimeSpan VentanaCuenta = TimeSpan.FromHours(1);

    private readonly AuthRateLimitOptions _options;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _esperandoPorCuenta = new(StringComparer.Ordinal);
    private int _esperando;

    public TimeSpan VentanaPar { get; }
    private int LimitePar => Math.Max(1, _options.FallosPorCorreoEIp);
    private int LimiteCuenta => Math.Max(1, _options.FallosPorCorreoPorHora);
    private int LimiteDispositivo => Math.Max(1, _options.FallosPorDispositivo);

    public LoginThrottle(AuthRateLimitOptions options, TimeProvider time)
    {
        _options = options;
        _time = time;
        VentanaPar = TimeSpan.FromMinutes(Math.Max(1, options.MinutosBloqueo));
    }

    private sealed class Counter
    {
        public int Count;
        public DateTimeOffset Start;
        public DateTimeOffset LastSeen;
        public TimeSpan Window;
        public int Limit;
        public bool CuentaReal;
    }

    /// <summary>Un intento reservado (o rechazado: <see cref="RetryAfter"/> con valor).</summary>
    public readonly record struct Attempt(string Cuenta, string PairKey, string? DeviceKey, int FallosPrevios, TimeSpan? RetryAfter)
    {
        public bool Blocked => RetryAfter is not null;
    }

    // ── Claves ──────────────────────────────────────────────────────
    public static string UserKey(Guid id) => "u:" + id.ToString("N");
    public static string PlatformKey(Guid id) => "p:" + id.ToString("N");

    /// <summary>Correo que no corresponde a ninguna cuenta: hash de tamaño fijo del correo normalizado.</summary>
    public static string UnknownKey(string? email) =>
        "x:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(email))), 0, 16);

    /// <summary>IPv4 tal cual; IPv6 por su /64 (una conexión doméstica o un servidor recibe una /64 entera).</summary>
    public static string IpKey(IPAddress? ip)
    {
        if (ip is null)
            return "sin-ip";
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    public static string Seconds(TimeSpan t) => ((int)Math.Ceiling(Math.Max(1, t.TotalSeconds))).ToString();

    // ── Intentos ────────────────────────────────────────────────────
    /// <summary>
    /// Reserva un intento de verificar la clave de <paramref name="cuenta"/> desde <paramref name="ip"/>.
    /// <paramref name="cuentaReal"/>: la cuenta existe. <paramref name="dispositivo"/>: el nonce de una
    /// cookie de dispositivo conocido válida de esa cuenta (DeviceCookies.Validate), o null.
    /// </summary>
    public Attempt Begin(string cuenta, IPAddress? ip, bool cuentaReal, string? dispositivo = null)
    {
        var pairKey = cuenta + "|" + IpKey(ip);
        var deviceKey = dispositivo is null ? null : "d|" + cuenta + "|" + dispositivo;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var par = Peek(pairKey, now);
            var total = Peek(cuenta, now);
            var disp = deviceKey is null ? null : Peek(deviceKey, now);

            if (par is not null && par.Count >= LimitePar)
                return Blocked(par, VentanaPar);
            if (deviceKey is not null)
            {
                // Dispositivo conocido: fuera del tope por cuenta, con su propio bloqueo corto
                if (disp is not null && disp.Count >= LimiteDispositivo)
                    return Blocked(disp, VentanaPar);
            }
            else if (total is not null && total.Count >= LimiteCuenta)
            {
                return Blocked(total, VentanaCuenta);
            }

            // Solo un intento que de verdad se va a verificar crea o suma contadores
            Bump(par ?? Create(pairKey, VentanaPar, LimitePar, cuentaReal, now), now);
            total = Bump(total ?? Create(cuenta, VentanaCuenta, LimiteCuenta, cuentaReal, now), now);
            if (deviceKey is not null)
                Bump(disp ?? Create(deviceKey, VentanaPar, LimiteDispositivo, cuentaReal, now), now);
            Trim(now);
            return new Attempt(cuenta, pairKey, deviceKey, total.Count - 1, null);

            Attempt Blocked(Counter c, TimeSpan ventana) =>
                new(cuenta, pairKey, deviceKey, total?.Count ?? 0, c.Start + ventana - now);
        }
    }

    /// <summary>La clave era correcta: devuelve la reserva de la cuenta y reinicia el par y el dispositivo.</summary>
    public void Succeeded(Attempt attempt)
    {
        if (attempt.Blocked)
            return;
        lock (_lock)
        {
            _counters.Remove(attempt.PairKey);
            if (attempt.DeviceKey is not null)
                _counters.Remove(attempt.DeviceKey);
            if (_counters.TryGetValue(attempt.Cuenta, out var total) && --total.Count <= 0)
                _counters.Remove(attempt.Cuenta);
        }
    }

    /// <summary>
    /// La clave era correcta pero los contadores son compartidos (correo repetido de antes de la regla):
    /// devuelve solo la reserva de este intento, sin reiniciar nada. Los fallos de otros siguen contando.
    /// </summary>
    public void Release(Attempt attempt)
    {
        if (attempt.Blocked)
            return;
        lock (_lock)
        {
            foreach (var key in new[] { attempt.PairKey, attempt.Cuenta, attempt.DeviceKey })
                if (key is not null && _counters.TryGetValue(key, out var counter) && --counter.Count <= 0)
                    _counters.Remove(key);
        }
    }

    /// <summary>La clave era equivocada: la reserva queda como fallo. Devuelve cuánto esperar antes de responder.</summary>
    public TimeSpan Failed(Attempt attempt)
    {
        if (attempt.Blocked || _options.RetardoBaseMs <= 0)
            return TimeSpan.Zero;
        var ms = _options.RetardoBaseMs * Math.Pow(2, Math.Min(attempt.FallosPrevios, 20));
        return TimeSpan.FromMilliseconds(Math.Min(ms, Math.Max(0, _options.RetardoMaximoMs)));
    }

    /// <summary>
    /// Registra el fallo y espera su retardo (se corta si el cliente se va). Devuelve false, sin esperar,
    /// si ya hay <see cref="AuthRateLimitOptions.MaxEsperando"/> peticiones esperando en total o
    /// <see cref="AuthRateLimitOptions.MaxEsperandoPorCuenta"/> de esa cuenta: el llamador responde 429.
    /// </summary>
    public async Task<bool> FailAndDelayAsync(Attempt attempt, CancellationToken ct)
    {
        var retardo = Failed(attempt);
        if (retardo <= TimeSpan.Zero)
            return true;
        lock (_lock)
        {
            _esperandoPorCuenta.TryGetValue(attempt.Cuenta, out var deLaCuenta);
            if (_esperando >= Math.Max(1, _options.MaxEsperando) || deLaCuenta >= Math.Max(1, _options.MaxEsperandoPorCuenta))
                return false;
            _esperando++;
            _esperandoPorCuenta[attempt.Cuenta] = deLaCuenta + 1;
        }
        try
        {
            await Task.Delay(retardo, _time, ct);
        }
        catch (OperationCanceledException)
        {
            // El cliente se fue: no hay a quién responder
        }
        finally
        {
            lock (_lock)
            {
                _esperando--;
                if (--_esperandoPorCuenta[attempt.Cuenta] <= 0)
                    _esperandoPorCuenta.Remove(attempt.Cuenta);
            }
        }
        return true;
    }

    public int Esperando
    {
        get { lock (_lock) return _esperando; }
    }

    /// <summary>Clave restablecida o cambiada: la cuenta empieza de cero en todas las IPs y dispositivos.</summary>
    public void Reset(string cuenta)
    {
        var porIp = cuenta + "|";
        var porDispositivo = "d|" + cuenta + "|";
        lock (_lock)
        {
            _counters.Remove(cuenta);
            foreach (var key in _counters.Keys.Where(k => k.StartsWith(porIp, StringComparison.Ordinal) || k.StartsWith(porDispositivo, StringComparison.Ordinal)).ToList())
                _counters.Remove(key);
        }
    }

    /// <summary>
    /// Normalización para la clave de un correo que no existe: NFKC, sin caracteres invisibles de formato
    /// (categoría Cf: U+200B/U+200D, U+00AD, U+FEFF, U+2060…) ni de control, minúsculas y "ß" → "ss".
    /// Acerca la clave a lo que la collation de SQL Server considera igual, para que las variantes de un
    /// correo inexistente no sirvan de intentos nuevos. Queda abierto: otras equivalencias propias de la
    /// collation (p. ej. ligaduras que NFKC no descompone) pueden dar claves distintas, lo que solo afecta
    /// a correos SIN cuenta (las cuentas reales se cuentan por id).
    /// </summary>
    public static string NormalizeEmail(string? email)
    {
        var value = email ?? string.Empty;
        try
        {
            value = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Cadena Unicode inválida (p. ej. un sustituto suelto): se usa tal cual
        }
        var limpio = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            var categoria = char.GetUnicodeCategory(c);
            if (categoria is UnicodeCategory.Format or UnicodeCategory.Control)
                continue;
            limpio.Append(c);
        }
        return limpio.ToString().Trim().ToLowerInvariant().Replace("ß", "ss");
    }

    public int Entradas
    {
        get { lock (_lock) return _counters.Count; }
    }

    // ── Internos (bajo _lock) ───────────────────────────────────────
    /// <summary>El contador vigente de la clave, o null (no existe o ya venció; uno vencido se quita).</summary>
    private Counter? Peek(string key, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(key, out var counter))
            return null;
        if (now - counter.Start < counter.Window)
            return counter;
        _counters.Remove(key);
        return null;
    }

    private Counter Create(string key, TimeSpan window, int limit, bool cuentaReal, DateTimeOffset now)
    {
        var counter = new Counter { Start = now, LastSeen = now, Window = window, Limit = limit, CuentaReal = cuentaReal };
        _counters[key] = counter;
        return counter;
    }

    private static Counter Bump(Counter counter, DateTimeOffset now)
    {
        counter.Count++;
        counter.LastSeen = now;
        return counter;
    }

    private void Trim(DateTimeOffset now)
    {
        var max = Math.Max(10, _options.MaxEntradas);
        if (_counters.Count <= max)
            return;
        var objetivo = max * 9 / 10;

        Remove(_counters.Where(kv => now - kv.Value.Start >= kv.Value.Window).Select(kv => kv.Key));
        if (_counters.Count <= objetivo)
            return;
        Remove(_counters.Where(kv => !kv.Value.CuentaReal && kv.Value.Count < kv.Value.Limit)
            .OrderBy(kv => kv.Value.LastSeen).Take(_counters.Count - objetivo).Select(kv => kv.Key));
        if (_counters.Count <= objetivo)
            return;
        Remove(_counters.Where(kv => kv.Value.CuentaReal && kv.Value.Count * 2 < kv.Value.Limit)
            .OrderBy(kv => kv.Value.LastSeen).Take(_counters.Count - objetivo).Select(kv => kv.Key));
        // Lo que queda son contadores vigentes bloqueados o de cuentas reales con muchos fallos: no se sueltan
    }

    private void Remove(IEnumerable<string> keys)
    {
        foreach (var key in keys.ToList())
            _counters.Remove(key);
    }
}
