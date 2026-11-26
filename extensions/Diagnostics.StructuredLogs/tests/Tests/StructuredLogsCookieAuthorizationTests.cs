using System.Net;
using System.Security.Claims;
using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.StructuredLogs.Authorization;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsCookieAuthorizationTests
{
    private const string CookieScheme = "structured-logs-cookie-test";

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("missing", HttpStatusCode.Forbidden)]
    [InlineData("allowed", HttpStatusCode.OK)]
    public async Task Native_event_stream_cookie_requests_receive_http_authorization_status(
        string? identity, HttpStatusCode expected)
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        await SignInAsync(client, identity);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, StructuredLogsCompatibilityCases.StreamPath);
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? "text/event-stream" : null,
            response.Content.Headers.ContentType?.MediaType);
        await cancellation.CancelAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public async Task Ordinary_browser_routes_keep_cookie_redirects(string? identity)
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        await SignInAsync(client, identity);
        using var response = await client.GetAsync("/test/browser-page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(identity is null ? "/test/login" : "/test/denied", response.Headers.Location?.AbsolutePath);
    }

    private static async Task SignInAsync(HttpClient client, string? identity)
    {
        if (identity is null)
            return;
        using var response = await client.GetAsync("/test/sign-in/" + identity);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }

    private static Task<IHost> StartHostAsync() => new HostBuilder()
        .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddElsaEndpoints();
            services.AddRouting();
            services.AddAuthentication(CookieScheme).AddCookie(CookieScheme, options =>
            {
                options.LoginPath = "/test/login";
                options.AccessDeniedPath = "/test/denied";
            });
            services.AddAuthorization();
            services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { CookieScheme });
            new StructuredLogsFeature().ConfigureServices(services);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                new StructuredLogsFeature().MapEndpoints(endpoints, null);
                endpoints.MapGet("/test/sign-in/{identity}", async context =>
                {
                    var claims = new List<Claim>
                    {
                        new(IdentityClaimTypes.Normalized, "v1"),
                        new(IdentityClaimTypes.Provider, "structured-logs-cookie-test")
                    };
                    if (context.Request.RouteValues["identity"]?.ToString() == "allowed")
                        claims.Add(new Claim(IdentityClaimTypes.Permission, StructuredLogsPermissions.Read));
                    await context.SignInAsync(CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme)));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                });
                endpoints.MapGet("/test/browser-page", () => "Private browser page")
                    .RequireAnyPermission(PermissionKey.Wildcard, StructuredLogsPermissions.Policy);
            });
        }))
        .StartAsync();
}
