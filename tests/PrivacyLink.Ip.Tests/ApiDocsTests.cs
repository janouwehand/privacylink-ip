using PrivacyLink.Ip.Tests.Infrastructure;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class ApiDocsTests
{
    private const string MainHost = "ip.privacylink.nl";
    private const string V4Host = "v4.ip.privacylink.nl";
    private const string V6Host = "v6.ip.privacylink.nl";

    private const string Template =
        "<!doctype html><html><body>" +
        "<h1>{{MAIN_HOST}}</h1>" +
        "<p><a href=\"https://{{V4_HOST}}/api/ip\">{{V4_HOST}}</a></p>" +
        "<p><a href=\"https://{{V6_HOST}}/api/ip\">{{V6_HOST}}</a></p>" +
        "<p>Which address do I get?</p>" +
        "<pre>curl.exe https://{{V4_HOST}}/api/ip</pre>" +
        "<p>ip_family_mismatch</p>" +
        "<footer>{{MAIN_HOST}}{{MAIN_HOST}}</footer>" +
        "</body></html>";

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        File.WriteAllText(Path.Combine(root, "Web", "api.html"), Template);
        return root;
    }

    private static string CreateContentRootWithoutDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
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
    public async Task Api_ReturnsRenderedDocsWithRevalidatingPublicCache()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api", MainHost);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("public, no-cache", response.Headers.CacheControl?.ToString());
            Assert.NotNull(response.Headers.ETag);

            Assert.DoesNotContain("{{", html);
            Assert.Contains(V4Host, html);
            Assert.Contains(V6Host, html);
            Assert.Contains(MainHost, html);
            Assert.Contains("Which address do I get?", html);
            Assert.Contains("curl.exe", html);
            Assert.Contains("ip_family_mismatch", html);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task Api_WithTrailingSlash_ReturnsTheSameRenderedDocs()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api/", MainHost);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.DoesNotContain("{{", html);
            Assert.Contains("Which address do I get?", html);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task ApiHtmlAlias_IsRewrittenAndRendered()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api.html", MainHost);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.DoesNotContain("{{", html);
            Assert.Contains("curl.exe", html);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task Api_RejectsOtherMethodsWithMethodNotAllowed()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api", MainHost, method: "POST");

            Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task MissingDocsTemplate_ReturnsNeutralNotFound()
    {
        var root = CreateContentRootWithoutDocs();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api", MainHost);

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("api_docs_not_available\n", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task Head_ReturnsHeadersWithoutBody()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var response = await Requests.GetAsync(client, "/api", MainHost, method: "HEAD");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Assert.Equal("public, no-cache", response.Headers.CacheControl?.ToString());
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task Api_ReturnsNotModifiedWhenEntityTagMatches()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var initial = await Requests.GetAsync(client, "/api", MainHost);
            var entityTag = initial.Headers.ETag?.ToString();
            Assert.NotNull(entityTag);

            using var response = await Requests.GetAsync(client, "/api", MainHost, ifNoneMatch: entityTag);

            Assert.Equal(System.Net.HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal("public, no-cache", response.Headers.CacheControl?.ToString());
            Assert.Equal(entityTag, response.Headers.ETag?.ToString());
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }
}
