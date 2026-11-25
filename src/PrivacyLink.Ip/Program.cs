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

// HTML template paths and trailing-slash aliases are not canonical. Redirect
// them permanently so search engines see one canonical URL per page instead of
// duplicate HTML at several paths. The v4/v6 API hostnames redirect the
// homepage and documentation to the main hostname.
app.Use((context, next) =>
{
    var hostKind = hosts.Classify(context.Request.Host.Host);
    var onFamilyHost = hostKind is HostKind.V4 or HostKind.V6;

    if (context.Request.Path == "/index.html")
    {
        RedirectPermanent(context, onFamilyHost ? $"https://{hosts.Main}/" : "/");
        return Task.CompletedTask;
    }

    if (context.Request.Path == "/index.nl.html" || context.Request.Path == "/nl/")
    {
        RedirectPermanent(context, onFamilyHost ? $"https://{hosts.Main}/nl" : "/nl");
        return Task.CompletedTask;
    }

    if (context.Request.Path == "/api.html" || context.Request.Path == "/api/")
    {
        RedirectPermanent(context, onFamilyHost ? $"https://{hosts.Main}/api" : "/api");
        return Task.CompletedTask;
    }

    return next();
});

var assetRoot = Path.Combine(app.Environment.ContentRootPath, "Web", "assets");
if (Directory.Exists(assetRoot))
{
    var assetProvider = new PhysicalFileProvider(assetRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = assetProvider,
        RequestPath = "/assets",
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

app.MapMethods("/robots.txt", ["GET", "HEAD"], HandleRobots);

app.MapMethods("/sitemap.xml", ["GET", "HEAD"], HandleSitemap);

app.MapMethods("/healthz", ["GET", "HEAD"], HandleHealth);

app.MapMethods("/api/ip", ["GET", "HEAD"], HandleIpPlain);

app.MapMethods("/api/ip.json", ["GET", "HEAD"], HandleIpJson);

app.MapMethods("/api", ["GET", "HEAD"], HandleApiDocs);

app.MapMethods("/api.html", ["GET", "HEAD"], HandleApiDocs);

app.MapMethods("/", ["GET", "HEAD"], HandleHomepage);

app.MapMethods("/index.html", ["GET", "HEAD"], HandleHomepage);

app.MapMethods("/nl", ["GET", "HEAD"], HandleDutchHomepage);

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
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync("ok\n");
    }
}

async Task HandleRobots(HttpContext context)
{
    var hostKind = hosts.Classify(context.Request.Host.Host);

    // Only the main hostname is a search destination. The v4/v6 hostnames are
    // API transport endpoints; keep crawlers away from duplicate documentation
    // and raw address responses there.
    var body = hostKind == HostKind.Main
        ? $"User-agent: *\nAllow: /\nDisallow: /healthz\n\nSitemap: https://{hosts.Main}/sitemap.xml\n"
        : "User-agent: *\nDisallow: /\n";

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/plain; charset=utf-8";
    context.Response.Headers.CacheControl = "public, max-age=3600";

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync(body, context.RequestAborted);
    }
}

async Task HandleSitemap(HttpContext context)
{
    var mainHost = System.Security.SecurityElement.Escape(hosts.Main);
    var body =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n" +
        $"  <url><loc>https://{mainHost}/</loc></url>\n" +
        $"  <url><loc>https://{mainHost}/nl</loc></url>\n" +
        $"  <url><loc>https://{mainHost}/api</loc></url>\n" +
        "</urlset>\n";

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "application/xml; charset=utf-8";
    context.Response.Headers.CacheControl = "public, max-age=3600";

    if (!HttpMethods.IsHead(context.Request.Method))
    {
        await context.Response.WriteAsync(body, context.RequestAborted);
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

    // Raw address responses are data, not search results.
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

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
    if (hosts.Classify(context.Request.Host.Host) is HostKind.V4 or HostKind.V6)
    {
        RedirectPermanent(context, $"https://{hosts.Main}/api");
        return;
    }

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

Task HandleHomepage(HttpContext context) => HandleHomepageAsync(context, "index.html", "/");

Task HandleDutchHomepage(HttpContext context) => HandleHomepageAsync(context, "index.nl.html", "/nl");

async Task HandleHomepageAsync(HttpContext context, string templateFile, string canonicalPath)
{
    var hostKind = hosts.Classify(context.Request.Host.Host);
    if (hostKind == HostKind.Unknown)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "invalid_host", json: false);
        return;
    }

    // The homepage only exists on the main hostname. The family hostnames are
    // part of the API, not separate search destinations.
    if (hostKind is HostKind.V4 or HostKind.V6)
    {
        RedirectPermanent(context, $"https://{hosts.Main}{canonicalPath}");
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

    var templatePath = Path.Combine(app.Environment.ContentRootPath, "Web", templateFile);
    if (!File.Exists(templatePath))
    {
        app.Logger.LogWarning("Homepage template is missing");
        await WriteErrorAsync(context, StatusCodes.Status404NotFound, "homepage_not_available", json: false);
        return;
    }

    var template = await File.ReadAllTextAsync(templatePath, context.RequestAborted);
    var model = new IpState(address.ToString(), version, version == 4 ? 6 : 4);
    var html = renderer.RenderHtml(template, model, hosts.Main, hosts.V4, hosts.V6, assets);

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

static void RedirectPermanent(HttpContext context, string target)
{
    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
    context.Response.Headers.Location = target + context.Request.QueryString.Value;
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
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

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
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

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
