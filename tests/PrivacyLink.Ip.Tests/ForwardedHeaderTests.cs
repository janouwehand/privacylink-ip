using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class ForwardedHeaderTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string TrustedProxy = "10.0.0.5";

    [Fact]
    public async Task TrustedProxy_ForwardedForSetsClientIp()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy
        });
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: TrustedProxy,
            forwardedFor: "203.0.113.9");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("203.0.113.9\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TrustedProxyNetwork_ForwardedForSetsClientIp()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24"
        });
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: "10.0.0.9",
            forwardedFor: "203.0.113.9");

        Assert.Equal("203.0.113.9\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SpoofedForwardedFor_FromUnknownProxyIsIgnored()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: "198.51.100.7",
            forwardedFor: "1.2.3.4");

        Assert.Equal("198.51.100.7\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MultiHopForwardedFor_DoesNotTrustArbitraryLeftmostValue()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy
        });
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: TrustedProxy,
            forwardedFor: "1.2.3.4, 203.0.113.9");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("203.0.113.9\n", body);
        Assert.DoesNotContain("1.2.3.4", body);
    }

    [Fact]
    public async Task MalformedForwardedFor_IsNotTrusted()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy
        });
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: TrustedProxy,
            forwardedFor: "not-an-ip");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ip_unavailable\n", body);
        Assert.DoesNotContain(TrustedProxy, body);
    }

    [Fact]
    public async Task MissingForwardedHeaderConfiguration_FailsClosed()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            MainHost,
            remoteIp: "10.0.0.5");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ip_unavailable\n", body);
        Assert.DoesNotContain("10.0.0.5", body);
    }

    [Fact]
    public async Task PrivateRemoteAddress_IsNeverReturnedAsPublicIp()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, remoteIp: "192.168.1.10");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("192.168.1.10", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DevAllowance_ReturnsLoopbackWhenEnabledInDevelopment()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["Dev:AllowNonPublicAddresses"] = "true"
        });
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, remoteIp: "127.0.0.1");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("127.0.0.1\n", await response.Content.ReadAsStringAsync());
    }
}
