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
/// Renders the static HTML template by replacing exactly three placeholders.
/// The renderer is intentionally tiny and allocation-friendly, and all
/// model-derived values are encoded before insertion.
/// </summary>
public sealed class SiteRenderer
{
    private static readonly JsonSerializerOptions ServerStateJsonOptions = new(JsonSerializerDefaults.Web);

    public string RenderHtml(string template, IpState model)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(model);

        var ipv4 = model.InitialVersion == 4 ? model.InitialIp : string.Empty;
        var ipv6 = model.InitialVersion == 6 ? model.InitialIp : string.Empty;

        return template
            .Replace("{{INITIAL_IPV4}}", HtmlEncoder.Default.Encode(ipv4), StringComparison.Ordinal)
            .Replace("{{INITIAL_IPV6}}", HtmlEncoder.Default.Encode(ipv6), StringComparison.Ordinal)
            .Replace("{{SERVER_STATE}}", JsonSerializer.Serialize(model, ServerStateJsonOptions), StringComparison.Ordinal);
    }
}
