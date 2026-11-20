using System.Security.Cryptography;
using System.Text;
using NemediClinic.Infrastructure.Services;

namespace NemediClinic.Api.Services;

/// <summary>
/// Cookie de dispositivo conocido (patrón "device cookies" de OWASP). Tras un ingreso correcto de una
/// cuenta activa y sin clave temporal, el API entrega <see cref="Name"/> (HttpOnly, Secure,
/// SameSite=Strict, solo para /api/v1/auth) con {cuenta, nonce, vencimiento} firmados con una subllave
/// HKDF de Jwt:Secret. La firma incluye el sello de la clave (PasswordStamp): cambiar o restablecer la
/// clave invalida todas las cookies de la cuenta. Sin estado en el servidor (sobrevive a reinicios y
/// reciclados de IIS) y no se puede fabricar sin la llave.
/// Un intento que trae una cookie válida de ESA cuenta no cae en el tope por cuenta (LoginThrottle): es
/// el dispositivo donde el dueño ya entró. Tiene su propio bloqueo corto por (cuenta, nonce).
/// </summary>
public sealed class DeviceCookies
{
    public const string Name = "nemedi_device";
    public const string CookiePath = "/api/v1/auth";
    private const int MaxLength = 512;

    private readonly byte[] _subllave;
    private readonly TimeSpan _vigencia;
    private readonly TimeProvider _time;
    private readonly string _secret;

    public DeviceCookies(IConfiguration config, AuthRateLimitOptions options, TimeProvider time)
    {
        _secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret no está configurado.");
        _subllave = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(_secret), 32,
            salt: [], info: Encoding.UTF8.GetBytes("NemediClinic.DeviceCookie.v1"));
        _vigencia = TimeSpan.FromDays(Math.Max(1, options.DiasDispositivo));
        _time = time;
    }

    /// <summary>Valor nuevo para la cuenta (nonce aleatorio).</summary>
    public string Issue(string cuenta, string passwordHash, out DateTimeOffset expires)
    {
        expires = _time.GetUtcNow() + _vigencia;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"v1|{cuenta}|{nonce}|{expires.ToUnixTimeSeconds()}";
        return B64(Encoding.UTF8.GetBytes(payload)) + "." + B64(Mac(payload, passwordHash));
    }

    /// <summary>El nonce si la cookie es de <paramref name="cuenta"/>, no venció y su firma vale con la clave actual.</summary>
    public string? Validate(string? cookie, string cuenta, string passwordHash)
    {
        if (string.IsNullOrEmpty(cookie) || cookie.Length > MaxLength)
            return null;
        var partes = cookie.Split('.');
        if (partes.Length != 2)
            return null;
        string payload;
        byte[] firma;
        try
        {
            payload = Encoding.UTF8.GetString(FromB64(partes[0]));
            firma = FromB64(partes[1]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(firma, Mac(payload, passwordHash)))
            return null;

        var campos = payload.Split('|');
        if (campos.Length != 4 || campos[0] != "v1" || campos[1] != cuenta || !long.TryParse(campos[3], out var exp))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(exp) > _time.GetUtcNow() ? campos[2] : null;
    }

    public CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private byte[] Mac(string payload, string passwordHash) =>
        HMACSHA256.HashData(_subllave, Encoding.UTF8.GetBytes(payload + "|" + PasswordStamp.Compute(passwordHash, _secret)));

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
