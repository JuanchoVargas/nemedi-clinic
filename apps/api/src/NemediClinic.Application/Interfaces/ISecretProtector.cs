namespace NemediClinic.Application.Interfaces;

/// <summary>
/// Cifra datos sensibles que se guardan en la base (credenciales de proveedores de cada clínica).
/// Implementación: AesSecretProtector (AES-GCM, clave de Secrets:EncryptionKey o derivada de Jwt:Secret).
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Lanza si el texto fue alterado o se cifró con otra clave.</summary>
    string Unprotect(string protectedText);
}
