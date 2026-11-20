using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.DTOs.Platform;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Services.Jobs;

/// <summary>
/// Ejecuta los jobs recurrentes con candado y registro, venga la orden de donde venga:
/// Hangfire (desarrollo / servidor propio), la tarea programada de Plesk (POST /api/v1/jobs/{nombre})
/// o el botón de /platform.
///
/// Candado: antes de correr se inserta una fila JobRun EnCurso. El índice único filtrado
/// IX_JobRuns_Nombre_EnCurso hace que la segunda inserción simultánea falle → 409, aunque las dos
/// vengan de procesos distintos (IIS solapa el proceso viejo y el nuevo al reciclar). Si el proceso
/// muere a mitad, la fila queda EnCurso: pasado <see cref="StaleAfter"/> la siguiente corrida la
/// cierra como Abandonada y sigue.
/// </summary>
public class JobRunner
{
    private readonly IClock _clock;
    public const string OrigenProgramador = "Programador";
    public const string OrigenEndpoint = "Endpoint";
    public const string OrigenPlataforma = "Plataforma";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IEnumerable<IScheduledJob> _jobs;
    private readonly IConfiguration _config;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(AppDbContext db, IEnumerable<IScheduledJob> jobs, IConfiguration config, ILogger<JobRunner> logger, IClock clock)
    {
        _clock = clock;
        _db = db;
        _jobs = jobs;
        _config = config;
        _logger = logger;
    }

    public IReadOnlyList<IScheduledJob> Jobs => _jobs.ToList();

    /// <summary>Corre el job. 404 si no existe, 409 si ya está corriendo. Un fallo del job queda registrado (Estado Fallida), no lanza.</summary>
    public async Task<JobRunDto> RunAsync(string nombre, string origen, CancellationToken ct = default)
    {
        var job = _jobs.FirstOrDefault(j => string.Equals(j.Nombre, nombre, StringComparison.OrdinalIgnoreCase))
            ?? throw ApiException.NotFound($"No existe el job '{nombre}'.");

        // Una corrida que quedó colgada (el proceso se reinició a mitad) no bloquea para siempre
        var colgadaDesde = _clock.Now - StaleAfter;
        await _db.JobRuns
            .Where(r => r.Nombre == job.Nombre && r.Estado == JobRunEstado.EnCurso && r.Inicio < colgadaDesde)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Estado, JobRunEstado.Abandonada)
                .SetProperty(r => r.Fin, _clock.Now)
                .SetProperty(r => r.Resultado, "Quedó sin terminar: el proceso se reinició durante la ejecución."), ct);

        var run = new JobRun { Nombre = job.Nombre, Inicio = _clock.Now, Estado = JobRunEstado.EnCurso, Origen = origen };
        _db.JobRuns.Add(run);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _db.ChangeTracker.Clear();
            var enCurso = await _db.JobRuns.AsNoTracking()
                .Where(r => r.Nombre == job.Nombre && r.Estado == JobRunEstado.EnCurso)
                .Select(r => (DateTime?)r.Inicio).FirstOrDefaultAsync(ct);
            throw ApiException.Conflict(
                $"El job '{job.Nombre}' ya se está ejecutando{(enCurso is { } inicio ? $" (empezó a las {inicio:HH:mm:ss})" : string.Empty)}.");
        }

        var reloj = Stopwatch.StartNew();
        try
        {
            run.Resultado = Truncar(await job.RunAsync(ct));
            run.Estado = JobRunEstado.Exitosa;
            _logger.LogInformation("Job {Job} ({Origen}) terminó en {Ms} ms: {Resultado}", job.Nombre, origen, reloj.ElapsedMilliseconds, run.Resultado);
        }
        catch (Exception ex)
        {
            run.Resultado = Truncar(ex.GetBaseException().Message);
            run.Estado = JobRunEstado.Fallida;
            _logger.LogError(ex, "Job {Job} ({Origen}) falló", job.Nombre, origen);
        }

        run.Fin = _clock.Now;
        run.DuracionMs = reloj.ElapsedMilliseconds;
        // Si el job falló a mitad, el contexto puede tener cambios a medias: se descartan y solo se
        // guarda el cierre de la ejecución (que además libera el candado).
        _db.ChangeTracker.Clear();
        _db.JobRuns.Attach(run);
        _db.Entry(run).State = EntityState.Modified;
        await _db.SaveChangesAsync(CancellationToken.None);
        return ToDto(run);
    }

    /// <summary>Entrada de Hangfire: un choque con otra ejecución no es un error que reintentar.</summary>
    public async Task RunScheduledAsync(string nombre)
    {
        try
        {
            await RunAsync(nombre, OrigenProgramador);
        }
        catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status409Conflict)
        {
            _logger.LogInformation("Job {Job} omitido: {Message}", nombre, ex.Message);
        }
    }

    public async Task<JobsOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var nombres = _jobs.Select(j => j.Nombre).ToList();
        var historial = await _db.JobRuns.AsNoTracking()
            .OrderByDescending(r => r.Inicio).Take(30).ToListAsync(ct);
        // La última de cada job puede ser más vieja que las 30 del historial
        var ultimas = await _db.JobRuns.AsNoTracking()
            .Where(r => nombres.Contains(r.Nombre))
            .GroupBy(r => r.Nombre)
            .Select(g => g.OrderByDescending(r => r.Inicio).First())
            .ToListAsync(ct);
        var ultimasExitosas = await _db.JobRuns.AsNoTracking()
            .Where(r => nombres.Contains(r.Nombre) && r.Estado == JobRunEstado.Exitosa)
            .GroupBy(r => r.Nombre)
            .Select(g => g.OrderByDescending(r => r.Inicio).First())
            .ToListAsync(ct);

        return new JobsOverviewDto
        {
            ProgramadorActivo = SchedulerEnabled(_config),
            EndpointHabilitado = !string.IsNullOrWhiteSpace(_config["Jobs:ApiKey"]),
            Jobs = _jobs.Select(j => new JobStatusDto
            {
                Nombre = j.Nombre,
                Descripcion = j.Descripcion,
                FrecuenciaSugerida = j.FrecuenciaSugerida,
                UltimaEjecucion = ultimas.FirstOrDefault(r => r.Nombre == j.Nombre) is { } u ? ToDto(u) : null,
                UltimaExitosa = ultimasExitosas.FirstOrDefault(r => r.Nombre == j.Nombre) is { } e ? ToDto(e) : null,
            }).ToList(),
            Historial = historial.Select(ToDto).ToList(),
        };
    }

    /// <summary>Hangfire dentro del API. Encendido por defecto (desarrollo, Docker); en IIS se apaga.</summary>
    public static bool SchedulerEnabled(IConfiguration config) => config.GetValue("Jobs:SchedulerEnabled", true);

    private static string? Truncar(string? texto) => texto is { Length: > 2000 } ? texto[..2000] : texto;

    private static JobRunDto ToDto(JobRun r) => new()
    {
        Id = r.Id,
        Nombre = r.Nombre,
        Inicio = r.Inicio,
        Fin = r.Fin,
        DuracionMs = r.DuracionMs,
        Estado = r.Estado.ToString(),
        Resultado = r.Resultado,
        Origen = r.Origen,
    };
}
