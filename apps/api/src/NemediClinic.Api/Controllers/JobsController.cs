using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services.Jobs;
using NemediClinic.Application.DTOs.Platform;

namespace NemediClinic.Api.Controllers;

/// <summary>
/// Disparo externo de los jobs recurrentes: en IIS el app pool se recicla y un programador dentro
/// del proceso no es confiable, así que una tarea programada de Plesk llama aquí.
///
/// POST /api/v1/jobs/{nombre} con la cabecera X-Jobs-Key = Jobs:ApiKey. Sin clave configurada el
/// endpoint no existe (404); con clave equivocada, 401 (la clave se compara en tiempo constante y
/// antes de mirar el nombre, para no revelar qué jobs hay). 200 con la ejecución, 409 si ya está
/// corriendo, 500 si el job falló (queda registrado igual), para que la tarea lo marque como error.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[AllowAnonymous]
public class JobsController : ControllerBase
{
    public const string KeyHeader = "X-Jobs-Key";

    private readonly JobRunner _runner;
    private readonly IConfiguration _config;

    public JobsController(JobRunner runner, IConfiguration config)
    {
        _runner = runner;
        _config = config;
    }

    [HttpPost("{nombre}")]
    public async Task<ActionResult<JobRunDto>> Run(string nombre)
    {
        var expected = _config["Jobs:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return NotFound();

        var given = Request.Headers[KeyHeader].FirstOrDefault() ?? string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
            return Unauthorized(new { error = "Clave de jobs inválida." });

        // Sin el token de la request: si la tarea programada corta la conexión, el job termina igual
        var run = await _runner.RunAsync(nombre, JobRunner.OrigenEndpoint, CancellationToken.None);
        return run.Estado == "Fallida"
            ? StatusCode(StatusCodes.Status500InternalServerError, new { error = $"El job {run.Nombre} falló: {run.Resultado}", ejecucion = run })
            : Ok(run);
    }
}
