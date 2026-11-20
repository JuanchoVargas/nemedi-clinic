using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NemediClinic.Application.Interfaces;

namespace NemediClinic.Api.Json;

/// <summary>
/// Regla del sistema: todo DateTime de negocio viaja como hora de la clínica (Clock:TimeZone,
/// Bogotá) sin zona, "yyyy-MM-ddTHH:mm:ss", en ambos sentidos. Nada depende de la zona del servidor.
///
/// Lectura: sin zona → se guarda tal cual (Kind = Unspecified). Si un cliente manda "Z" u offset
/// ("…-04:00"), ese instante se lleva a la hora de la clínica y se descarta la zona, para que nunca
/// entre a la base un valor UTC u otra hora "disfrazados".
///
/// Escritura: siempre sin sufijo. Un valor con Kind = Utc (auditoría: CreatedAt, UpdatedAt; EF los
/// marca así al leerlos, ver AppDbContext) se convierte a la hora de la clínica.
/// </summary>
public sealed class LocalDateTimeJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss";
    private readonly IClock _clock;

    public LocalDateTimeJsonConverter(IClock clock) => _clock = clock;

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException("Fecha vacía.");

        // Con zona: DateTimeOffset conserva el instante exacto. DateTime.Parse lo llevaría a la zona
        // del servidor (Kind = Local), que es justo lo que no puede pasar.
        if (HasZone(raw))
        {
            if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                throw new JsonException($"Fecha inválida: {raw}");
            return _clock.ToLocal(instant);
        }

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new JsonException($"Fecha inválida: {raw}");
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var local = value.Kind == DateTimeKind.Utc ? _clock.ToLocal(value) : value;
        writer.WriteStringValue(local.ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <summary>¿Trae "Z" o un offset ±hh:mm después de la hora?</summary>
    private static bool HasZone(string raw)
    {
        var t = raw.IndexOf('T');
        if (t < 0) return false;
        var time = raw.AsSpan(t + 1);
        return time.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || time.IndexOfAny('+', '-') >= 0;
    }
}
