using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace PrivacyLink.Ip.Tests.Infrastructure;

/// <summary>
/// Test-only middleware that sets the connection remote address before the
/// forwarded-header middleware runs. This lets integration tests simulate both
/// direct clients and trusted/untrusted proxies.
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}

/// <summary>Re-exported for convenience; nothing else is required here.</summary>
public static class TestRemoteIp
{
    public static void Set(HttpRequestMessage request, string address)
    {
        request.Headers.TryAddWithoutValidation(TestRemoteIpStartupFilter.HeaderName, address);
    }
}
