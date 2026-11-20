using System.Net;

namespace NemediClinic.Api.Services;

/// <summary>
/// Proxies de los que el API acepta X-Forwarded-For/Proto/Host: ForwardedHeaders:KnownProxies (IPs) y
/// ForwardedHeaders:KnownNetworks (CIDR), como array o separados por coma. Un valor que no se entiende
/// detiene el arranque.
/// </summary>
public sealed record TrustedProxies(IReadOnlyList<IPAddress> Proxies, IReadOnlyList<IPNetwork> Networks)
{
    public const string ProxiesKey = "ForwardedHeaders:KnownProxies";
    public const string NetworksKey = "ForwardedHeaders:KnownNetworks";

    public bool Any => Proxies.Count > 0 || Networks.Count > 0;

    public static TrustedProxies Read(IConfiguration config)
    {
        var proxies = Parse(config, ProxiesKey, IPAddress.Parse);
        var networks = Parse(config, NetworksKey, IPNetwork.Parse);

        // Confiar en todo internet (o en un pedazo enorme) es lo mismo que no tener la regla: no arranca
        foreach (var proxy in proxies.Where(p => p.Equals(IPAddress.Any) || p.Equals(IPAddress.IPv6Any)))
            throw new InvalidOperationException($"{ProxiesKey} no puede ser \"{proxy}\": declara la IP del proxy.");
        foreach (var network in networks.Where(EsDemasiadoAmplia))
            throw new InvalidOperationException(
                $"{NetworksKey} no puede ser \"{network}\": es demasiado amplia (mínimo /{PrefijoMinimo(network)}). Declara la red o la IP del proxy.");
        return new TrustedProxies(proxies, networks);
    }

    /// <summary>Prefijo mínimo: /8 en IPv4, /32 en IPv6, /104 en IPv4 mapeada en IPv6 (= /8 de IPv4).</summary>
    private static int PrefijoMinimo(IPNetwork network) =>
        network.BaseAddress.IsIPv4MappedToIPv6 ? 104
        : network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 8
        : 32;

    private static bool EsDemasiadoAmplia(IPNetwork network) => network.PrefixLength < PrefijoMinimo(network);

    private static List<T> Parse<T>(IConfiguration config, string key, Func<string, T> parse)
    {
        var section = config.GetSection(key);
        var values = section.GetChildren().Select(c => c.Value)
            .Concat((section.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrEmpty(v));
        var result = new List<T>();
        foreach (var value in values)
        {
            try
            {
                result.Add(parse(value!));
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"{key} tiene un valor que no es una IP o red válida: \"{value}\".");
            }
        }
        return result;
    }
}
