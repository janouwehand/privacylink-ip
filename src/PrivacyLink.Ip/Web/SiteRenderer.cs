using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivacyLink.Ip.Web;

/// <summary>Server-side model inserted into the HTML template.</summary>
public sealed record IpState(
    [property: JsonPropertyName("initialIp")] string InitialIp,
    [property: JsonPropertyName("initialVersion")] int InitialVersion,
    [property: JsonPropertyName("otherVersion")] int OtherVersion);

/// <summary>
/// Renders the static HTML template by replacing the hostname, IP, server-state
/// and content-hashed asset placeholders. The renderer is intentionally tiny
/// and allocation-friendly, and all inserted values are encoded before insertion.
/// </summary>
public sealed class SiteRenderer
{
    private static readonly JsonSerializerOptions ServerStateJsonOptions = new(JsonSerializerDefaults.Web);

    public string RenderHtml(
        string template,
        IpState model,
        string mainHost,
        string v4Host,
        string v6Host,
        AssetManifest assets)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mainHost);
        ArgumentNullException.ThrowIfNull(v4Host);
        ArgumentNullException.ThrowIfNull(v6Host);
        ArgumentNullException.ThrowIfNull(assets);

        var ipv4 = model.InitialVersion == 4 ? model.InitialIp : string.Empty;
        var ipv6 = model.InitialVersion == 6 ? model.InitialIp : string.Empty;

        return template
            .Replace("{{MAIN_HOST}}", HtmlEncoder.Default.Encode(mainHost), StringComparison.Ordinal)
            .Replace("{{V4_HOST}}", HtmlEncoder.Default.Encode(v4Host), StringComparison.Ordinal)
            .Replace("{{V6_HOST}}", HtmlEncoder.Default.Encode(v6Host), StringComparison.Ordinal)
            .Replace("{{INITIAL_IPV4}}", HtmlEncoder.Default.Encode(ipv4), StringComparison.Ordinal)
            .Replace("{{INITIAL_IPV6}}", HtmlEncoder.Default.Encode(ipv6), StringComparison.Ordinal)
            .Replace("{{SERVER_STATE}}", JsonSerializer.Serialize(model, ServerStateJsonOptions), StringComparison.Ordinal)
            .Replace("{{ASSET_CSS_URL}}", HtmlEncoder.Default.Encode(assets.CssUrl), StringComparison.Ordinal)
            .Replace("{{ASSET_JS_URL}}", HtmlEncoder.Default.Encode(assets.JsUrl), StringComparison.Ordinal);
    }

    /// <summary>
    /// Renders the API documentation template by replacing the hostname and
    /// content-hashed asset placeholders. All values are encoded before insertion.
    /// </summary>
    public string RenderDocs(string template, string mainHost, string v4Host, string v6Host, AssetManifest assets)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(mainHost);
        ArgumentNullException.ThrowIfNull(v4Host);
        ArgumentNullException.ThrowIfNull(v6Host);
        ArgumentNullException.ThrowIfNull(assets);

        return template
            .Replace("{{MAIN_HOST}}", HtmlEncoder.Default.Encode(mainHost), StringComparison.Ordinal)
            .Replace("{{V4_HOST}}", HtmlEncoder.Default.Encode(v4Host), StringComparison.Ordinal)
            .Replace("{{V6_HOST}}", HtmlEncoder.Default.Encode(v6Host), StringComparison.Ordinal)
            .Replace("{{ASSET_CSS_URL}}", HtmlEncoder.Default.Encode(assets.CssUrl), StringComparison.Ordinal)
            .Replace("{{ASSET_JS_URL}}", HtmlEncoder.Default.Encode(assets.JsUrl), StringComparison.Ordinal);
    }
}
