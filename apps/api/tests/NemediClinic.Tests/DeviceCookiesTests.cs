using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NemediClinic.Api.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>
/// Cookie de dispositivo conocido (DeviceCookies): solo exime a la cuenta para la que se emitió, con la
/// clave con la que se emitió, mientras no venza y sin alterar.
/// </summary>
public class DeviceCookiesTests
{
    private const string Hash = "$2a$11$hashDeLaClaveActual";
    private static readonly string Cuenta = LoginThrottle.UserKey(Guid.NewGuid());

    private static (DeviceCookies Cookies, FakeTimeProvider Time) Create()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "llave-de-pruebas-de-al-menos-treinta-y-dos-caracteres" })
            .Build();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero));
        return (new DeviceCookies(config, new AuthRateLimitOptions { DiasDispositivo = 60 }, time), time);
    }

    [Fact]
    public void Exime_a_la_misma_cuenta_con_la_misma_clave()
    {
        var (cookies, _) = Create();
        var valor = cookies.Issue(Cuenta, Hash, out _);
        Assert.NotNull(cookies.Validate(valor, Cuenta, Hash));
    }

    [Fact]
    public void La_cookie_de_otra_cuenta_no_exime()
    {
        var (cookies, _) = Create();
        var valor = cookies.Issue(LoginThrottle.UserKey(Guid.NewGuid()), Hash, out _);
        Assert.Null(cookies.Validate(valor, Cuenta, Hash));
    }

    [Fact]
    public void Tras_cambiar_la_clave_no_exime()
    {
        var (cookies, _) = Create();
        var valor = cookies.Issue(Cuenta, Hash, out _);
        Assert.Null(cookies.Validate(valor, Cuenta, "$2a$11$hashDeLaClaveNueva"));
    }

    [Fact]
    public void Vencida_no_exime()
    {
        var (cookies, time) = Create();
        var valor = cookies.Issue(Cuenta, Hash, out var expires);
        Assert.Equal(time.GetUtcNow().AddDays(60), expires);
        time.Advance(TimeSpan.FromDays(60).Add(TimeSpan.FromSeconds(1)));
        Assert.Null(cookies.Validate(valor, Cuenta, Hash));
    }

    [Fact]
    public void Alterada_o_basura_no_exime()
    {
        var (cookies, _) = Create();
        var valor = cookies.Issue(Cuenta, Hash, out _);
        var partes = valor.Split('.');
        // Otro vencimiento con la firma de antes
        var payload = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(partes[0])));
        var campos = payload.Split('|');
        var otroPayload = $"{campos[0]}|{campos[1]}|{campos[2]}|{long.Parse(campos[3]) + 86_400 * 365}";
        var alterada = B64(System.Text.Encoding.UTF8.GetBytes(otroPayload)) + "." + partes[1];

        Assert.Null(cookies.Validate(alterada, Cuenta, Hash));
        Assert.Null(cookies.Validate(valor + "x", Cuenta, Hash));
        Assert.Null(cookies.Validate("basura", Cuenta, Hash));
        Assert.Null(cookies.Validate(new string('a', 5000), Cuenta, Hash));
        Assert.Null(cookies.Validate(null, Cuenta, Hash));
    }

    [Fact]
    public void Los_atributos_la_protegen()
    {
        var (cookies, _) = Create();
        var options = cookies.Options(DateTimeOffset.UnixEpoch.AddYears(60));
        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal("/api/v1/auth", options.Path);
    }

    private static string Pad(string b64url)
    {
        var b = b64url.Replace('-', '+').Replace('_', '/');
        return b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
