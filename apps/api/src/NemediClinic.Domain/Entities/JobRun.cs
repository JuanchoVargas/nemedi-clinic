using NemediClinic.Domain.Enums;

namespace NemediClinic.Domain.Entities;

/// <summary>
/// Una ejecución de un job recurrente (vencimiento de paquetes, alertas de lotes, purga de adjuntos…).
/// Es de sistema: sin TenantId, un job recorre todas las clínicas. Sirve de registro (visible en
/// /platform) y de candado: un índice único filtrado sobre Nombre con Estado = EnCurso impide que dos
/// ejecuciones del mismo job corran a la vez, aunque vengan de procesos distintos.
/// </summary>
public class JobRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Hora local de Bogotá, como el resto del sistema.</summary>
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public long? DuracionMs { get; set; }
    public JobRunEstado Estado { get; set; } = JobRunEstado.EnCurso;
    /// <summary>Resumen de lo que hizo, o el error si falló.</summary>
    public string? Resultado { get; set; }
    /// <summary>Quién lo disparó: Programador (Hangfire), Endpoint (tarea programada) o Plataforma.</summary>
    public string Origen { get; set; } = string.Empty;
}
