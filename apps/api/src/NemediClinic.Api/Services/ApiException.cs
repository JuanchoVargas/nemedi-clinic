using Microsoft.AspNetCore.Mvc;
using NemediClinic.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NemediClinic.Api.Services;

/// <summary>
/// Error de negocio lanzado desde un service. El filtro lo traduce al contrato de error
/// del API ({ error }), así el controller queda delgado: recibe, llama al service y responde.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);

    /// <summary>Cuándo reintentar (cabecera Retry-After, en segundos); solo para 429.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static ApiException TooManyRequests(string message, TimeSpan retryAfter) =>
        new(StatusCodes.Status429TooManyRequests, message) { RetryAfter = retryAfter };
}

public class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    /// <summary>Lo que ve la persona cuando el servidor no puede guardar el archivo (el motivo técnico va al log).</summary>
    public const string StorageMessage =
        "El servidor no pudo guardar el archivo porque su carpeta de archivos no permite escribir. " +
        "No es un problema de tu imagen: avísale al administrador del sistema.";

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ApiException ex:
                if (ex.RetryAfter is { } retry)
                    context.HttpContext.Response.Headers.RetryAfter = LoginThrottle.Seconds(retry);
                context.Result = new ObjectResult(new { error = ex.Message }) { StatusCode = ex.StatusCode };
                context.ExceptionHandled = true;
                break;
            // Permisos o disco: 503 (el servicio de archivos no está disponible), no un 500 genérico
            case StorageUnavailableException ex:
                logger.LogError(ex, "No se pudo guardar un archivo en {Path}: {Reason}", context.HttpContext.Request.Path, ex.Reason);
                context.Result = new ObjectResult(new { error = StorageMessage, code = "storage_unavailable" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
                context.ExceptionHandled = true;
                break;
        }
    }
}
