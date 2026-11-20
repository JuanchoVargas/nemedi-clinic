namespace NemediClinic.Application.DTOs.Platform;

/// <summary>Una ejecución de un job (POST /jobs/{nombre} y GET /platform/jobs).</summary>
public class JobRunDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public long? DuracionMs { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Resultado { get; set; }
    public string Origen { get; set; } = string.Empty;
}

/// <summary>Un job registrado con su última ejecución.</summary>
public class JobStatusDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    /// <summary>Con qué frecuencia conviene programarlo (texto para la tarea de Plesk).</summary>
    public string FrecuenciaSugerida { get; set; } = string.Empty;
    public JobRunDto? UltimaEjecucion { get; set; }
    public JobRunDto? UltimaExitosa { get; set; }
}

public class JobsOverviewDto
{
    /// <summary>Hangfire corre dentro del API (Jobs:SchedulerEnabled). En IIS va apagado.</summary>
    public bool ProgramadorActivo { get; set; }
    /// <summary>POST /api/v1/jobs/{nombre} habilitado (hay Jobs:ApiKey configurada).</summary>
    public bool EndpointHabilitado { get; set; }
    public List<JobStatusDto> Jobs { get; set; } = [];
    /// <summary>Últimas ejecuciones de todos los jobs, la más reciente primero.</summary>
    public List<JobRunDto> Historial { get; set; } = [];
}
