using System.Reflection;

namespace NemediClinic.Api.Services;

/// <summary>
/// SOLO DESARROLLO (Clock__SimulateServerTimeZone): hace que este proceso crea estar en otra zona
/// horaria, como el IIS de producción en US Eastern, para comprobar que el sistema no depende de
/// ella (tests/e2e/clock.spec.ts). En Linux bastaría la variable TZ; en Windows .NET la ignora y lee
/// la zona del sistema, así que se reemplaza la zona local que TimeZoneInfo guarda en caché.
/// Program.cs se niega a usarlo fuera de Development.
/// </summary>
public static class ServerTimeZoneSimulator
{
    public static void Apply(TimeZoneInfo zone)
    {
        // TimeZoneInfo.s_cachedData (CachedData) → _localTimeZone: de ahí salen TimeZoneInfo.Local,
        // DateTime.Now y TimeProvider.System.LocalTimeZone.
        var cachedField = typeof(TimeZoneInfo)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(f => f.FieldType.Name == "CachedData");
        var cachedData = cachedField?.GetValue(null);
        var localField = cachedData?.GetType().GetField("_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance);
        if (cachedData is null || localField is null)
            throw new InvalidOperationException("Este runtime no permite simular la zona del servidor (cambió TimeZoneInfo).");

        localField.SetValue(cachedData, zone);
        if (TimeProvider.System.LocalTimeZone.Id != zone.Id)
            throw new InvalidOperationException($"No se pudo simular la zona {zone.Id}.");
    }
}
