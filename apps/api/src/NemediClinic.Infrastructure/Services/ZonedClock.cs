using NemediClinic.Application.Interfaces;

namespace NemediClinic.Infrastructure.Services;

/// <summary>
/// <see cref="IClock"/> sobre <see cref="TimeProvider"/>: toma el instante UTC y lo lleva a la zona
/// de la clínica. No usa la hora ni la zona local del servidor, así que da lo mismo en un IIS en US
/// Eastern con horario de verano que en un contenedor en UTC. TimeProvider permite fijar el instante
/// en las pruebas (FakeTimeProvider).
/// </summary>
public sealed class ZonedClock : IClock
{
    /// <summary>Zona por defecto: Bogotá. En Windows es "SA Pacific Standard Time".</summary>
    public const string DefaultTimeZoneId = "America/Bogota";

    private readonly TimeProvider _time;

    public ZonedClock(TimeProvider time, string? timeZoneId = null)
    {
        _time = time;
        TimeZone = ResolveTimeZone(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim());
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public DateTime Now => ToLocal(_time.GetUtcNow());

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone),
            DateTimeKind.Unspecified);

    public DateTime ToLocal(DateTimeOffset instant) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime, DateTimeKind.Unspecified);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    /// <summary>
    /// Resuelve un id de zona IANA ("America/Bogota") o de Windows ("SA Pacific Standard Time") en
    /// cualquier sistema: IIS en Windows conoce los de Windows (y los IANA solo si tiene ICU), un
    /// contenedor Linux conoce los IANA. Si el id directo no existe, se prueba su equivalente.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (TryFind(id, out var zone))
            return zone;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TryFind(windowsId, out zone))
            return zone;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) && TryFind(ianaId, out zone))
            return zone;
        throw new InvalidOperationException(
            $"Clock:TimeZone = '{id}' no es una zona horaria conocida en este servidor. " +
            "Usa un id IANA (America/Bogota) o de Windows (SA Pacific Standard Time).");
    }

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = null!;
            return false;
        }
    }
}
