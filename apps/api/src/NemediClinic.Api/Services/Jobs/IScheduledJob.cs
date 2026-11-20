namespace NemediClinic.Api.Services.Jobs;

/// <summary>
/// Un job recurrente. Para agregar uno (p. ej. los recordatorios de WhatsApp): implementar esta
/// interfaz y registrarlo en Program.cs con AddScoped&lt;IScheduledJob, MiJob&gt;(). Con eso ya tiene
/// endpoint (POST /api/v1/jobs/{Nombre}), candado, registro en /platform y, en desarrollo, su
/// entrada en Hangfire con <see cref="CronSugerido"/>.
///
/// Reglas para quien lo implemente:
///   · Idempotente: correrlo dos veces seguidas no hace daño ni duplica efectos.
///   · Sin contexto HTTP: no hay tenant; recorre todas las clínicas con IgnoreQueryFilters y
///     filtra IsDeleted a mano.
///   · Corto: la tarea programada espera la respuesta HTTP (unos minutos como mucho). Si crece,
///     que procese por lotes y deje el resto para la siguiente corrida.
/// </summary>
public interface IScheduledJob
{
    /// <summary>kebab-case; es el segmento de la URL y la clave del registro.</summary>
    string Nombre { get; }
    string Descripcion { get; }
    /// <summary>Cron para Hangfire (hora local) cuando el programador interno está activo.</summary>
    string CronSugerido { get; }
    /// <summary>La misma frecuencia en palabras, para configurar la tarea en Plesk.</summary>
    string FrecuenciaSugerida { get; }

    /// <summary>Hace el trabajo y devuelve un resumen legible de lo que hizo.</summary>
    Task<string> RunAsync(CancellationToken ct);
}
