using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Multitenant.Api.Configuration;

/// <summary>
/// Optional proxy support. When enabled, <c>X-Forwarded-For</c> and
/// <c>X-Forwarded-Proto</c> are honoured so the API sees the real client IP
/// (used for auth rate limiting and logs) and the original scheme (used for links).
/// Only the configured proxies/networks are trusted; the framework's loopback
/// default remains unless replaced.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>How many proxies to walk back through. Keep this at the number of proxies you run.</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>Individual proxy IPs that may set forwarding headers.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR form, for example <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];

    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;

        foreach (var proxy in KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains '{proxy}', which is not a valid IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in KnownNetworks)
        {
            var parts = network.Split('/');
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var address)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
                || prefix < 0
                || prefix > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains '{network}', which is not valid CIDR notation.");
            }

            options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefix));
        }
    }
}
