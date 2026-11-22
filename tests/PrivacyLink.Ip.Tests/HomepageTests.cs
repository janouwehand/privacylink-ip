using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class HomepageTests
{
    private const string Template =
        "<!doctype html><html><body>" +
        "<div id=\"v4\">{{INITIAL_IPV4}}</div>" +
        "<div id=\"v6\">{{INITIAL_IPV6}}</div>" +
        "<script type=\"application/json\" id=\"server-state\">{{SERVER_STATE}}</script>" +
        "</body></html>";

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        File.WriteAllText(Path.Combine(root, "Web", "index.html"), Template);
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
    public async Task Homepage_RendersInitialIpv4WithNoStore()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/", "ip.privacylink.nl", "203.0.113.42");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Contains("<div id=\"v4\">203.0.113.42</div>", html);
            Assert.Contains("<div id=\"v6\"></div>", html);
            Assert.Contains("\"initialVersion\":4", html);
            Assert.Contains("\"otherVersion\":6", html);
            Assert.False(response.Headers.Contains("ETag"));
            Assert.False(response.Content.Headers.Contains("Last-Modified"));
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task IndexHtml_AliasRendersTheSameTemplate()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/index.html", "ip.privacylink.nl", "2001:db8::1");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<div id=\"v6\">2001:db8::1</div>", html);
            Assert.DoesNotContain("{{", html);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task Homepage_PrivateRemoteAddressIsNotPresentedAsPublicIp()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/", "ip.privacylink.nl", "10.0.0.5");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("10.0.0.5", body);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task MissingTemplate_ReturnsNeutralNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/", "ip.privacylink.nl", "203.0.113.42");

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("homepage_not_available\n", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }
}
