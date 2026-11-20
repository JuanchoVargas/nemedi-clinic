using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NemediClinic.Api.Middleware;

/// <summary>
/// Endpoint que solo existe en Development: en cualquier otro entorno responde 404 sin cuerpo.
/// Es un filtro de recurso para cortar ANTES del model binding: con un chequeo dentro de la acción,
/// un cuerpo inválido respondía 400 en producción (la validación de [ApiController] corre primero)
/// y delataba que la ruta existe.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!env.IsDevelopment())
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
