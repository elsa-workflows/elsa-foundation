using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Shared HTTP host for the <c>GET /_elsa/identity/token</c> tests (plan C2). Composes the three landed
/// workstreams end to end over private in-memory databases: the ASP.NET Core Identity substrate + cookie
/// sign-in + <c>POST /_elsa/identity/login</c> (A), the OpenIddict issuance + bearer validation scheme (B),
/// and the Api FastEndpoints hosting the token endpoint (C). A single self-contained <c>/protected</c>
/// endpoint (default scheme = the OpenIddict selector) proves an issued bearer authenticates onward.
/// Disposed via <see cref="IAsyncDisposable"/> so each test class tears the host down.
/// </summary>
public sealed class TokenEndpointFixture : IAsyncDisposable
{
    private readonly IHost _host;

    private TokenEndpointFixture(IHost host) => _host = host;

    /// <summary>Builds, starts, and seeds a ready-to-use host.</summary>
    /// <remarks>
    /// Mapping FastEndpoints mutates process-global state, so the calling test class must join
    /// <see cref="FastEndpointsHostCollection"/>.
    /// </remarks>
    public static async Task<TokenEndpointFixture> StartAsync()
    {
        var databaseSuffix = Guid.NewGuid().ToString("n");

        var host = new HostBuilder()
            // The identity dev/demo features register a DevelopmentOrDemoGuard that hard-fails startup outside
            // the Development environment (HostBuilder defaults to Production). This composition legitimately
            // runs in dev/demo mode, so declare the Development environment.
            .UseEnvironment(Environments.Development)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthorization();

                    // Workstream A: identity substrate + cookie sign-in + login endpoint (in dev/demo mode with
                    // an explicitly configured admin so we can log in through the real POST /login flow).
                    services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                        isDevelopmentOrDemo: true,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"identity-{databaseSuffix}"),
                        initialAdmin: TestAdmin.SeedOptions());

                    // Workstream B: OpenIddict issuance + bearer validation + selector default scheme.
                    services.AddFoundationIdentityOpenIddict(
                        options => options.IsDevelopmentOrDemo = true,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{databaseSuffix}"));

                    // Model an external JWT handler that authenticates a raw provider principal. The token
                    // exchange must reject this identity until a provider-owned projection normalizes it.
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, ForgedExternalAuthenticationHandler>(
                            ForgedExternalAuthenticationHandler.AuthenticationSchemeName, _ => { });

                    // Workstream C: the provider-agnostic Api surface (token endpoint lives here).
                    services.AddFoundationIdentityApi();

                    // FastEndpoints hosting the identity Api + login endpoints, discovered from both assemblies.
                    services.AddFastEndpoints(o => o.Assemblies =
                    [
                        typeof(FoundationIdentityApiOptions).Assembly,   // Api endpoints (token, session, …)
                        typeof(AspNetCoreIdentityDefaults).Assembly      // sign-in endpoints (login)
                    ]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        // FastEndpoints holds these in a process-global static, so mapping without
                        // configuring them inherits whatever the previous host in this process left
                        // behind — which would let these tests pass for the wrong reason. Pin them to
                        // the claim types this host actually issues.
                        endpoints.MapFastEndpoints(config =>
                        {
                            config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                            config.Security.RoleClaimType = IdentityClaimTypes.Role;
                        });
                        endpoints
                            .MapGet("/protected", context =>
                                context.Response.WriteAsync(context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty))
                            .RequireAuthorization();
                        endpoints
                            .MapGet("/permission-protected", () => Results.Ok())
                            .RequireAuthorization(new AuthorizeAttribute
                            {
                                AuthenticationSchemes = ForgedExternalAuthenticationHandler.AuthenticationSchemeName
                            })
                            .RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersManage);
                    });
                });
            })
            .Build();

        // Materialize both in-memory schemas before startup so the seeder hosted service can write.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        // Starting runs the registered hosted services (identity admin seeder + OpenIddict store init).
        await host.StartAsync();

        return new TokenEndpointFixture(host);
    }

    public HttpClient Client => _host.GetTestClient();

    public IServiceProvider Services => _host.Services;

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed class ForgedExternalAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationSchemeName = "Elsa.Identity.Oidc.Jwt";
    public const string Token = "forged-external";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.Authorization.ToString().Equals($"Bearer {Token}", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "external-attacker"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
            new Claim(IdentityClaimTypes.Permission, DefaultIdentityPermissionKeys.IdentityUsersManage)
        ], AuthenticationSchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationSchemeName)));
    }
}
