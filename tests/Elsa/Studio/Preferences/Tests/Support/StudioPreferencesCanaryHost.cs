using System.Security.Claims;
using CShells.AspNetCore.Features;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using CShells.FastEndpoints.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Preferences.Tests.Support;

/// <summary>
/// Deterministic plain ASP.NET Core host for migrated HTTP/OpenAPI evidence and mixed-authoring-model
/// checks. Immutable pre-migration baselines remain the only source of legacy evidence.
/// </summary>
public sealed class StudioPreferencesCanaryHost : IAsyncDisposable
{
    public const string IdentityHeader = "X-Canary-Identity";
    public const string HostId = "studio-primary";
    public const string SubjectId = "user-7";
    public const string TenantId = "tenant-3";

    private readonly IHost host;

    private StudioPreferencesCanaryHost(IHost host)
    {
        this.host = host;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => host.Services;

    public static Task<StudioPreferencesCanaryHost> StartMigratedAsync() => StartAsync();

    private static async Task<StudioPreferencesCanaryHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(CanaryAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CanaryAuthenticationHandler>(
                            CanaryAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddOpenApi();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            CanaryAuthenticationHandler.SchemeName
                        });
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                    services.AddSingleton<IAuthSessionService, CanaryAuthSessionService>();
                    services.AddScoped<IPermissionResourceHandler, CanaryPermissionResourceHandler>();

                    // Register the production feature services exactly as the shell feature does,
                    // while keeping this legacy evidence host independent of CShells activation.
                    new StudioPreferencesFeature().ConfigureServices(services);
                    new StudioPreferencesApiFeature().ConfigureServices(services);
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(UnrelatedFastEndpointsEndpoint).Assembly];
                    });
                });
                webHost.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.TraceIdentifier = "studio-preferences-canary";
                        await next(context);
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            foreach (var configurator in endpoints.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                        });

                        MapMigratedFeature(endpoints);

                        endpoints.MapOpenApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        await SeedDashboardAsync(host.Services);
        return new StudioPreferencesCanaryHost(host);
    }

    public static async Task<IReadOnlyList<HttpCompatibilityObservation>> CaptureAsync(
        IReadOnlyList<HttpCompatibilityCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var observations = new List<HttpCompatibilityObservation>(cases.Count);

        // A fresh host per case makes each observation independent of prior conditional writes and
        // keeps revisions, timestamps, and seeded documents deterministic across repeated captures.
        foreach (var testCase in cases)
        {
            await using var canary = await StartAsync();
            observations.Add(await HttpEvidenceCapture.CaptureAsync(canary.Client, testCase));
        }

        return observations;
    }

    public async Task<StudioPreferenceDocument?> FindDashboardAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
        var key = new StudioPreferenceKey(SubjectId, TenantId, HostId, StudioPreferenceNamespaces.Dashboard);
        return await store.FindAsync(key);
    }

    public async Task<string> GetCurrentOpenApiDocumentAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task SeedDashboardAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
        var key = new StudioPreferenceKey(SubjectId, TenantId, HostId, StudioPreferenceNamespaces.Dashboard);
        using var value = System.Text.Json.JsonDocument.Parse("{\"layout\":\"wide\"}");
        await store.WriteAsync(
            key,
            new StudioPreferenceWrite(1, value.RootElement.Clone()),
            StudioPreferenceWriteCondition.MustNotExist,
            FixedTimeProvider.UtcNow);
    }

    private static void MapMigratedFeature(IEndpointRouteBuilder endpoints)
    {
        IWebShellFeature feature = new StudioPreferencesApiFeature();
        feature.MapEndpoints(endpoints, null);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public static readonly DateTimeOffset UtcNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CanaryAuthSessionService : IAuthSessionService
    {
        public ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthSession(
                "authenticated",
                SubjectId,
                "Ada",
                TenantId,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                "fresh",
                CanaryAuthenticationHandler.SchemeName));
    }

    private sealed class CanaryPermissionResourceHandler(IHttpContextAccessor accessor) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            var request = (context.Resource as HttpContext) ?? accessor.HttpContext;
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                request?.Request.Headers[IdentityHeader].ToString() == "resource-denied"
                    ? PermissionEvaluationResult.Denied("The canary resource denied the request.")
                    : null);
        }
    }

    private sealed class CanaryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "studio-preferences-canary";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var fixture = values.ToString();
            var permissions = fixture switch
            {
                "read" => [StudioPreferencesPermissions.Read],
                "write" => [StudioPreferencesPermissions.Write],
                "wildcard" => [PermissionKey.Wildcard],
                "resource-denied" => [StudioPreferencesPermissions.Read, StudioPreferencesPermissions.Write],
                _ => Array.Empty<string>()
            };
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, fixture == "untrusted" ? "legacy" : "v1"),
                new(IdentityClaimTypes.TenantId, TenantId),
                new(IdentityClaimTypes.Provider, "canary")
            };
            claims.AddRange(permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

internal sealed class UnrelatedFastEndpointsEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/_canary/unrelated-fastendpoint";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions(StudioPreferencesPermissions.Read);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("unrelated-fastendpoint", cancellationToken);
}
