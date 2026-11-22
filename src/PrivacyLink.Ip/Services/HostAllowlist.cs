namespace PrivacyLink.Ip.Services;

/// <summary>Classifies the request Host header against the exact host allowlist.</summary>
public enum HostKind
{
    Unknown = 0,
    Main = 1,
    V4 = 2,
    V6 = 3
}

/// <summary>
/// Exact-match hostname allowlist used to determine whether the request arrived
/// on the main, IPv4-only or IPv6-only hostname.
/// </summary>
/// <remarks>
/// Suffix matching is intentionally not used: <c>evil-v4.ip.privacylink.nl</c>
/// must not be treated as the configured v4 hostname.
/// </remarks>
public sealed class HostAllowlist
{
    private readonly string _main;
    private readonly string _v4;
    private readonly string _v6;
    private readonly HashSet<string> _mainAliases;

    public HostAllowlist(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _main = NormalizeHost(configuration["Hosts:Main"] ?? "ip.privacylink.nl");
        _v4 = NormalizeHost(configuration["Hosts:V4"] ?? "v4.ip.privacylink.nl");
        _v6 = NormalizeHost(configuration["Hosts:V6"] ?? "v6.ip.privacylink.nl");
        _mainAliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alias in configuration.GetSection("Hosts:MainAliases").Get<string[]>() ?? [])
        {
            var normalized = NormalizeHost(alias);
            if (normalized.Length > 0)
            {
                _mainAliases.Add(normalized);
            }
        }
    }

    public string Main => _main;

    public string V4 => _v4;

    public string V6 => _v6;

    /// <summary>Classifies a Host header value. The port is ignored.</summary>
    public HostKind Classify(string? host)
    {
        var normalized = NormalizeHost(host);
        if (normalized.Length == 0)
        {
            return HostKind.Unknown;
        }

        if (string.Equals(normalized, _v4, StringComparison.Ordinal))
        {
            return HostKind.V4;
        }

        if (string.Equals(normalized, _v6, StringComparison.Ordinal))
        {
            return HostKind.V6;
        }

        if (string.Equals(normalized, _main, StringComparison.Ordinal) || _mainAliases.Contains(normalized))
        {
            return HostKind.Main;
        }

        return HostKind.Unknown;
    }

    private static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        // Host.Host never contains a port, but config values may include one.
        // Strip an IPv6 bracket form and any port defensively.
        var value = host.Trim();

        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            value = closing > 0 ? value[1..closing] : value[1..];
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon > 0 && value.IndexOf(':') == colon)
            {
                value = value[..colon];
            }
        }

        return value.TrimEnd('.').ToLowerInvariant();
    }
}
