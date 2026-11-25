using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrivacyLink.Ip.Tests.Infrastructure;
using PrivacyLink.Ip.Web;
using Xunit;

namespace PrivacyLink.Ip.Tests;

/// <summary>
/// Integration coverage for the Dutch homepage described in
/// <c>spec/dutch-homepage.md</c>. Rendered-page tests read the templates that
/// ship in <c>src/PrivacyLink.Ip/Web</c> so the Dutch copy and metadata stay
/// under test; the placeholder tests use an isolated content root.
/// </summary>
public sealed class DutchHomepageTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string V4Host = "v4.ip.privacylink.nl";
    private const string V6Host = "v6.ip.privacylink.nl";
    private const string EnglishUrl = "https://ip.privacylink.nl/";
    private const string DutchUrl = "https://ip.privacylink.nl/nl";

    private const string Template =
        "<!doctype html><html lang=\"nl\"><head>" +
        "<link rel=\"stylesheet\" href=\"{{ASSET_CSS_URL}}\">" +
        "<script src=\"{{ASSET_JS_URL}}\" defer></script>" +
        "</head><body>" +
        "<div id=\"v4\">{{INITIAL_IPV4}}</div>" +
        "<div id=\"v6\">{{INITIAL_IPV6}}</div>" +
        "<script type=\"application/json\" id=\"server-state\">{{SERVER_STATE}}</script>" +
        "</body></html>";

    private static readonly SiteRenderer Renderer = new();

    private static readonly AssetManifest Assets = new(
        Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", "missing-assets"));

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        File.WriteAllText(Path.Combine(root, "Web", "index.nl.html"), Template);
        return root;
    }

    private static void DeleteContentRoot(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
    }

    [Fact]
    public async Task DutchHomepage_ReturnsRenderedHtmlWithNoStore()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/nl", MainHost, "203.0.113.42");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(response.Content.Headers.Contains("Last-Modified"));
        Assert.False(response.Headers.Contains("X-Robots-Tag"));

        Assert.Matches("<html\\b[^>]*lang=\"nl\"", html);
        Assert.Matches("<h1\\b[^>]*>Wat is mijn IP-adres\\?</h1>", html);

        AssertElement(html, "link", ("rel", "canonical"), ("href", DutchUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "en"), ("href", EnglishUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "nl"), ("href", DutchUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "x-default"), ("href", EnglishUrl));

        AssertElement(html, "meta", ("property", "og:locale"), ("content", "nl_NL"));
        AssertElement(html, "meta", ("property", "og:locale:alternate"), ("content", "en_US"));
        AssertElement(html, "meta", ("name", "robots"), ("content", "index, follow"));

        // The language switch is a plain crawlable link back to the English page.
        AssertElement(html, "a", ("href", "/"), ("hreflang", "en"), ("lang", "en"));

        AssertDutchJsonLd(html);

        // The visitor's address must not leak into the static page metadata.
        // The server-state JSON legitimately carries it; only title, meta tags
        // and JSON-LD are asserted here.
        Assert.DoesNotContain("203.0.113.42", ReadMetadata(html));
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task DutchHomepage_HeadReturnsHeadersWithoutBody()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/nl", MainHost, "203.0.113.42", method: "HEAD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DutchHomepage_DoesNotExposeRawTemplateAsStaticFile()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var rawTemplate = await Requests.GetAsync(client, "/assets/index.nl.html", MainHost);
        Assert.Equal(HttpStatusCode.NotFound, rawTemplate.StatusCode);

        using var alias = await Requests.GetAsync(client, "/index.nl.html", MainHost);
        Assert.Equal(HttpStatusCode.MovedPermanently, alias.StatusCode);
        Assert.Equal("/nl", alias.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("203.0.113.42")]
    [InlineData("2001:db8::1")]
    public async Task DutchHomepage_RendersIpv4AndIpv6Placeholders(string remoteIp)
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/nl", MainHost, remoteIp);
            var html = await response.Content.ReadAsStringAsync();

            var isIpv4 = remoteIp.Contains('.', StringComparison.Ordinal);
            var version = isIpv4 ? 4 : 6;
            var otherVersion = isIpv4 ? 6 : 4;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"<div id=\"v4\">{(isIpv4 ? remoteIp : string.Empty)}</div>", html);
            Assert.Contains($"<div id=\"v6\">{(isIpv4 ? string.Empty : remoteIp)}</div>", html);
            Assert.Contains($"\"initialVersion\":{version}", html);
            Assert.Contains($"\"otherVersion\":{otherVersion}", html);
            Assert.DoesNotContain("{{", html);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public void DutchHomepage_EncodesInjectedValues()
    {
        var html = Renderer.RenderHtml(
            Template,
            new IpState("<script>alert(1)</script>&\"'", 4, 6),
            MainHost,
            V4Host,
            V6Host,
            Assets);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&amp;&quot;&#x27;", html);
    }

    [Theory]
    [InlineData(V4Host)]
    [InlineData(V6Host)]
    public async Task DutchHomepage_OnFamilyHostRedirectsToMain(string host)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/nl", host, "203.0.113.42");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(DutchUrl, response.Headers.Location?.ToString());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(MainHost, "/nl")]
    [InlineData(V4Host, DutchUrl)]
    [InlineData(V6Host, DutchUrl)]
    public async Task DutchHomepage_WithTrailingSlashRedirects(string host, string expectedLocation)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/nl/", host, "203.0.113.42");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.ToString());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(MainHost, "/nl")]
    [InlineData(V4Host, DutchUrl)]
    [InlineData(V6Host, DutchUrl)]
    public async Task IndexNlHtml_RedirectsToDutchHomepage(string host, string expectedLocation)
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/index.nl.html", host, "203.0.113.42");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.ToString());
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EnglishHomepage_ContainsDutchHreflang()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/", MainHost, "203.0.113.42");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertElement(html, "link", ("rel", "canonical"), ("href", EnglishUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "en"), ("href", EnglishUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "nl"), ("href", DutchUrl));
        AssertElement(html, "link", ("rel", "alternate"), ("hreflang", "x-default"), ("href", EnglishUrl));

        // The English page advertises the Dutch locale and links to the Dutch page.
        AssertElement(html, "meta", ("property", "og:locale:alternate"), ("content", "nl_NL"));
        AssertElement(html, "a", ("href", "/nl"), ("hreflang", "nl"), ("lang", "nl"));
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task SitemapXml_ContainsDutchHomepage()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/sitemap.xml", MainHost);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<loc>{DutchUrl}</loc>", body);
        Assert.Contains($"<loc>{EnglishUrl}</loc>", body);
        Assert.DoesNotContain(V4Host, body);
        Assert.DoesNotContain(V6Host, body);
    }

    [Fact]
    public async Task RobotsTxt_AllowsDutchHomepageOnMainHost()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/robots.txt", MainHost);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("User-agent: *", body);
        Assert.Contains("Allow: /", body);
        Assert.DoesNotContain("Disallow: /nl", body);
        Assert.Contains($"Sitemap: https://{MainHost}/sitemap.xml", body);
    }

    [Fact]
    public async Task ApiDocs_RemainEnglishAndUnaffected()
    {
        using var factory = new PrivacyLinkAppFactory();
        using var client = factory.CreateAppClient();

        using var response = await Requests.GetAsync(client, "/api", MainHost);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("<html\\b[^>]*lang=\"en\"", html);
        Assert.Contains("API", html);
        Assert.DoesNotContain("hreflang=", html);
        Assert.DoesNotContain("Nederlands", html);
    }

    [Fact]
    public void HomepageTemplates_UseTheSamePlaceholders()
    {
        using var factory = new PrivacyLinkAppFactory();
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        var webRoot = Path.Combine(environment.ContentRootPath, "Web");

        var english = ReadPlaceholders(Path.Combine(webRoot, "index.html"));
        var dutch = ReadPlaceholders(Path.Combine(webRoot, "index.nl.html"));

        Assert.Equal(english, dutch);
        Assert.Equal(
            new[]
            {
                "{{ASSET_CSS_URL}}",
                "{{ASSET_JS_URL}}",
                "{{INITIAL_IPV4}}",
                "{{INITIAL_IPV6}}",
                "{{MAIN_HOST}}",
                "{{SERVER_STATE}}",
                "{{V4_HOST}}",
                "{{V6_HOST}}"
            },
            english);
    }

    private static string[] ReadPlaceholders(string templatePath)
        => Regex.Matches(File.ReadAllText(templatePath), "\\{\\{[A-Z0-9_]+\\}\\}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertDutchJsonLd(string html)
    {
        var graph = ReadJsonLdGraph(html);

        var website = Assert.Single(graph, node => HasType(node, "WebSite"));
        Assert.Equal("nl", website.GetProperty("inLanguage").GetString());
        Assert.Equal($"{DutchUrl}#website", website.GetProperty("@id").GetString());
        Assert.Equal(DutchUrl, website.GetProperty("url").GetString());

        var webPage = Assert.Single(graph, node => HasType(node, "WebPage"));
        Assert.Equal("nl", webPage.GetProperty("inLanguage").GetString());
        Assert.Equal($"{DutchUrl}#webpage", webPage.GetProperty("@id").GetString());
        Assert.Equal(DutchUrl, webPage.GetProperty("url").GetString());

        var webApp = Assert.Single(graph, node => HasType(node, "WebApplication"));
        Assert.Equal("nl", webApp.GetProperty("inLanguage").GetString());
        Assert.Equal($"{DutchUrl}#webapp", webApp.GetProperty("@id").GetString());
        Assert.Equal(DutchUrl, webApp.GetProperty("url").GetString());
    }

    private static List<JsonElement> ReadJsonLdGraph(string html)
    {
        var match = Regex.Match(
            html,
            "<script\\s+type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The Dutch homepage must contain a JSON-LD script block.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        Assert.True(
            document.RootElement.TryGetProperty("@graph", out var graph),
            "The JSON-LD block must contain an @graph array.");
        Assert.Equal(JsonValueKind.Array, graph.ValueKind);

        return graph.EnumerateArray().Select(node => node.Clone()).ToList();
    }

    private static bool HasType(JsonElement node, string type)
    {
        if (!node.TryGetProperty("@type", out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(value.GetString(), type, StringComparison.Ordinal),
            JsonValueKind.Array => value.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String
                    && string.Equals(item.GetString(), type, StringComparison.Ordinal)),
            _ => false
        };
    }

    private static string ReadMetadata(string html)
    {
        var parts = new List<string>();

        var title = Regex.Match(
            html,
            "<title>(?<value>.*?)</title>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (title.Success)
        {
            parts.Add(title.Groups["value"].Value);
        }

        parts.AddRange(Regex
            .Matches(html, "<meta\\b[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value));

        parts.AddRange(Regex
            .Matches(
                html,
                "<script\\s+type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["json"].Value));

        return string.Join("\n", parts);
    }

    private static void AssertElement(string html, string element, params (string Name, string Value)[] attributes)
    {
        var tags = Regex.Matches(html, $"<{element}\\b[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value);

        Assert.True(
            tags.Any(tag => attributes.All(attribute => HasAttribute(tag, attribute.Name, attribute.Value))),
            $"Expected an <{element}> element with "
            + string.Join(", ", attributes.Select(attribute => $"{attribute.Name}=\"{attribute.Value}\""))
            + ".");
    }

    private static bool HasAttribute(string tag, string name, string value)
        => Regex.IsMatch(
            tag,
            $"{name}\\s*=\\s*\"{Regex.Escape(value)}\"",
            RegexOptions.CultureInvariant);
}
