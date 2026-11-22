using System.Security.Cryptography;

namespace PrivacyLink.Ip.Web;

/// <summary>
/// Content-hash versions for the static assets referenced by the HTML
/// templates. A changed file produces a different URL, so the current URL can
/// be cached immutably while stale URLs are safely revalidated.
/// </summary>
public sealed class AssetManifest
{
    private readonly Dictionary<string, string?> _versions = new(StringComparer.OrdinalIgnoreCase);

    public AssetManifest(string assetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);

        CssUrl = CreateUrl(assetsDirectory, "site.css");
        JsUrl = CreateUrl(assetsDirectory, "site.js");
    }

    /// <summary>Versioned URL for the site stylesheet.</summary>
    public string CssUrl { get; }

    /// <summary>Versioned URL for the site script.</summary>
    public string JsUrl { get; }

    /// <summary>
    /// True when <paramref name="version"/> is the current content hash for the
    /// requested asset path, so the response may be cached immutably.
    /// </summary>
    public bool IsCurrentVersion(string path, string? version)
    {
        return version is { Length: > 0 }
            && _versions.TryGetValue(path, out var current)
            && string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
    }

    private string CreateUrl(string assetsDirectory, string fileName)
    {
        var path = $"/assets/{fileName}";
        var filePath = Path.Combine(assetsDirectory, fileName);
        if (!File.Exists(filePath))
        {
            _versions[path] = null;
            return path;
        }

        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        var version = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
        _versions[path] = version;
        return $"{path}?v={version}";
    }
}
