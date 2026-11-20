using Microsoft.Extensions.Time.Testing;
using NemediClinic.Infrastructure.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// El reloj de la clínica frente a un servidor en US Eastern con horario de verano (el IIS de
/// producción). FakeTimeProvider fija el instante UTC y la zona local "del servidor"; el reloj no
/// debe depender de esta última.
/// </summary>
public class ZonedClockTests
{
    private static readonly TimeZoneInfo Eastern = ZonedClock.ResolveTimeZone("America/New_York");

    /// <summary>Un servidor en US Eastern parado en <paramref name="utc"/>.</summary>
    private static (ZonedClock Clock, FakeTimeProvider Server) EasternServerAt(DateTimeOffset utc, string? clinicZone = null)
    {
        var server = new FakeTimeProvider(utc);
        server.SetLocalTimeZone(Eastern);
        return (new ZonedClock(server, clinicZone), server);
    }

    [Theory]
    [InlineData("America/Bogota")]
    [InlineData("SA Pacific Standard Time")]
    public void Resuelve_Bogota_con_id_IANA_o_de_Windows(string id)
    {
        var zone = ZonedClock.ResolveTimeZone(id);
        Assert.Equal(TimeSpan.FromHours(-5), zone.BaseUtcOffset);
        Assert.False(zone.SupportsDaylightSavingTime);
    }

    [Fact]
    public void Una_zona_inexistente_falla_con_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ZonedClock.ResolveTimeZone("Marte/Olympus"));
        Assert.Contains("Clock:TimeZone", ex.Message);
    }

    [Fact]
    public void Por_defecto_la_clinica_esta_en_Bogota()
    {
        var (clock, _) = EasternServerAt(new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), clock.Now);
        Assert.Equal(DateTimeKind.Unspecified, clock.Now.Kind);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }

    [Fact]
    public void La_hora_del_servidor_no_cambia_la_de_la_clinica()
    {
        var instante = new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
        var (clock, server) = EasternServerAt(instante);
        // El servidor (EDT, UTC−4) cree que son las 10:00; la clínica sigue en las 09:00
        Assert.Equal(10, server.GetLocalNow().Hour);
        Assert.Equal(9, clock.Now.Hour);

        server.SetLocalTimeZone(TimeZoneInfo.Utc);
        Assert.Equal(9, clock.Now.Hour);
    }

    [Fact]
    public void Fin_del_horario_de_verano_en_Eastern_no_repite_la_hora_en_la_clinica()
    {
        // 2026-11-01: Eastern pasa de EDT (UTC−4) a EST (UTC−5) a las 06:00 UTC y repite la 01:xx.
        var antes = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        var (clock, server) = EasternServerAt(antes);
        var servidorAntes = server.GetLocalNow().DateTime;
        var clinicaAntes = clock.Now;

        server.Advance(TimeSpan.FromHours(1));
        var servidorDespues = server.GetLocalNow().DateTime;
        var clinicaDespues = clock.Now;

        // El servidor marca 01:30 dos veces; una hora real después, la clínica avanzó una hora
        Assert.Equal(servidorAntes, servidorDespues);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 30, 0), clinicaAntes);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), clinicaDespues);
    }

    [Fact]
    public void Inicio_del_horario_de_verano_en_Eastern_no_salta_una_hora_en_la_clinica()
    {
        // 2027-03-14: Eastern salta de 02:00 EST a 03:00 EDT a las 07:00 UTC.
        var (clock, server) = EasternServerAt(new DateTimeOffset(2027, 3, 14, 6, 30, 0, TimeSpan.Zero));
        var servidorAntes = server.GetLocalNow().DateTime;
        var clinicaAntes = clock.Now;

        server.Advance(TimeSpan.FromHours(1));

        // El servidor avanzó dos horas de pared en una hora real; la clínica, una
        Assert.Equal(TimeSpan.FromHours(2), server.GetLocalNow().DateTime - servidorAntes);
        Assert.Equal(TimeSpan.FromHours(1), clock.Now - clinicaAntes);
        Assert.Equal(new DateTime(2027, 3, 14, 2, 30, 0), clock.Now);
    }

    [Fact]
    public void El_dia_cambia_a_la_medianoche_de_la_clinica_no_a_la_del_servidor()
    {
        // 04:30 UTC del 1 de octubre: en Eastern ya es 1 de octubre (00:30); en Bogotá, 30 de septiembre (23:30)
        var (clock, server) = EasternServerAt(new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 1), DateOnly.FromDateTime(server.GetLocalNow().DateTime));
        Assert.Equal(new DateOnly(2026, 9, 30), clock.Today);
    }

    [Fact]
    public void ToLocal_y_ToUtc_son_inversos_y_ToLocal_toma_Unspecified_como_UTC()
    {
        var (clock, _) = EasternServerAt(new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero));
        var local = new DateTime(2026, 10, 1, 9, 0, 0);
        var utc = clock.ToUtc(local);
        Assert.Equal(new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc), utc);
        // EF devuelve Unspecified lo que se guardó como UTC
        Assert.Equal(local, clock.ToLocal(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
        Assert.Equal(local, clock.ToLocal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void La_zona_es_configurable()
    {
        // Una clínica en Madrid (CEST, UTC+2 a finales de septiembre)
        var (clock, _) = EasternServerAt(new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero), "Europe/Madrid");
        Assert.Equal(new DateTime(2026, 9, 28, 16, 0, 0), clock.Now);
    }
}
