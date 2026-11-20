using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Invoicing;

/// <summary>
/// Tokens OAuth de Factus en memoria (singleton), uno por cuenta: clínica + ambiente + client_id +
/// usuario (hasheados: la clave del diccionario nunca lleva el secreto). El token dura 3600 s; se
/// considera vencido <see cref="Margen"/> antes, así una petición no sale con un token a punto de caer.
/// Un semáforo por cuenta evita que diez facturas simultáneas pidan diez tokens.
/// </summary>
public sealed class FactusTokenCache(IClock clock)
{
    public static readonly TimeSpan Margen = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public sealed record Entry(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc);

    public static string Key(InvoicingConnection c)
    {
        var raw = $"{c.TenantId}|{c.Produccion}|{c.ClientId}|{c.Usuario}|{c.Contrasena}|{c.ClientSecret}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public SemaphoreSlim Lock(string key) => _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    /// <summary>Token vigente (con margen) o null.</summary>
    public string? Valid(string key) =>
        _entries.TryGetValue(key, out var e) && e.ExpiresAtUtc - Margen > clock.UtcNow ? e.AccessToken : null;

    /// <summary>Refresh token de la última entrada, aunque el access token ya haya vencido.</summary>
    public string? RefreshToken(string key) => _entries.TryGetValue(key, out var e) ? e.RefreshToken : null;

    public void Store(string key, string accessToken, string? refreshToken, int expiresInSeconds) =>
        _entries[key] = new Entry(accessToken, refreshToken, clock.UtcNow.AddSeconds(Math.Max(expiresInSeconds, 60)));

    public void Invalidate(string key) => _entries.TryRemove(key, out _);
}
