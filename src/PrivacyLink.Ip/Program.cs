using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using PrivacyLink.Ip.Services;
using PrivacyLink.Ip.Web;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

builder.Services.AddSingleton<ClientIpResolver>();
builder.Services.AddSingleton<HostAllowlist>();
builder.Services.AddSingleton<SiteRenderer>();
builder.Services.AddSingleton(serviceProvider =>
{
    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
    return new AssetManifest(Path.Combine(environment.ContentRootPath, "Web", "assets"));
});

builder.Services.Configure<ForwardedHeadersOptions>(options => ConfigureForwardedHeaders(options, configuration));

var app = builder.Build();

var resolver = app.Services.GetRequiredService<ClientIpResolver>();
var hosts = app.Services.GetRequiredService<HostAllowlist>();
var renderer = app.Services.GetRequiredService<SiteRenderer>();
var assets = app.Services.GetRequiredService<AssetManifest>();

var allowedOrigin = configuration["Cors:AllowedOrigin"] ?? "https://ip.privacylink.nl";
var csp = string.Join(
    "; ",
    "default-src 'self'",
    "base-uri 'none'",
    "frame-ancestors 'none'",
    "form-action 'self'",
    "img-src 'self' data:",
    "script-src 'self'",
    "style-src 'self'",
    $"connect-src 'self' https://{hosts.V4} https://{hosts.V6}");

// Trust forwarded headers only from explicitly configured Caddy proxy
// addresses/networks. Must run before anything reads Connection.RemoteIpAddress.
app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp => errorApp.Run(HandleUnhandledError));

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    headers["Content-Security-Policy"] = csp;

    if (context.Request.IsHttps)
    {
        // Per-hostname HSTS, deliberately without includeSubDomains or preload.
        headers["Strict-Transport-Security"] = "max-age=31536000";
    }

    await next();
});

// Reject unknown Host headers before static files or endpoints. /healthz stays
// reachable on the internal container address for the Docker healthcheck.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/healthz")
        && hosts.Classify(context.Request.Host.Host) == HostKind.Unknown)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "invalid_host", json: false);
        return;
    }

    await next();
});

// HTTP -> HTTPS redirect for known hostnames. Caddy normally terminates TLS,
// so this mainly protects direct/internal HTTP access. Open redirects are
// avoided by only redirecting hostnames that pass the exact allowlist. The
// redirect is only enabled when a trusted proxy is configured: otherwise an
// untrusted X-Forwarded-Proto would be ignored and the redirect could loop.
var hasTrustedProxyConfiguration =
    (configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()?.Length ?? 0) > 0
    || (configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()?.Length ?? 0) > 0;

if (configuration.GetValue("Https:Redirect", true) && hasTrustedProxyConfiguration && !app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps && hosts.Classify(context.Request.Host.Host) != HostKind.Unknown)
        {
            var target = $"https://{context.Request.Host.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = target;
            return;
        }

        await next();
    });
}

// /index.html is rendered by the homepage endpoint (placeholders must be
// replaced), never served as a raw static file. /api.html is rewritten to the
// API documentation endpoint for the same reason.
app.Use((context, next) =>
{
    if (context.Request.Path == "/index.html")
    {
        context.Request.Path = "/";
    }
    else if (context.Request.Path == "/api.html")
    {
        context.Request.Path = "/api";
    }

    return next();
});

var webRoot = Path.Combine(app.Environment.ContentRootPath, "Web");
if (Directory.Exists(webRoot))
{
    var webProvider = new PhysicalFileProvider(webRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = webProvider,
        OnPrepareResponse = staticFileContext =>
        {
            var context = staticFileContext.Context;
            var path = context.Request.Path.Value ?? string.Empty;
            var version = context.Request.Query["v"].ToString();

            // Only the current content-hashed URL may be cached immutably.
            // Stale or unversioned URLs must revalidate, so a deploy is visible
            // immediately instead of after a max-age window.
            context.Response.Headers.CacheControl = assets.IsCurrentVersion(path, version)
                ? "public, max-age=31536000, immutable"
                : "public, no-cache";
        }
    });
}

app.MapMethods("/healthz", ["GET", "HEAD"], HandleHealth);

app.MapMethods("/api/ip", ["GET", "HEAD"], HandleIpPlain);

app.MapMethods("/api/ip.json", ["GET", "HEAD"], HandleIpJson);

app.MapMethods("/api", ["GET", "HEAD"], HandleApiDocs);

app.MapMethods("/api.html", ["GET", "HEAD"], HandleApiDocs);

app.MapMethods("/", ["GET", "HEAD"], HandleHomepage);

app.MapMethods("/index.html", ["GET", "HEAD"], HandleHomepage);

app.Run();

