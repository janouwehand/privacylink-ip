using System.Net;
using System.Net.Sockets;
using PrivacyLink.Ip.Services;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class ClientIpResolverTests
{
    [Theory]
    [InlineData("203.0.113.42", 4)]
    [InlineData("2001:db8:85a3::8a2e:370:7334", 6)]
    [InlineData("::ffff:203.0.113.42", 4)]
    public void GetVersion_NormalizesAddressFamily(string value, int expected)
    {
        Assert.Equal(expected, ClientIpResolver.GetVersion(IPAddress.Parse(value)));
    }

    [Fact]
    public void Normalize_ConvertsIpv4MappedAddressWithoutBracketsOrPort()
    {
        var normalized = ClientIpResolver.Normalize(IPAddress.Parse("::ffff:192.0.2.1"));

        Assert.Equal(AddressFamily.InterNetwork, normalized.AddressFamily);
        Assert.Equal("192.0.2.1", normalized.ToString());
        Assert.DoesNotContain(":", normalized.ToString());
        Assert.DoesNotContain("[", normalized.ToString());
    }

    [Fact]
    public void Normalize_KeepsRealIpv6AddressCompact()
    {
        var normalized = ClientIpResolver.Normalize(IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001"));

        Assert.Equal("2001:db8::1", normalized.ToString());
    }

    [Theory]
    [InlineData("203.0.113.42", true)]
    [InlineData("198.51.100.7", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.16.4.2", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.5", false)]
    public void IsPubliclyRoutable_RejectsNonPublicRanges(string value, bool expected)
    {
        Assert.Equal(expected, ClientIpResolver.IsPubliclyRoutable(IPAddress.Parse(value)));
    }
}
