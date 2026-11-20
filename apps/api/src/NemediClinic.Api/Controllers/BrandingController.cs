using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Middleware;
using NemediClinic.Api.Services;
using NemediClinic.Application.DTOs.Platform;

namespace NemediClinic.Api.Controllers;

[ApiController]
[Route("api/v1/branding")]
[AllowAnonymous]
public class BrandingController : ControllerBase
{
    private readonly BrandingResolver _resolver;
    private readonly AllowedOrigins _origins;

    public BrandingController(BrandingResolver resolver, AllowedOrigins origins)
    {
        _resolver = resolver;
        _origins = origins;
    }

    /// <summary>Branding del canal cuyo dominio coincide con el Host. Fallback: Nemedi.</summary>
    [HttpGet]
    public async Task<ActionResult<BrandingDto>> Get()
    {
        var branding = HttpContext.GetBranding() ?? await _resolver.ResolveAsync(Request.Host.Host);
        return Ok(branding);
    }

    /// <summary>
    /// Manifest de la PWA con el nombre y el color del canal: la app instalada se llama como la marca
    /// del dominio por el que se entró. Con web y API en el mismo origen (Docker) las rutas son
    /// relativas; cuando el API vive en otro origen (Vite en desarrollo, api.* en IIS) la web manda
    /// ?origin= y, si es un origen permitido por CORS, start_url e iconos se resuelven contra él.
    /// La web trae uno estático de respaldo con los mismos campos (apps/web/vite.config.ts), para que
    /// la app se pueda instalar aunque el API no responda: si cambias uno, cambia el otro.
    /// </summary>
    [HttpGet("manifest.webmanifest")]
    public async Task<IActionResult> Manifest([FromQuery] string? origin)
    {
        var branding = HttpContext.GetBranding() ?? await _resolver.ResolveAsync(Request.Host.Host);
        var baseUrl = _origins.IsAllowed(origin) ? origin!.TrimEnd('/') : string.Empty;

        var manifest = new
        {
            name = branding.NombreComercial,
            // Lo que se ve bajo el ícono (~12 caracteres): "Nemedi Clinic" → "NemediClinic"; si no cabe,
            // la primera palabra. Misma regla que apps/web/src/lib/app-short-name.ts y vite.config.ts.
            short_name = ShortName(branding.NombreComercial),
            description = "Agenda, pacientes e historia clínica de la clínica.",
            lang = "es",
            start_url = baseUrl + "/dashboard",
            scope = baseUrl + "/",
            display = "standalone",
            orientation = "any",
            theme_color = branding.ColorPrimario,
            background_color = "#F7F6F3",
            icons = new object[]
            {
                new { src = baseUrl + "/icons/icon-192.png", sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = baseUrl + "/icons/icon-512.png", sizes = "512x512", type = "image/png", purpose = "any" },
                new { src = baseUrl + "/icons/icon-maskable-512.png", sizes = "512x512", type = "image/png", purpose = "maskable" },
            },
        };

        Response.Headers.CacheControl = "public, max-age=3600";
        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }

    public static string ShortName(string nombre)
    {
        var compacto = string.Concat(nombre.Where(c => !char.IsWhiteSpace(c)));
        return compacto.Length <= 12 ? compacto : nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}
