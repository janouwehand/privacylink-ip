using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class SecurityHeaderTests
{
    private const string V4Host = "v4.ip.privacylink.nl";
    private const string V6Host = "v6.ip.privacylink.nl";
    private const string CanonicalOrigin = "https://ip.privacylink.nl";

    [Fact]
    public async Task V4Api_AllowsCanonicalOriginWithoutWildcardOrCredentials()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(
            client,
            "/api/ip",
            V4Host,
            remoteIp: "203.0.113.42",
            origin: "https://evil.example");

        Assert.Equal(CanonicalOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task V6Api_AllowsCanonicalOrigin()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", V6Host, remoteIp: "2001:db8::1");

        Assert.Equal(CanonicalOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task MainHost_DoesNotEmitCrossOriginAllowHeader()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", "ip.privacylink.nl", "203.0.113.42");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task IpResponses_HaveSecurityHeadersAndNoStore()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", "ip.privacylink.nl", "203.0.113.42");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(response.Content.Headers.Contains("Last-Modified"));

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("connect-src 'self' https://v4.ip.privacylink.nl https://v6.ip.privacylink.nl", csp);
        Assert.DoesNotContain("'unsafe-inline'", csp);
    }

    [Fact]
    public async Task Hsts_IsPerHostnameWithoutSubdomainsOrPreload()
    {
        using var factory = new PrivacyLinkAppFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
        });
        using var client = factory.CreateAppClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ip");
        request.Headers.Host = "ip.privacylink.nl";
        request.Headers.TryAddWithoutValidation(TestRemoteIpStartupFilter.HeaderName, "10.0.0.5");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.42");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal("max-age=31536000", response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task UnknownHost_DoesNotLeakClientIpInSecurityHeadersTest()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip.json", "attacker.example", "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("203.0.113.42", await response.Content.ReadAsStringAsync());
    }
}
