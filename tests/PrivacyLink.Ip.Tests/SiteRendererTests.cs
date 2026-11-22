using PrivacyLink.Ip.Web;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class SiteRendererTests
{
    private const string Template =
        "<div id=\"v4\">{{INITIAL_IPV4}}</div>" +
        "<div id=\"v6\">{{INITIAL_IPV6}}</div>" +
        "<script type=\"application/json\" id=\"state\">{{SERVER_STATE}}</script>";

    private static readonly SiteRenderer Renderer = new();

    private static readonly AssetManifest Assets = new(
        Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", "missing-assets"));

    [Fact]
    public void RenderHtml_FillsOnlyTheMatchingFamilyPlaceholder()
    {
        var html = Renderer.RenderHtml(Template, new IpState("203.0.113.42", 4, 6), Assets);

        Assert.Contains("<div id=\"v4\">203.0.113.42</div>", html);
        Assert.Contains("<div id=\"v6\"></div>", html);
    }

    [Fact]
    public void RenderHtml_FillsIpv6AndLeavesIpv4Empty()
    {
        var html = Renderer.RenderHtml(Template, new IpState("2001:db8::1", 6, 4), Assets);

        Assert.Contains("<div id=\"v4\"></div>", html);
        Assert.Contains("<div id=\"v6\">2001:db8::1</div>", html);
    }

    [Fact]
    public void RenderHtml_EncodesHtmlInjectionInModelValues()
    {
        var html = Renderer.RenderHtml(Template, new IpState("<script>alert(1)</script>&\"'", 4, 6), Assets);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("203.0.113.42", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&amp;&quot;&#x27;", html);
    }

    [Fact]
    public void RenderHtml_WritesExactServerStateShape()
    {
        var html = Renderer.RenderHtml(Template, new IpState("203.0.113.42", 4, 6), Assets);

        Assert.Contains(
            "<script type=\"application/json\" id=\"state\">{\"initialIp\":\"203.0.113.42\",\"initialVersion\":4,\"otherVersion\":6}</script>",
            html);
    }

    [Fact]
    public void RenderHtml_ReplacesEveryPlaceholderOccurrence()
    {
        const string template = "{{INITIAL_IPV4}}-{{INITIAL_IPV4}}{{SERVER_STATE}}{{SERVER_STATE}}";

        var html = Renderer.RenderHtml(template, new IpState("203.0.113.42", 4, 6), Assets);

        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void RenderDocs_ReplacesEveryHostPlaceholderOccurrence()
    {
        const string template = "{{MAIN_HOST}}|{{V4_HOST}}|{{V6_HOST}}{{MAIN_HOST}}{{V4_HOST}}{{V6_HOST}}";

        var html = Renderer.RenderDocs(template, "ip.privacylink.nl", "v4.ip.privacylink.nl", "v6.ip.privacylink.nl", Assets);

        Assert.DoesNotContain("{{", html);
        Assert.Equal(
            "ip.privacylink.nl|v4.ip.privacylink.nl|v6.ip.privacylink.nlip.privacylink.nlv4.ip.privacylink.nlv6.ip.privacylink.nl",
            html);
    }

    [Fact]
    public void RenderDocs_EncodesHtmlInjectionInHostValues()
    {
        var html = Renderer.RenderDocs(
            "{{MAIN_HOST}}|{{V4_HOST}}|{{V6_HOST}}",
            "<script>alert(1)</script>",
            "v4&\"'",
            "v6<>",
            Assets);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("v4&amp;&quot;&#x27;", html);
        Assert.Contains("v6&lt;&gt;", html);
    }

    [Fact]
    public void RenderHtml_ReplacesAssetUrlPlaceholders()
    {
        const string template = "<link href=\"{{ASSET_CSS_URL}}\"><script src=\"{{ASSET_JS_URL}}\"></script>";

        var html = Renderer.RenderHtml(template, new IpState("203.0.113.42", 4, 6), Assets);

        Assert.DoesNotContain("{{", html);
        Assert.Contains("href=\"/assets/site.css\"", html);
        Assert.Contains("src=\"/assets/site.js\"", html);
    }
}
