namespace NemediClinic.Api.Services;

/// <summary>
/// Orígenes del frontend permitidos (CORS_ORIGINS, separados por coma). Es la única lista: la usan
/// CORS, el manifest de la PWA y el branding por dominio. En Development, sin CORS_ORIGINS, se
/// permiten los puertos locales de Vite.
/// </summary>
public class AllowedOrigins
{
    private static readonly string[] DevDefaults = ["http://localhost:3000", "http://localhost:5173"];

    public AllowedOrigins(IConfiguration config, IHostEnvironment env)
    {
        var configured = (config["CORS_ORIGINS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.TrimEnd('/'))
            .ToArray();
        All = configured.Length == 0 && env.IsDevelopment() ? DevDefaults : configured;
    }

    public IReadOnlyList<string> All { get; }

    public bool IsAllowed(string? origin) =>
        !string.IsNullOrWhiteSpace(origin)
        && Uri.TryCreate(origin, UriKind.Absolute, out _)
        && All.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Web y API en el mismo origen (IIS: clinic.example.com y clinic.example.com/api; Docker: detrás
    /// de Caddy) no necesitan CORS: la lista vacía es lo normal y deja afuera a cualquier otro sitio.
    /// Si se configura (un frontend en otro dominio), no admite comodines ni rutas.
    /// </summary>
    public void EnsureValidForProduction()
    {
        if (All.Any(o => o.Contains('*')))
            throw new InvalidOperationException("CORS_ORIGINS no admite comodines en Production: lista los dominios exactos.");
        if (All.Any(o => !Uri.TryCreate(o, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/"))
            throw new InvalidOperationException("CORS_ORIGINS debe tener orígenes completos, sin ruta: https://clinic.example.com");
    }
}
