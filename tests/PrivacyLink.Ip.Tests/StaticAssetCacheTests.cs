using System.Text.RegularExpressions;
using PrivacyLink.Ip.Tests.Infrastructure;
using PrivacyLink.Ip.Web;
using Xunit;

namespace PrivacyLink.Ip.Tests;

public sealed class StaticAssetCacheTests
{
    private const string MainHost = "ip.privacylink.nl";

    private const string Template =
        "<!doctype html><html><head>" +
        "<link rel=\"stylesheet\" href=\"{{ASSET_CSS_URL}}\">" +
        "<script src=\"{{ASSET_JS_URL}}\" defer></script>" +
        "</head><body>" +
        "<div>{{INITIAL_IPV4}}</div>" +
        "<div>{{INITIAL_IPV6}}</div>" +
        "<script type=\"application/json\">{{SERVER_STATE}}</script>" +
        "</body></html>";

    private static string CreateContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "Web", "assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(root, "Web", "index.html"), Template);
        File.WriteAllText(Path.Combine(assets, "site.css"), "body{color:#111}");
        File.WriteAllText(Path.Combine(assets, "site.js"), "void 0;");
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
    public async Task VersionedAsset_IsImmutable_AndOtherUrlsRevalidate()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var page = await Requests.GetAsync(client, "/", MainHost, "203.0.113.42");
            var html = await page.Content.ReadAsStringAsync();
            var match = Regex.Match(html, "/assets/site\\.css\\?v=(?<version>[0-9a-f]{16})");
            Assert.True(match.Success, html);
            var version = match.Groups["version"].Value;

            using var current = await Requests.GetAsync(client, $"/assets/site.css?v={version}", MainHost);
            Assert.Equal(System.Net.HttpStatusCode.OK, current.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", current.Headers.CacheControl?.ToString());
            Assert.Equal("body{color:#111}", await current.Content.ReadAsStringAsync());

            using var unversioned = await Requests.GetAsync(client, "/assets/site.css", MainHost);
            Assert.Equal("public, no-cache", unversioned.Headers.CacheControl?.ToString());
            Assert.NotNull(unversioned.Headers.ETag);

            using var stale = await Requests.GetAsync(client, "/assets/site.css?v=0000000000000000", MainHost);
            Assert.Equal("public, no-cache", stale.Headers.CacheControl?.ToString());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public async Task UnversionedAsset_ReturnsNotModifiedWhenEntityTagMatches()
    {
        var root = CreateContentRoot();
        try
        {
            using var factory = new PrivacyLinkAppFactory(contentRoot: root);
            using var client = factory.CreateAppClient();

            using var initial = await Requests.GetAsync(client, "/assets/site.css", MainHost);
            var entityTag = initial.Headers.ETag?.ToString();
            Assert.NotNull(entityTag);

            using var response = await Requests.GetAsync(
                client,
                "/assets/site.css",
                MainHost,
                ifNoneMatch: entityTag);

            Assert.Equal(System.Net.HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            DeleteContentRoot(root);
        }
    }

    [Fact]
    public void Manifest_ChangesVersionWhenContentChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacylink-ip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var css = Path.Combine(root, "site.css");
            File.WriteAllText(css, "body{color:#111}");
            File.WriteAllText(Path.Combine(root, "site.js"), "void 0;");

            var first = new AssetManifest(root);
            var firstVersion = first.CssUrl.Split("?v=")[1];

            Assert.True(first.IsCurrentVersion("/assets/site.css", firstVersion));
            Assert.False(first.IsCurrentVersion("/assets/site.css", "0000000000000000"));
            Assert.False(first.IsCurrentVersion("/assets/other.css", firstVersion));

            File.WriteAllText(css, "body{color:#222}");
            var second = new AssetManifest(root);

            Assert.NotEqual(first.CssUrl, second.CssUrl);
            Assert.False(second.IsCurrentVersion("/assets/site.css", firstVersion));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
