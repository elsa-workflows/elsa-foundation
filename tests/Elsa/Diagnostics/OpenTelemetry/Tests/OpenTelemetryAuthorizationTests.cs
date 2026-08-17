using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry;
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
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryAuthorizationTests
{
    private const string Permission = "DIAGNOSTICS:OPENTELEMETRY.READ";

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("lacking", HttpStatusCode.Forbidden)]
    [InlineData("exact", HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    [InlineData("legacy", HttpStatusCode.OK)]
    [InlineData("implied", HttpStatusCode.Forbidden)]
    [InlineData("tenant", HttpStatusCode.OK)]
    [InlineData("resource", HttpStatusCode.OK)]
    public async Task Query_endpoint_uses_the_same_permission_evaluator_for_mixed_host(string? identity, HttpStatusCode expected)
    {
        using var host = await StartHostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/opentelemetry/storage");
        if (identity is not null)
            request.Headers.Add("X-Test-Identity", identity);

        using var response = await host.GetTestClient().SendAsync(request);

        Assert.Equal(expected, response.StatusCode);

        if (identity is "exact" or "wildcard" or "legacy")
        {
            using var canaryRequest = new HttpRequestMessage(HttpMethod.Get, "/_wave5/fe-canary");
            canaryRequest.Headers.Add("X-Test-Identity", identity);
            using var canaryResponse = await host.GetTestClient().SendAsync(canaryRequest);
            Assert.Equal(HttpStatusCode.OK, canaryResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Authorized_query_binder_exercises_success_and_malformed_json_paths()
    {
        using var host = await StartHostAsync();
        using var success = new HttpRequestMessage(HttpMethod.Post, "/diagnostics/opentelemetry/resources/search")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        success.Headers.Add("X-Test-Identity", "exact");
        using var successResponse = await host.GetTestClient().SendAsync(success);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        Assert.Contains("items", await successResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/diagnostics/opentelemetry/resources/search")
        {
            Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json")
        };
        malformed.Headers.Add("X-Test-Identity", "exact");
        using var malformedResponse = await host.GetTestClient().SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
    }

    private static async Task<IHost> StartHostAsync()
    {
        var host = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddFastEndpoints();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "test";
                    options.DefaultChallengeScheme = "test";
                }).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
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
        }).Build();
        await host.StartAsync();
        return host;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["X-Test-Identity"].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var permission = identity switch
            {
                "exact" or "tenant" or "resource" => Permission,
                "wildcard" => PermissionKey.Wildcard,
                "legacy" => "DIAGNOSTICS:OPENTELEMETRY",
                "implied" => "DIAGNOSTICS",
                _ => "DIAGNOSTICS:OTHER"
            };
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Permission, permission),
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.Provider, "test"),
                new(IdentityClaimTypes.TenantId, identity == "tenant" ? "tenant-a" : "global")
            };
            if (identity == "tenant")
                claims.Add(new Claim(IdentityClaimTypes.TenantId, "tenant-a"));
            if (identity == "resource")
                claims.Add(new Claim("resource", "telemetry-a"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
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

}

internal sealed class OpenTelemetryFastEndpointCanary : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/_wave5/fe-canary");
        ConfigurePermissions(OpenTelemetryPermissions.Read);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) => Send.OkAsync("fe-canary", cancellationToken);
}
