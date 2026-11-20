using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NemediClinic.Api.Services.Jobs;
using NemediClinic.Application.DTOs.Platform;

namespace NemediClinic.Api.Controllers.Platform;

/// <summary>Registro de los jobs recurrentes (última corrida, duración, resultado) y ejecución manual.</summary>
[ApiController]
[Route("api/v1/platform/jobs")]
[Authorize(Policy = "PlatformAdmin")]
public class PlatformJobsController : ControllerBase
{
    private readonly JobRunner _runner;

    public PlatformJobsController(JobRunner runner) => _runner = runner;

    [HttpGet]
    public async Task<ActionResult<JobsOverviewDto>> Get(CancellationToken ct) => Ok(await _runner.OverviewAsync(ct));

    /// <summary>Ejecutar ahora. Mismo candado que el programador y el endpoint externo.</summary>
    [HttpPost("{nombre}/run")]
    public async Task<ActionResult<JobRunDto>> Run(string nombre) =>
        Ok(await _runner.RunAsync(nombre, JobRunner.OrigenPlataforma, CancellationToken.None));
}
