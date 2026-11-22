using System.Text.Json;
using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class ApiEndpointTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string V4Host = "v4.ip.privacylink.nl";
    private const string V6Host = "v6.ip.privacylink.nl";

    [Fact]
    public async Task PlainEndpoint_ReturnsIpv4WithSingleTrailingNewline()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("203.0.113.42\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task PlainEndpoint_ReturnsCompactIpv6()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, "2001:0db8:0000:0000:0000:0000:0000:0001");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2001:db8::1\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PlainEndpoint_NormalizesIpv4MappedIpv6()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, "::ffff:203.0.113.42");

        Assert.Equal("203.0.113.42\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JsonEndpoint_HasExactSchema()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip.json", MainHost, "2001:db8::1");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"ip\":\"2001:db8::1\",\"version\":6}", body);

        using var document = JsonDocument.Parse(body);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Equal("2001:db8::1", document.RootElement.GetProperty("ip").GetString());
        Assert.Equal(6, document.RootElement.GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData(V4Host, "2001:db8::1")]
    [InlineData(V6Host, "203.0.113.42")]
    public async Task FamilyMismatch_Returns503WithoutIp(string host, string remoteIp)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", host, remoteIp);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ip_family_mismatch\n", body);
        Assert.DoesNotContain(remoteIp, body);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task FamilyMismatch_JsonReturnsErrorCode()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip.json", V4Host, "2001:db8::1");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"error\":\"ip_family_mismatch\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task V4AndV6Hosts_ReturnMatchingFamily()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var v4 = await Requests.GetAsync(client, "/api/ip", V4Host, "203.0.113.42");
        using var v6 = await Requests.GetAsync(client, "/api/ip", V6Host, "2001:db8::1");

        Assert.Equal("203.0.113.42\n", await v4.Content.ReadAsStringAsync());
        Assert.Equal("2001:db8::1\n", await v6.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Healthz_WorksWithoutExternalDependencies()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/healthz", "localhost", "127.0.0.1");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HeadRequest_ReturnsHeadersWithoutBody()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, "203.0.113.42", method: "HEAD");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OtherMethods_Return405()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", MainHost, "203.0.113.42", method: "POST");

        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task UnknownHostname_IsRejected()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", "evil.example", "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("203.0.113.42", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("v4.ip.privacylink.nl.evil.example")]
    [InlineData("evil-v4.ip.privacylink.nl")]
    public async Task HostHeaderLookalikes_AreRejected(string host)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", host, "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}
