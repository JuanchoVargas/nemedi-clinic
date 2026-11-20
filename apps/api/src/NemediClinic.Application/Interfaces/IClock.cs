namespace NemediClinic.Application.Interfaces;

/// <summary>
/// La hora del sistema: la de la clínica, nunca la del servidor.
///
/// Todo DateTime de negocio (citas, pagos, cajas, vencimientos, notas) es hora de pared de la
/// clínica sin zona (Kind = Unspecified), y la API la entrega y la recibe así, sin sufijo. La zona
/// sale de Clock:TimeZone (Bogotá por defecto). El servidor puede estar en cualquier zona, con o sin
/// horario de verano: nada lee su hora local (DateTime.Now está prohibido por el analizador del build).
///
/// Los campos de auditoría (CreatedAt, UpdatedAt, PurgedAt, vencimientos de tokens) son UTC.
/// </summary>
public interface IClock
{
    /// <summary>Zona de la clínica (Clock:TimeZone). Bogotá: UTC−5 todo el año.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Instante actual en UTC (Kind = Utc). Para auditoría y expiraciones.</summary>
    DateTime UtcNow { get; }

    /// <summary>Hora actual de la clínica (Kind = Unspecified). Para todo dato de negocio.</summary>
    DateTime Now { get; }

    /// <summary>Fecha actual de la clínica.</summary>
    DateOnly Today { get; }

    /// <summary>
    /// Un instante UTC → hora de la clínica. El valor se toma como UTC aunque venga con Kind
    /// Unspecified (EF devuelve así lo que se guardó con UtcNow).
    /// </summary>
    DateTime ToLocal(DateTime utc);

    /// <summary>Un instante con zona → hora de la clínica.</summary>
    DateTime ToLocal(DateTimeOffset instant);

    /// <summary>Hora de la clínica → instante UTC (para comparar con campos de auditoría).</summary>
    DateTime ToUtc(DateTime local);
}
