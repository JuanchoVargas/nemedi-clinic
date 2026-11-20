using Microsoft.Extensions.Configuration;
using NemediClinic.Api.Services;
using Xunit;

namespace NemediClinic.Tests;

/// <summary>Proxies de confianza para X-Forwarded-* (TrustedProxies.Read).</summary>
public class TrustedProxiesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Sin_configuracion_no_hay_proxies()
    {
        Assert.False(TrustedProxies.Read(Config()).Any);
    }

    [Fact]
    public void Lee_ips_y_redes_como_array_o_separadas_por_coma()
    {
        var t = TrustedProxies.Read(Config(
            ("ForwardedHeaders:KnownProxies:0", "172.30.250.10"),
            ("ForwardedHeaders:KnownNetworks", "10.0.0.0/8, fd00::/64")));
        Assert.Single(t.Proxies);
        Assert.Equal(2, t.Networks.Count);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "::/0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "::ffff:0:0/96")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "0.0.0.0")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "::")]
    public void Rechaza_confiar_en_todo_internet(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => TrustedProxies.Read(Config((key, value))));
    }

    [Theory]
    [InlineData("10.0.0.0/7")]
    [InlineData("128.0.0.0/1")]
    [InlineData("2001::/31")]
    [InlineData("::ffff:10.0.0.0/103")]
    public void Rechaza_redes_demasiado_amplias(string red)
    {
        Assert.Throws<InvalidOperationException>(() => TrustedProxies.Read(Config(("ForwardedHeaders:KnownNetworks:0", red))));
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("fd00::/32")]
    [InlineData("::ffff:10.0.0.0/104")]
    [InlineData("172.30.250.0/24")]
    public void Acepta_redes_desde_el_prefijo_minimo(string red)
    {
        Assert.Single(TrustedProxies.Read(Config(("ForwardedHeaders:KnownNetworks:0", red))).Networks);
    }

    [Fact]
    public void Un_valor_invalido_detiene_el_arranque()
    {
        Assert.Throws<InvalidOperationException>(() => TrustedProxies.Read(Config(("ForwardedHeaders:KnownProxies:0", "caddy"))));
    }
}