void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IConfiguration config)
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = config.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;

    foreach (var value in config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(value, out var proxy))
        {
            options.KnownProxies.Add(proxy);
        }
    }

    foreach (var value in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        if (System.Net.IPNetwork.TryParse(value, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}

async Task HandleHealth(HttpContext context)
{
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/plain; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync("ok\n");
    }
}

Task HandleIpPlain(HttpContext context) => HandleIpAsync(context, json: false);

Task HandleIpJson(HttpContext context) => HandleIpAsync(context, json: true);

async Task HandleIpAsync(HttpContext context, bool json)
{
    var hostKind = hosts.Classify(context.Request.Host.Host);
    if (hostKind == HostKind.Unknown)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "invalid_host", json);
        return;
    }

    ApplyCors(context, hostKind);

    var resolution = resolver.Resolve(context);
    if (!resolution.Success)
    {
        app.Logger.LogWarning("IP resolution failed: {Reason}", resolution.FailureReason);
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "ip_unavailable", json);
        return;
    }

    var address = resolution.Address!;
    var version = ClientIpResolver.GetVersion(address);

    if ((hostKind == HostKind.V4 && version != 4) || (hostKind == HostKind.V6 && version != 6))
    {
        app.Logger.LogWarning("IP family mismatch for {HostKind} hostname", hostKind);
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "ip_family_mismatch", json);
        return;
    }

    var ip = address.ToString();
    context.Response.StatusCode = StatusCodes.Status200OK;
    NoStore(context);

    if (json)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(new IpResponse(ip, version)));
        }
    }
    else
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync(ip + "\n");
        }
    }
}

async Task HandleApiDocs(HttpContext context)
{
    // The page contains no visitor-specific data, so it is cacheable and does
    // not need client IP resolution or IP-family gating. Unknown Host headers
    // are already rejected by the global allowlist middleware.
    var templatePath = Path.Combine(app.Environment.ContentRootPath, "Web", "api.html");
    if (!File.Exists(templatePath))
    {
        app.Logger.LogWarning("API documentation template is missing");
        await WriteErrorAsync(context, StatusCodes.Status404NotFound, "api_docs_not_available", json: false);
        return;
    }

    var template = await File.ReadAllTextAsync(templatePath, context.RequestAborted);
    var html = renderer.RenderDocs(template, hosts.Main, hosts.V4, hosts.V6, assets);
    var entityTag = CreateWeakEntityTag(html);

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "public, no-cache";
    context.Response.Headers.ETag = entityTag;

    if (MatchesEntityTag(context.Request.Headers.IfNoneMatch.ToString(), entityTag))
    {
        context.Response.StatusCode = StatusCodes.Status304NotModified;
        return;
    }

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}

async Task HandleHomepage(HttpContext context)
{
    var hostKind = hosts.Classify(context.Request.Host.Host);
    if (hostKind == HostKind.Unknown)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "invalid_host", json: false);
        return;
    }

    var resolution = resolver.Resolve(context);
    if (!resolution.Success)
    {
        app.Logger.LogWarning("IP resolution failed for homepage: {Reason}", resolution.FailureReason);
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "ip_unavailable", json: false);
        return;
    }

    var address = resolution.Address!;
    var version = ClientIpResolver.GetVersion(address);

    if ((hostKind == HostKind.V4 && version != 4) || (hostKind == HostKind.V6 && version != 6))
    {
        app.Logger.LogWarning("IP family mismatch for homepage on {HostKind} hostname", hostKind);
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "ip_family_mismatch", json: false);
        return;
    }

    var templatePath = Path.Combine(app.Environment.ContentRootPath, "Web", "index.html");
    if (!File.Exists(templatePath))
    {
        app.Logger.LogWarning("Homepage template is missing");
        await WriteErrorAsync(context, StatusCodes.Status404NotFound, "homepage_not_available", json: false);
        return;
    }

    var template = await File.ReadAllTextAsync(templatePath, context.RequestAborted);
    var model = new IpState(address.ToString(), version, version == 4 ? 6 : 4);
    var html = renderer.RenderHtml(template, model, assets);

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/html; charset=utf-8";
    NoStore(context);

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}

void ApplyCors(HttpContext context, HostKind hostKind)
{
    if (hostKind is HostKind.V4 or HostKind.V6)
    {
        context.Response.Headers.AccessControlAllowOrigin = allowedOrigin;
    }
}

void NoStore(HttpContext context)
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Remove("ETag");
    context.Response.Headers.Remove("Last-Modified");
}

// Cache validators are weak because a shared/edge cache may re-encode the body.
static string CreateWeakEntityTag(string content)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
    return $"W/\"{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}\"";
}

static bool MatchesEntityTag(string ifNoneMatch, string entityTag)
{
    if (string.IsNullOrEmpty(ifNoneMatch))
    {
        return false;
    }

    foreach (var candidate in ifNoneMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (candidate == "*" || string.Equals(candidate, entityTag, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

async Task WriteErrorAsync(HttpContext context, int statusCode, string errorCode, bool json)
{
    context.Response.StatusCode = statusCode;
    NoStore(context);

    // Error responses never echo request data (no IP, host or headers).
    if (json)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = errorCode }));
        }
    }
    else
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync(errorCode + "\n");
        }
    }
}

async Task HandleUnhandledError(HttpContext context)
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/plain; charset=utf-8";
    NoStore(context);

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync("internal_error\n");
    }
}

/// <summary>Exact JSON response shape for <c>/api/ip.json</c>.</summary>
internal sealed record IpResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("ip")] string Ip,
    [property: System.Text.Json.Serialization.JsonPropertyName("version")] int Version);

/// <summary>Entry point marker used by the integration test host.</summary>
public partial class Program
{
}
