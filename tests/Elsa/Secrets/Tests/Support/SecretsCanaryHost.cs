using System.Security.Claims;
using System.Collections.Concurrent;
using CShells.FastEndpoints.Contracts;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Secrets.Api;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Secrets.Tests.Support;

/// <summary>
/// Deterministic plain TestServer host for Secrets contract and authorization evidence.
/// The host maps the migrated Minimal API surface and can optionally add a separate, real
/// FastEndpoints route to prove framework coexistence through the shared authorization path.
/// </summary>
public sealed class SecretsCanaryHost : IAsyncDisposable
{
    public const string IdentityHeader = "X-Secrets-Canary-Identity";
    public const string AlphaTenant = "tenant-alpha";
    public const string BetaTenant = "tenant-beta";
    public const string ActiveName = "shared.name";
    public const string SensitiveMarker = "secrets-1348-sensitive-marker";
    public const string ConfigurationKey = "Canary:Secrets:Shared";
    public const string ConfigurationValue = "secrets-1348-configuration-value";
    public static DateTimeOffset FixedUtcNow => FixedTimeProvider.UtcNow;

    private readonly IHost host;

    private SecretsCanaryHost(IHost host, IReadOnlyList<EndpointDataSource> endpointDataSources)
    {
        this.host = host;
        Client = host.GetTestClient();
        EndpointDataSources = endpointDataSources;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => host.Services;

    public IReadOnlyList<EndpointDataSource> EndpointDataSources { get; }

    public static void ResetPermissionEvaluatorObservations() =>
        RecordingPermissionEvaluator.Reset();

    public static int PermissionEvaluatorCallsFor(string path) =>
        RecordingPermissionEvaluator.CallsFor(path);

    public static Task<SecretsCanaryHost> StartMigratedAsync() => StartAsync();

    private static async Task<SecretsCanaryHost> StartAsync()
    {
        IReadOnlyList<EndpointDataSource>? endpointDataSources = null;
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication(CanaryAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CanaryAuthenticationHandler>(
                            CanaryAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddOpenApi();
                    services.AddSingleton<IConfiguration>(Configuration());
                    services.Configure<SecretsOptions>(options =>
                        options.EncryptionKey = "secrets-canary-encryption-key");
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            CanaryAuthenticationHandler.SchemeName
                        });
                    services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                    services.AddScoped<IPermissionResourceHandler, CanaryPermissionResourceHandler>();

                    new SecretsApiFeature().ConfigureServices(services);
                });
                webHost.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.TraceIdentifier = "secrets-canary-trace";
                        await next(context);
                    });
                    // The real host supplies the outer exception-to-HTTP boundary. Keep the plain
                    // TestServer deterministic as well so domain validation/conflict cases become
                    // observable ProblemDetails instead of escaping through TestServer.
                    app.Use(async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                        {
                            context.Response.Clear();
                            context.Response.StatusCode = exception is ArgumentException ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
                            context.Response.ContentType = "application/problem+json";
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "about:blank",
                                title = exception is ArgumentException ? "Bad Request" : "Internal Server Error",
                                status = context.Response.StatusCode,
                                traceId = context.TraceIdentifier
                            });
                        }
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        SecretsApi.MapSecretsApi(endpoints);
                        endpoints.MapOpenApi();
                        endpointDataSources = endpoints.DataSources.ToArray();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        await SeedAsync(host.Services);
        return new SecretsCanaryHost(host, endpointDataSources ?? []);
    }

    public static async Task<IReadOnlyList<HttpCompatibilityObservation>> CaptureAsync(
        IReadOnlyList<HttpCompatibilityCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var observations = new List<HttpCompatibilityObservation>(cases.Count);

        // Every case receives a fresh repository so mutation cases cannot affect another
        // observation and all seeded IDs/timestamps remain deterministic.
        foreach (var testCase in cases)
        {
            await using var canary = await StartMigratedAsync();
            observations.Add(await HttpEvidenceCapture.CaptureAsync(canary.Client, testCase));
        }

        return observations;
    }

    public async Task<string> GetCurrentOpenApiDocumentAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.StopAsync();
        host.Dispose();
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Secrets:EncryptionKey"] = "secrets-canary-encryption-key",
            [ConfigurationKey] = ConfigurationValue
        })
        .Build();

    private static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecretRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretValueProtector>();

        await repository.TryAddAsync(Encrypted(
            AlphaTenant, ActiveName, "secret-alpha-shared", "Alpha shared", protector.Protect(SensitiveMarker)));
        await repository.TryAddAsync(Encrypted(
            BetaTenant, ActiveName, "secret-beta-shared", "Beta shared", protector.Protect("beta-private-value")));
        await repository.TryAddAsync(ConfigurationBacked(
            AlphaTenant, "configuration.secret", "secret-alpha-configuration"));
        await repository.TryAddAsync(Encrypted(
            AlphaTenant, "revoked.secret", "secret-alpha-revoked", "Revoked", protector.Protect("revoked-value"), SecretStatus.Revoked));
        await repository.TryAddAsync(Encrypted(
            AlphaTenant, "deleted.secret", "secret-alpha-deleted", "Deleted", protector.Protect("deleted-value"), SecretStatus.Deleted));
        await repository.TryAddAsync(Expired(
            AlphaTenant, "expired.secret", "secret-alpha-expired", protector.Protect("expired-value")));
    }

    private static Secret Encrypted(
        string tenantId,
        string name,
        string id,
        string displayName,
        string protectedValue,
        SecretStatus status = SecretStatus.Active) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            DisplayName = displayName,
            Description = $"{displayName} description",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Scope = "workflow",
            Tags = new HashSet<string>(["canary", tenantId], StringComparer.OrdinalIgnoreCase),
            Status = status,
            CreatedAt = FixedTimeProvider.UtcNow.AddMinutes(-5),
            Versions =
            [
                new SecretVersion
                {
                    Version = 1,
                    Status = status is SecretStatus.Revoked or SecretStatus.Deleted ? status : SecretStatus.Active,
                    CreatedAt = FixedTimeProvider.UtcNow.AddMinutes(-5),
                    Payload = new SecretPayload
                    {
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["protectedValue"] = protectedValue
                        }
                    }
                }
            ]
        };

    private static Secret ConfigurationBacked(string tenantId, string name, string id) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        DisplayName = "Configuration secret",
        Description = "Configuration-backed canary secret",
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Configuration,
        Scope = "application",
        Tags = new HashSet<string>(["canary", "configuration"], StringComparer.OrdinalIgnoreCase),
        Status = SecretStatus.Active,
        CreatedAt = FixedTimeProvider.UtcNow.AddMinutes(-5),
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                CreatedAt = FixedTimeProvider.UtcNow.AddMinutes(-5),
                Payload = new SecretPayload
                {
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["configurationKey"] = ConfigurationKey
                    }
                }
            }
        ]
    };

    private static Secret Expired(string tenantId, string name, string id, string protectedValue)
    {
        var secret = Encrypted(tenantId, name, id, "Expired", protectedValue);
        secret.Versions[0].ExpiresAt = FixedTimeProvider.UtcNow.AddMinutes(-1);
        return secret;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public static readonly DateTimeOffset UtcNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CanaryPermissionResourceHandler(IHttpContextAccessor accessor) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            var request = (context.Resource as HttpContext) ?? accessor.HttpContext;
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                request?.Request.Headers[IdentityHeader].ToString().Contains("resource-denied", StringComparison.Ordinal)
                    == true
                    ? PermissionEvaluationResult.Denied("The canary resource denied the request.")
                    : null);
        }
    }

    private sealed class RecordingPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
    {
        private static readonly ConcurrentDictionary<string, int> Calls = new(StringComparer.Ordinal);
        private readonly ClaimsPermissionEvaluator inner = new(catalog);

        public async ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Resource is HttpContext httpContext)
                Calls.AddOrUpdate(httpContext.Request.Path.Value ?? string.Empty, 1, static (_, count) => count + 1);

            return await inner.EvaluateAsync(context, cancellationToken);
        }

        public static void Reset() => Calls.Clear();

        public static int CallsFor(string path) => Calls.GetValueOrDefault(path);
    }

    private sealed class CanaryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "secrets-canary";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(IdentityHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var fixture = values.ToString();
            var parts = fixture.Split('|', StringSplitOptions.TrimEntries);
            var permission = parts.ElementAtOrDefault(0);
            var tenant = parts.ElementAtOrDefault(1);
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.Normalized, fixture.Contains("untrusted", StringComparison.Ordinal) ? "legacy" : "v1"),
                new(IdentityClaimTypes.Provider, "secrets-canary")
            };

            if (!string.Equals(tenant, "no-tenant", StringComparison.Ordinal))
                claims.Add(new Claim(IdentityClaimTypes.TenantId, tenant is "tenant-beta" ? BetaTenant : AlphaTenant));

            var permissionValue = permission switch
            {
                "read" => "secrets:read",
                "write" => "secrets:write",
                "update-value" => "secrets:update-value",
                "delete" => "secrets:delete",
                "test" => "secrets:test",
                "wildcard" => PermissionKey.Wildcard,
                "resource-denied" => "secrets:read",
                _ => null
            };
            if (permissionValue is not null)
                claims.Add(new Claim(IdentityClaimTypes.Permission, permissionValue));

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
