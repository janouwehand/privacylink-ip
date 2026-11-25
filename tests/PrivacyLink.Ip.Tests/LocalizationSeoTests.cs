using System.Net;
using System.Xml.Linq;
using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class LocalizationSeoTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string Origin = "https://" + MainHost;

    [Theory]
    [InlineData(null, "/en/")]
    [InlineData("fr-FR", "/en/")]
    [InlineData("nl-NL,nl;q=0.9,en;q=0.8", "/nl/")]
    [InlineData("fr-FR,nl-BE;q=0.9,en;q=0.8", "/nl/")]
    [InlineData("nl-BE;q=0.5,en-GB;q=0.9", "/en/")]
    [InlineData("nl;q=0", "/en/")]
    public async Task Root_SelectsLanguageWithoutCaching(string? preference, string expectedPath)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = MainHost;
        if (preference is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", preference);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedPath, response.Headers.Location?.OriginalString);
        Assert.Contains("Accept-Language", response.Headers.Vary);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(MainHost, "nl", "en-GB", "Wat is mijn IP-adres?", "203.0.113.42")]
    [InlineData(MainHost, "en", "nl-BE", "What Is My IP Address?", "203.0.113.42")]
    [InlineData("v4.ip.privacylink.nl", "nl", "en-GB", "Wat is mijn IP-adres?", "203.0.113.42")]
    [InlineData("v6.ip.privacylink.nl", "en", "nl-BE", "What Is My IP Address?", "2001:db8::1")]
    public async Task LanguagePage_IsStableAndReferencesMainHost(
        string host, string language, string preference, string title, string address)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{language}/");
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("Accept-Language", preference);
        TestRemoteIp.Set(request, address);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{language}\">", html);
        Assert.Contains($"<title>{title}", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"description\"", html);
        Assert.Matches($"""<link(?=[^>]*rel="canonical")(?=[^>]*href="{UriPattern($"/{language}/")}")[^>]*>""", html);
        AssertAlternate(html, "en", "/en/");
        AssertAlternate(html, "nl", "/nl/");
        AssertAlternate(html, "x-default", "/");
        Assert.Contains("href=\"/en/\"", html);
        Assert.Contains("href=\"/nl/\"", html);
        Assert.Matches($"""<a\b(?=[^>]*href="/{language}/")(?=[^>]*aria-current="page")[^>]*>""", html);
        var otherLanguage = language == "nl" ? "en" : "nl";
        Assert.DoesNotMatch($"""<a\b(?=[^>]*href="/{otherLanguage}/")(?=[^>]*aria-current="page")[^>]*>""", html);
        Assert.Matches($"""(?s)<(?:span|div|section)\b[^>]*data-nosnippet[^>]*>(?:(?!</(?:span|div|section)>).)*{System.Text.RegularExpressions.Regex.Escape(address)}""", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task Sitemap_ListsOnlyCanonicalPages()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();
        using var robots = await Requests.GetAsync(client, "/robots.txt", MainHost);
        robots.EnsureSuccessStatusCode();
        Assert.Contains($"Sitemap: {Origin}/sitemap.xml", await robots.Content.ReadAsStringAsync());

        using var sitemap = await Requests.GetAsync(client, "/sitemap.xml", MainHost);
        sitemap.EnsureSuccessStatusCode();
        var root = XDocument.Parse(await sitemap.Content.ReadAsStringAsync()).Root!;
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(ns + "urlset", root.Name);
        Assert.Equal(
            new[] { $"{Origin}/api", $"{Origin}/en/", $"{Origin}/nl/" },
            root.Elements(ns + "url").Select(url => url.Element(ns + "loc")?.Value).Order());
    }

    [Theory]
    [InlineData("/api/ip")]
    [InlineData("/api/ip.json")]
    public async Task RawApi_IsNotIndexable(string path)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();
        using var response = await Requests.GetAsync(client, path, MainHost, "203.0.113.42");
        response.EnsureSuccessStatusCode();
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag"));
    }

    [Fact]
    public async Task ApiDocumentation_RemainsEnglishAndIndexableOnTheMainUrl()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();
        using var response = await Requests.GetAsync(client, "/api", "v4.ip.privacylink.nl");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Matches($"""<link(?=[^>]*rel="canonical")(?=[^>]*href="{UriPattern("/api")}")[^>]*>""", html);
        Assert.DoesNotContain("hreflang=", html);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    private static string UriPattern(string path) => System.Text.RegularExpressions.Regex.Escape(Origin + path);

    private static void AssertAlternate(string html, string language, string path) =>
        Assert.Matches($"""<link(?=[^>]*rel="alternate")(?=[^>]*hreflang="{language}")(?=[^>]*href="{UriPattern(path)}")[^>]*>""", html);
}
