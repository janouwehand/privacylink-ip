using Microsoft.Extensions.Configuration;
using PrivacyLink.Ip.Services;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class HostAllowlistTests
{
    private static HostAllowlist Create(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Hosts:Main"] = "ip.privacylink.nl",
            ["Hosts:V4"] = "v4.ip.privacylink.nl",
            ["Hosts:V6"] = "v6.ip.privacylink.nl",
            ["Hosts:MainAliases:0"] = "localhost"
        };

        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new HostAllowlist(configuration);
    }

    [Theory]
    [InlineData("ip.privacylink.nl", HostKind.Main)]
    [InlineData("IP.PRIVACYLINK.NL", HostKind.Main)]
    [InlineData("ip.privacylink.nl.", HostKind.Main)]
    [InlineData("localhost", HostKind.Main)]
    [InlineData("v4.ip.privacylink.nl", HostKind.V4)]
    [InlineData("v6.ip.privacylink.nl", HostKind.V6)]
    public void Classify_AcceptsExactConfiguredHosts(string host, HostKind expected)
    {
        Assert.Equal(expected, Create().Classify(host));
    }

    [Theory]
    [InlineData("v4.ip.privacylink.nl.evil.example")]
    [InlineData("evil-v4.ip.privacylink.nl")]
    [InlineData("notv4.ip.privacylink.nl")]
    [InlineData("ip.privacylink.nl.evil.example")]
    [InlineData("evil.example")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_RejectsUnknownOrSuffixLookalikeHosts(string? host)
    {
        Assert.Equal(HostKind.Unknown, Create().Classify(host));
    }

    [Fact]
    public void Classify_UsesConfiguredHostnamesInsteadOfHardcodedDefaults()
    {
        var allowlist = Create(("Hosts:Main", "example.test"), ("Hosts:V4", "v4.example.test"));

        Assert.Equal(HostKind.Main, allowlist.Classify("example.test"));
        Assert.Equal(HostKind.V4, allowlist.Classify("v4.example.test"));
        Assert.Equal(HostKind.Unknown, allowlist.Classify("ip.privacylink.nl"));
    }
}
