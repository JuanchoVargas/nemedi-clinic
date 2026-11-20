using System.Net;
using Microsoft.Extensions.Time.Testing;
using NemediClinic.Api.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>Contadores de claves equivocadas por (cuenta, IP) y por cuenta (LoginThrottle).</summary>
public class LoginThrottleTests
{
    private static readonly string Dueno = LoginThrottle.UserKey(Guid.NewGuid());
    private static IPAddress Ip(string ip) => IPAddress.Parse(ip);

    private static (LoginThrottle Throttle, FakeTimeProvider Time) Create(Action<AuthRateLimitOptions>? configure = null)
    {
        var options = new AuthRateLimitOptions { FallosPorCorreoEIp = 3, MinutosBloqueo = 15, FallosPorCorreoPorHora = 100 };
        configure?.Invoke(options);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero));
        return (new LoginThrottle(options, time), time);
    }

    private static void Fail(LoginThrottle throttle, string cuenta, string ip, bool real = true)
    {
        var intento = throttle.Begin(cuenta, Ip(ip), real);
        Assert.False(intento.Blocked);
        throttle.Failed(intento);
    }

    private static bool Blocked(LoginThrottle throttle, string cuenta, string ip) => throttle.Begin(cuenta, Ip(ip), true).Blocked;

    [Fact]
    public void Bloquea_la_cuenta_desde_la_ip_de_los_fallos_pero_no_desde_otra()
    {
        var (throttle, _) = Create();
        for (var i = 0; i < 3; i++)
            Fail(throttle, Dueno, "10.0.0.1");

        var bloqueado = throttle.Begin(Dueno, Ip("10.0.0.1"), true);
        Assert.True(bloqueado.Blocked);
        Assert.True(bloqueado.RetryAfter > TimeSpan.Zero);
        Assert.False(Blocked(throttle, Dueno, "10.0.0.2"));
        Assert.False(Blocked(throttle, LoginThrottle.UserKey(Guid.NewGuid()), "10.0.0.1"));
    }

    [Fact]
    public void Muchos_fallos_por_hora_bloquean_la_cuenta_salvo_con_su_cookie_de_dispositivo()
    {
        var (throttle, time) = Create(o => o.FallosPorCorreoPorHora = 5);
        for (var i = 1; i <= 5; i++)
            Fail(throttle, Dueno, $"10.0.1.{i}");

        Assert.True(Blocked(throttle, Dueno, "10.0.9.9"), "sin cookie");
        Assert.False(throttle.Begin(Dueno, Ip("10.0.9.9"), true, dispositivo: "nonce-del-dueno").Blocked, "con su cookie");
        time.Advance(TimeSpan.FromHours(1));
        Assert.False(Blocked(throttle, Dueno, "10.0.9.9"));
    }

    [Fact]
    public void El_dispositivo_tiene_su_propio_bloqueo_corto()
    {
        var (throttle, time) = Create(o => { o.FallosPorCorreoEIp = 100; o.FallosPorDispositivo = 2; });
        for (var i = 1; i <= 2; i++)
            throttle.Failed(throttle.Begin(Dueno, Ip($"10.0.2.{i}"), true, "robado"));
        Assert.True(throttle.Begin(Dueno, Ip("10.0.2.9"), true, "robado").Blocked);
        Assert.False(throttle.Begin(Dueno, Ip("10.0.2.9"), true, "otro").Blocked);
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.False(throttle.Begin(Dueno, Ip("10.0.2.9"), true, "robado").Blocked);
    }

    [Fact]
    public void Ipv6_cuenta_por_su_64()
    {
        Assert.Equal(LoginThrottle.IpKey(Ip("2001:db8:1:2::1")), LoginThrottle.IpKey(Ip("2001:db8:1:2:ffff:eeee:dddd:cccc")));
        Assert.NotEqual(LoginThrottle.IpKey(Ip("2001:db8:1:2::1")), LoginThrottle.IpKey(Ip("2001:db8:1:3::1")));
        Assert.Equal("10.0.0.1", LoginThrottle.IpKey(Ip("::ffff:10.0.0.1")));

        var (throttle, _) = Create();
        Fail(throttle, Dueno, "2001:db8:1:2::1");
        Fail(throttle, Dueno, "2001:db8:1:2::2");
        Fail(throttle, Dueno, "2001:db8:1:2::3");
        Assert.True(Blocked(throttle, Dueno, "2001:db8:1:2::99"));
        Assert.False(Blocked(throttle, Dueno, "2001:db8:1:3::1"));
    }

    [Fact]
    public void Acertar_devuelve_la_reserva_y_reinicia_el_par()
    {
        var (throttle, _) = Create();
        Fail(throttle, Dueno, "10.0.0.1");
        Fail(throttle, Dueno, "10.0.0.1");
        throttle.Succeeded(throttle.Begin(Dueno, Ip("10.0.0.1"), true));

        for (var i = 0; i < 3; i++)
            Fail(throttle, Dueno, "10.0.0.1");
        Assert.True(Blocked(throttle, Dueno, "10.0.0.1"));
    }

    [Fact]
    public void Con_contadores_compartidos_acertar_devuelve_solo_su_reserva()
    {
        var (throttle, _) = Create();
        Fail(throttle, Dueno, "10.0.0.1");
        Fail(throttle, Dueno, "10.0.0.1");
        throttle.Release(throttle.Begin(Dueno, Ip("10.0.0.1"), true)); // otra cuenta con el mismo correo entra

        // Los dos fallos siguen: uno más bloquea
        Fail(throttle, Dueno, "10.0.0.1");
        Assert.True(Blocked(throttle, Dueno, "10.0.0.1"));
    }

    [Fact]
    public void Los_intentos_simultaneos_no_pasan_del_cupo()
    {
        var (throttle, _) = Create(o => o.FallosPorCorreoEIp = 10);
        var reservados = 0;
        Parallel.For(0, 200, _ =>
        {
            if (!throttle.Begin(Dueno, Ip("10.0.0.1"), true).Blocked)
                Interlocked.Increment(ref reservados);
        });
        Assert.Equal(10, reservados);
    }

    [Fact]
    public void Un_correo_que_no_existe_tiene_clave_de_tamano_fijo()
    {
        var corto = LoginThrottle.UnknownKey("a@b.test");
        var largo = LoginThrottle.UnknownKey(new string('x', 5000) + "@b.test");
        Assert.Equal(corto.Length, largo.Length);
        Assert.Equal(LoginThrottle.UnknownKey("ａdmin@clinica.test"), LoginThrottle.UnknownKey(" ADMIN@clinica.test "));
    }

    [Fact]
    public void Restablecer_la_clave_limpia_los_contadores_de_la_cuenta()
    {
        var (throttle, _) = Create(o => o.FallosPorCorreoPorHora = 3);
        for (var i = 0; i < 3; i++)
            Fail(throttle, Dueno, "10.0.0.1");
        Assert.True(Blocked(throttle, Dueno, "10.0.0.1"));
        Assert.True(Blocked(throttle, Dueno, "10.0.0.2"));

        throttle.Reset(Dueno);
        Assert.False(Blocked(throttle, Dueno, "10.0.0.1"));
    }

    [Fact]
    public void El_bloqueo_del_par_vence_con_su_ventana()
    {
        var (throttle, time) = Create();
        for (var i = 0; i < 3; i++)
            Fail(throttle, Dueno, "10.0.0.1");
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.False(Blocked(throttle, Dueno, "10.0.0.1"));
    }

    [Fact]
    public void El_retardo_se_duplica_con_cada_fallo_hasta_el_tope()
    {
        var (throttle, _) = Create(o => { o.FallosPorCorreoEIp = 100; o.RetardoBaseMs = 500; o.RetardoMaximoMs = 3000; });
        var retardos = Enumerable.Range(0, 5).Select(_ => throttle.Failed(throttle.Begin(Dueno, Ip("10.0.0.1"), true)).TotalMilliseconds).ToList();
        Assert.Equal(new double[] { 500, 1000, 2000, 3000, 3000 }, retardos);
    }

    [Fact]
    public void Un_intento_bloqueado_no_crea_contadores()
    {
        var (throttle, _) = Create(o => o.FallosPorCorreoPorHora = 1);
        Fail(throttle, Dueno, "10.0.0.1");
        var antes = throttle.Entradas;
        for (var i = 0; i < 500; i++)
            Assert.True(Blocked(throttle, Dueno, $"10.1.{i / 250}.{i % 250}"));
        Assert.Equal(antes, throttle.Entradas);
    }

    [Fact]
    public void Un_bloqueo_no_se_descarta_antes_de_vencer_aunque_sea_de_un_correo_que_no_existe()
    {
        // Si se descartara, inundar la tabla diría qué correos no existen (su bloqueo se olvida, el de una
        // cuenta real no): un oráculo de enumeración
        var (throttle, _) = Create(o => o.MaxEntradas = 100);
        var fantasma = LoginThrottle.UnknownKey("fantasma@x.test");
        for (var i = 0; i < 3; i++)
            Fail(throttle, fantasma, "10.0.0.1", real: false);
        Assert.True(throttle.Begin(fantasma, Ip("10.0.0.1"), false).Blocked);

        for (var i = 0; i < 1000; i++)
            Fail(throttle, LoginThrottle.UnknownKey($"basura{i}@x.test"), "10.0.0.2", real: false);

        Assert.True(throttle.Begin(fantasma, Ip("10.0.0.1"), false).Blocked, "el bloqueo del correo inexistente sigue");
    }

    [Fact]
    public void Para_hacer_sitio_se_descartan_primero_los_correos_que_no_existen()
    {
        var (throttle, _) = Create(o => o.MaxEntradas = 100);
        Fail(throttle, Dueno, "10.0.0.1"); // un fallo: por debajo de la mitad de su umbral

        for (var i = 0; i < 1000; i++)
            Fail(throttle, LoginThrottle.UnknownKey($"basura{i}@x.test"), "10.0.0.2", real: false);

        // El fallo de la cuenta real sigue contando: dos más la bloquean desde esa IP
        Fail(throttle, Dueno, "10.0.0.1");
        Fail(throttle, Dueno, "10.0.0.1");
        Assert.True(Blocked(throttle, Dueno, "10.0.0.1"));
    }

    [Fact]
    public void Correos_basura_no_desbloquean_a_la_victima_y_la_memoria_queda_acotada()
    {
        var (throttle, _) = Create(o => o.MaxEntradas = 100);
        for (var i = 0; i < 3; i++)
            Fail(throttle, Dueno, "10.0.0.1");

        for (var i = 0; i < 5000; i++)
            Fail(throttle, LoginThrottle.UnknownKey($"basura{i}@x.test"), "10.0.0.2", real: false);

        Assert.True(Blocked(throttle, Dueno, "10.0.0.1"), "la víctima sigue bloqueada");
        Assert.True(throttle.Entradas <= 100, $"{throttle.Entradas} contadores");
    }

    [Fact]
    public async Task Una_cuenta_no_ocupa_todos_los_cupos_de_espera()
    {
        var (throttle, _) = Create(o => { o.FallosPorCorreoEIp = 100; o.RetardoBaseMs = 1000; o.MaxEsperando = 50; o.MaxEsperandoPorCuenta = 2; });
        var otra = LoginThrottle.UserKey(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        var esperando = Enumerable.Range(0, 2).Select(_ => throttle.FailAndDelayAsync(throttle.Begin(Dueno, Ip("10.0.0.1"), true), cts.Token)).ToList();

        // Si la regla fallara, esta llamada esperaría: el token la corta y la prueba falla en vez de colgarse
        using var corta = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.False(await throttle.FailAndDelayAsync(throttle.Begin(Dueno, Ip("10.0.0.1"), true), corta.Token), "tercera de la misma cuenta");
        var deOtra = throttle.FailAndDelayAsync(throttle.Begin(otra, Ip("10.0.0.1"), true), cts.Token);
        Assert.Equal(3, throttle.Esperando);

        cts.Cancel();
        await Task.WhenAll(esperando.Append(deOtra));
        Assert.Equal(0, throttle.Esperando);
    }

    [Fact]
    public void Un_correo_sin_cuenta_cuenta_igual_con_invisibles_o_con_eszett()
    {
        Assert.Equal(LoginThrottle.UnknownKey("nadie@x.test"), LoginThrottle.UnknownKey("na‍die@x.test"));
        Assert.Equal(LoginThrottle.UnknownKey("nadie@x.test"), LoginThrottle.UnknownKey("﻿na­die⁠@x.test"));
        Assert.Equal(LoginThrottle.UnknownKey("strasse@x.test"), LoginThrottle.UnknownKey("STRAßE@x.test"));
        Assert.Equal("strasse@x.test", LoginThrottle.NormalizeEmail("stra​ße@x.test"));
    }

    [Fact]
    public async Task Con_demasiadas_peticiones_esperando_responde_sin_esperar()
    {
        var (throttle, _) = Create(o => { o.FallosPorCorreoEIp = 100; o.RetardoBaseMs = 1000; o.MaxEsperando = 2; });
        using var cts = new CancellationTokenSource();
        var esperando = Enumerable.Range(0, 2).Select(_ => throttle.FailAndDelayAsync(throttle.Begin(Dueno, Ip("10.0.0.1"), true), cts.Token)).ToList();
        Assert.Equal(2, throttle.Esperando);

        Assert.False(await throttle.FailAndDelayAsync(throttle.Begin(Dueno, Ip("10.0.0.1"), true), CancellationToken.None));

        cts.Cancel(); // el cliente se va: la espera se corta
        Assert.All(await Task.WhenAll(esperando), Assert.True);
        Assert.Equal(0, throttle.Esperando);
    }
}
