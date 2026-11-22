using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrivacyLink.Ip.Tests.Infrastructure;

/// <summary>
/// Configures the application under test with explicit allowlists and a
/// test-only startup filter that controls the connection remote address.
/// </summary>
public sealed class PrivacyLinkAppFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly string? _contentRoot;

    public PrivacyLinkAppFactory(
        IDictionary<string, string?>? settings = null,
        string? contentRoot = null)
    {
        _settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                _settings[pair.Key] = pair.Value;
            }
        }

        _contentRoot = contentRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_contentRoot is not null)
        {
            builder.UseContentRoot(_contentRoot);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var defaults = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Hosts:Main"] = "ip.privacylink.nl",
                ["Hosts:V4"] = "v4.ip.privacylink.nl",
                ["Hosts:V6"] = "v6.ip.privacylink.nl",
                ["Hosts:MainAliases:0"] = "localhost",
                ["Cors:AllowedOrigin"] = "https://ip.privacylink.nl",
                ["Https:Redirect"] = "false"
            };

            foreach (var pair in _settings)
            {
                defaults[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(defaults);
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    public HttpClient CreateAppClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });
}

/// <summary>Shared request helpers for integration tests.</summary>
public static class Requests
{
    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string host,
        string? remoteIp = null,
        string? forwardedFor = null,
        string? origin = null,
        string method = "GET",
        string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = host;

        if (remoteIp is not null)
        {
            TestRemoteIp.Set(request, remoteIp);
        }

        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return client.SendAsync(request);
    }
}
