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

    [Fact]
    public void RenderHtml_FillsOnlyTheMatchingFamilyPlaceholder()
    {
        var html = Renderer.RenderHtml(Template, new IpState("203.0.113.42", 4, 6));

        Assert.Contains("<div id=\"v4\">203.0.113.42</div>", html);
        Assert.Contains("<div id=\"v6\"></div>", html);
    }

    [Fact]
    public void RenderHtml_FillsIpv6AndLeavesIpv4Empty()
    {
        var html = Renderer.RenderHtml(Template, new IpState("2001:db8::1", 6, 4));

        Assert.Contains("<div id=\"v4\"></div>", html);
        Assert.Contains("<div id=\"v6\">2001:db8::1</div>", html);
    }

    [Fact]
    public void RenderHtml_EncodesHtmlInjectionInModelValues()
    {
        var html = Renderer.RenderHtml(Template, new IpState("<script>alert(1)</script>&\"'", 4, 6));

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("203.0.113.42", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&amp;&quot;&#x27;", html);
    }

    [Fact]
    public void RenderHtml_WritesExactServerStateShape()
    {
        var html = Renderer.RenderHtml(Template, new IpState("203.0.113.42", 4, 6));

        Assert.Contains(
            "<script type=\"application/json\" id=\"state\">{\"initialIp\":\"203.0.113.42\",\"initialVersion\":4,\"otherVersion\":6}</script>",
            html);
    }

    [Fact]
    public void RenderHtml_ReplacesEveryPlaceholderOccurrence()
    {
        const string template = "{{INITIAL_IPV4}}-{{INITIAL_IPV4}}{{SERVER_STATE}}{{SERVER_STATE}}";

        var html = Renderer.RenderHtml(template, new IpState("203.0.113.42", 4, 6));

        Assert.DoesNotContain("{{", html);
    }
}
