using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Platform;

namespace NemediClinic.Api.Middleware;

/// <summary>
/// Resuelve el branding por el dominio del frontend y lo deja en HttpContext.Items.
///   · Web y API en el mismo origen (Docker + Caddy): el Host de la request, que detrás de Caddy
///     llega por X-Forwarded-Host (UseForwardedHeaders lo aplica solo si viene de un proxy declarado en ForwardedHeaders).
///   · API en su propio subdominio (IIS: clinic.* llama a api.*): el Host es el del API, así que
///     manda la cabecera Origin que el navegador pone en toda llamada de otro origen — solo si es
///     un origen permitido por CORS, que es la lista de frontends que el API reconoce.
/// Solo en Development se acepta además ?host= / X-Branding-Host para probar sin tocar el archivo hosts.
/// </summary>
public class BrandingMiddleware
{
    public const string ItemKey = "Branding";
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public BrandingMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context, BrandingResolver resolver, AllowedOrigins origins)
    {
        // Solo lo consume el endpoint público; evita una consulta en el resto de requests.
        if (context.Request.Path.StartsWithSegments("/api/v1/branding"))
        {
            var host = context.Request.Host.Host;
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (origins.IsAllowed(origin))
                host = new Uri(origin!).Host;
            if (_env.IsDevelopment())
            {
                var overrideHost = context.Request.Query["host"].FirstOrDefault()
                    ?? context.Request.Headers["X-Branding-Host"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(overrideHost))
                    host = overrideHost;
            }
            context.Items[ItemKey] = await resolver.ResolveAsync(host);
        }

        await _next(context);
    }
}

public static class BrandingHttpContextExtensions
{
    public static BrandingDto? GetBranding(this HttpContext context) =>
        context.Items.TryGetValue(BrandingMiddleware.ItemKey, out var value) ? value as BrandingDto : null;
}
