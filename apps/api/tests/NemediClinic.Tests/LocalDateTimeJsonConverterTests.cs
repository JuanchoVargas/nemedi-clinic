using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NemediClinic.Api.Json;
using NemediClinic.Infrastructure.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// La API entrega y recibe hora de la clínica sin sufijo, y un valor con zona entra convertido.
/// El "servidor" está en US Eastern para comprobar que su zona no se cuela en ninguna conversión.
/// </summary>
public class LocalDateTimeJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public LocalDateTimeJsonConverterTests()
    {
        var server = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero));
        server.SetLocalTimeZone(ZonedClock.ResolveTimeZone("America/New_York"));
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new LocalDateTimeJsonConverter(new ZonedClock(server)));
    }

    private DateTime Read(string value) => JsonSerializer.Deserialize<DateTime>($"\"{value}\"", _options);
    private string Write(DateTime value) => JsonSerializer.Serialize(value, _options);

    [Fact]
    public void Sin_zona_entra_tal_cual()
    {
        var d = Read("2026-10-01T09:00:00");
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), d);
        Assert.Equal(DateTimeKind.Unspecified, d.Kind);
    }

    [Theory]
    [InlineData("2026-10-01T14:00:00Z")]
    [InlineData("2026-10-01T14:00:00.000Z")]
    [InlineData("2026-10-01T10:00:00-04:00")]
    [InlineData("2026-10-01T16:00:00+02:00")]
    public void Con_zona_entra_convertido_a_la_hora_de_la_clinica(string value)
    {
        var d = Read(value);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), d);
        Assert.Equal(DateTimeKind.Unspecified, d.Kind);
    }

    [Fact]
    public void Sale_sin_sufijo()
    {
        Assert.Equal("\"2026-10-01T09:00:00\"", Write(new DateTime(2026, 10, 1, 9, 0, 0)));
    }

    [Fact]
    public void Un_valor_UTC_de_auditoria_sale_en_hora_de_la_clinica()
    {
        Assert.Equal("\"2026-10-01T09:00:00\"", Write(new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Nullable_usa_el_mismo_converter()
    {
        var d = JsonSerializer.Deserialize<DateTime?>("\"2026-10-01T14:00:00Z\"", _options);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0), d);
        Assert.Equal("null", JsonSerializer.Serialize<DateTime?>(null, _options));
    }
}
