using System.Security.Cryptography;
using System.Text;

namespace NemediClinic.Infrastructure.Services;

/// <summary>
/// Sello de la clave que viaja en el JWT (claim <see cref="ClaimName"/>): un HMAC del hash BCrypt actual
/// con una subllave derivada (HKDF) de Jwt:Secret. Cambiar o restablecer la clave cambia el hash, así que los JWT emitidos antes
/// dejan de coincidir y SessionValidator los rechaza. No hace falta columna nueva (es la idea del
/// SecurityStamp de ASP.NET Identity) y el token no revela nada del hash: sin la llave no se recalcula.
/// </summary>
public static class PasswordStamp
{
    public const string ClaimName = "pwd_stamp";

    public static string Compute(string passwordHash, string secret)
    {
        // Subllave propia (HKDF con etiqueta) en vez de la llave de los JWT: la misma llave no se usa
        // para dos cosas distintas
        var subllave = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32,
            salt: [], info: Encoding.UTF8.GetBytes("NemediClinic.PasswordStamp.v1"));
        var mac = HMACSHA256.HashData(subllave, Encoding.UTF8.GetBytes(passwordHash));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Comparación en tiempo constante.</summary>
    public static bool Matches(string? claim, string expected) =>
        claim is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(claim), Encoding.UTF8.GetBytes(expected));
}
