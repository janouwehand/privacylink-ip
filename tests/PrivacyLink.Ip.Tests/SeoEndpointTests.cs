using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class SeoEndpointTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string V4Host = "v4.ip.privacylink.nl";
    private const string V6Host = "v6.ip.privacylink.nl";

    [Fact]
    public async Task RobotsTxt_AllowsCrawlingAndPointsToTheSitemap()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/robots.txt", MainHost);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Contains("User-agent: *", body);
        Assert.Contains("Allow: /", body);
        Assert.Contains("Disallow: /healthz", body);
        Assert.DoesNotContain("Disallow: /nl", body);
        Assert.Contains($"Sitemap: https://{MainHost}/sitemap.xml", body);
        Assert.DoesNotContain("Disallow: /\n", body);
    }

    [Theory]
    [InlineData(V4Host)]
    [InlineData(V6Host)]
    public async Task RobotsTxt_DisallowsTheApiOnlyHostnames(string host)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/robots.txt", host);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("User-agent: *", body);
        Assert.Contains("Disallow: /", body);
        Assert.DoesNotContain("Sitemap:", body);
    }

    [Fact]
    public async Task SitemapXml_ListsOnlyCanonicalMainHostPages()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/sitemap.xml", MainHost);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Contains("<loc>https://ip.privacylink.nl/</loc>", body);
        Assert.Contains("<loc>https://ip.privacylink.nl/nl</loc>", body);
        Assert.Contains("<loc>https://ip.privacylink.nl/api</loc>", body);
        Assert.DoesNotContain(V4Host, body);
        Assert.DoesNotContain(V6Host, body);
    }

    [Theory]
    [InlineData("/api/ip")]
    [InlineData("/api/ip.json")]
    [InlineData("/healthz")]
    public async Task DataEndpoints_AreNotIndexable(string path)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, path, MainHost, "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task ErrorResponses_AreNotIndexable()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api/ip", "attacker.example", "203.0.113.42");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }
}
