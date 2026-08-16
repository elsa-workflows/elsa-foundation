using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryAuthorizationBoundaryTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("exact", 200)]
    [InlineData("legacy", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("resource-allow", 200)]
    [InlineData("implied", 403)]
    [InlineData("untrusted", 401)]
    [InlineData("invalid-marker", 401)]
    [InlineData("ambiguous", 401)]
    [InlineData("resource-deny", 403)]
    [InlineData("tenant-mismatch", 403)]
    public async Task Minimal_and_retained_fastendpoints_routes_share_authentication_and_resource_boundaries(string? identity, int expectedStatus)
    {
        using var host = await StartHostAsync();
        using var minimal = await SendAsync(host, "/diagnostics/opentelemetry/storage", identity);
        using var fastEndpoints = await SendAsync(host, "/_wave5/fe-canary", identity);

        Assert.Equal(expectedStatus, (int)minimal.StatusCode);
        Assert.Equal(expectedStatus, (int)fastEndpoints.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(IHost host, string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.Add("X-Test-Identity", identity);
        return host.GetTestClient().SendAsync(request);
    }

    private static async Task<IHost> StartHostAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(OpenTelemetryFastEndpointCanary).Assembly];
                    options.Filter = type => type == typeof(OpenTelemetryFastEndpointCanary);
                });
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "otel-boundary";
                    options.DefaultChallengeScheme = "otel-boundary";
                }).AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>("otel-boundary", _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "otel-boundary" });
                services.AddScoped<IPermissionResourceHandler, BoundaryResourceHandler>();
                new OpenTelemetryFeature().ConfigureServices(services);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    new OpenTelemetryFeature().MapEndpoints(endpoints, null);
                    endpoints.MapFastEndpoints();
                });
            });
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class BoundaryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permission = identity switch
            {
                "exact" or "resource-deny" or "tenant-mismatch" => OpenTelemetryPermissions.Read,
                "legacy" => OpenTelemetryPermissions.LegacyPolicy,
                "wildcard" => PermissionKey.Wildcard,
                "resource-allow" => string.Empty,
                "implied" => "Diagnostics",
                _ => "Diagnostics:Other"
            };
            var authenticationType = identity == "untrusted" ? "other-authentication" : Scheme.Name;
            var normalized = identity == "invalid-marker" ? "invalid" : "v1";
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, normalized),
                new(IdentityClaimTypes.TenantId, identity == "tenant-mismatch" ? "tenant-a" : "tenant-a"),
                new("resource", identity switch
                {
                    "resource-allow" => "allow",
                    "resource-deny" => "deny",
                    "tenant-mismatch" => "tenant-b",
                    _ => string.Empty
                })
            };
            if (!string.IsNullOrWhiteSpace(permission))
                claims.Add(new Claim(IdentityClaimTypes.Permission, permission));

            if (identity == "ambiguous")
            {
                var first = new ClaimsIdentity(claims, Scheme.Name);
                var second = new ClaimsIdentity(claims, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal([first, second]), Scheme.Name)));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }

    private sealed class BoundaryResourceHandler : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var marker = context.Principal.FindFirst("resource")?.Value;
            return ValueTask.FromResult<PermissionEvaluationResult?>(marker switch
            {
                "allow" => PermissionEvaluationResult.Success,
                "deny" or "tenant-b" => PermissionEvaluationResult.Denied("resource denied"),
                _ => null
            });
        }
    }
}
