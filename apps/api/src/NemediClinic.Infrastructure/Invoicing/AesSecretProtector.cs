using System.Security.Cryptography;
using System.Text;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Invoicing;

/// <summary>
/// AES-256-GCM. La clave sale de un secreto de configuración (Secrets:EncryptionKey, o Jwt:Secret si
/// no hay otra) pasado por HKDF con una etiqueta propia: el mismo secreto no da la misma clave que la
/// firma de los JWT. Formato guardado: "v1:" + base64(nonce[12] | tag[16] | cifrado).
/// Cambiar el secreto deja ilegibles las credenciales guardadas: habría que volver a escribirlas.
/// </summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const string Prefix = "v1:";
    private readonly byte[] _key;

    public AesSecretProtector(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            throw new InvalidOperationException("El secreto para cifrar credenciales debe tener al menos 16 caracteres.");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32,
            info: Encoding.UTF8.GetBytes("NemediClinic.Secrets.v1"));
    }

    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Encrypt(nonce, data, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText)
    {
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Formato de secreto desconocido.");
        var raw = Convert.FromBase64String(protectedText[Prefix.Length..]);
        if (raw.Length < 28) throw new CryptographicException("Secreto incompleto.");
        var nonce = raw.AsSpan(0, 12);
        var tag = raw.AsSpan(12, 16);
        var cipher = raw.AsSpan(28);
        var data = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, 16))
            aes.Decrypt(nonce, cipher, tag, data);
        return Encoding.UTF8.GetString(data);
    }
}
