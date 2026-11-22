using System.Net;
using System.Net.Sockets;

namespace PrivacyLink.Ip.Services;

/// <summary>
/// Resolves the visitor's public IP address from the request after the
/// forwarded-header middleware has run.
/// </summary>
/// <remarks>
/// The resolver is deliberately fail-closed: it never returns a loopback,
/// private, link-local, CGNAT, multicast or unspecified address. Such an
/// address means the trusted-proxy configuration is missing or wrong, and
/// presenting it would leak the internal container/proxy address as if it
/// were the visitor's public address.
/// </remarks>
public sealed class ClientIpResolver
{
    /// <summary>
    /// Converts an IPv4-mapped IPv6 address (for example <c>::ffff:192.0.2.1</c>)
    /// to its IPv4 form. Other addresses are returned unchanged.
    /// </summary>
    public static IPAddress Normalize(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    /// <summary>Returns 4 or 6 for a normalized address.</summary>
    public static int GetVersion(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Normalize(address).AddressFamily == AddressFamily.InterNetwork ? 4 : 6;
    }

    /// <summary>
    /// True when the address is a globally routable address suitable for
    /// presenting as the visitor's public IP. Documentation ranges are treated
    /// as public, so integration tests can use RFC 5737 / RFC 3849 addresses.
    /// </summary>
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var ip = Normalize(address);

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                0 => false, // "this" network / unspecified
                10 => false, // RFC 1918
                100 when bytes[1] is >= 64 and <= 127 => false, // RFC 6598 CGNAT
                127 => false, // loopback
                169 when bytes[1] == 254 => false, // link-local
                172 when bytes[1] is >= 16 and <= 31 => false, // RFC 1918
                192 when bytes[1] == 168 => false, // RFC 1918
                >= 224 => false, // multicast and reserved/broadcast
                _ => true
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(ip))
            {
                return false;
            }

            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return false;
            }

            var bytes = ip.GetAddressBytes();
            // fc00::/7 unique local addresses.
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the effective client address from the request. Returns a
    /// failure result when the address is missing or not publicly routable.
    /// </summary>
    public IpResolution Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return IpResolution.Fail("remote_address_missing");
        }

        var normalized = Normalize(remote);
        if (!IsPubliclyRoutable(normalized))
        {
            return IpResolution.Fail("remote_address_not_public");
        }

        return IpResolution.Ok(normalized);
    }
}

/// <summary>Result of <see cref="ClientIpResolver.Resolve"/>.</summary>
public readonly record struct IpResolution(bool Success, IPAddress? Address, string? FailureReason)
{
    public static IpResolution Ok(IPAddress address) => new(true, address, null);

    public static IpResolution Fail(string reason) => new(false, null, reason);
}
