using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Services;
using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
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
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api.Support;

/// <summary>
/// A real mixed-authoring host for the Activities Design authorization matrix.  The production
/// Minimal API mapper and a retained test-only FastEndpoints endpoint are both sent through the
/// Foundation Identity policy provider and the same replacement evaluator/resource handlers.
/// </summary>
public sealed class ActivitiesDesignAuthorizationHost : IAsyncDisposable
{
    public const string IdentityHeader = "X-Activities-Design-Authorization-Identity";
    public const string TenantHeader = "X-Activities-Design-Authorization-Tenant";
    public const string ProviderHeader = "X-Activities-Design-Authorization-Provider";
    public const string ResourceHeader = "X-Activities-Design-Authorization-Resource";

    public ActivitiesDesignAuthorizationHost(IHost host)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Client = host.GetTestClient();
        Observations = host.Services.GetRequiredService<AuthorizationObservationState>();
    }

    public HttpClient Client { get; }
    public IHost Host { get; }
    public AuthorizationObservationState Observations { get; }
    public IReadOnlyList<Endpoint> Endpoints => Host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    public static async Task<ActivitiesDesignAuthorizationHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-activities-design-authorization");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication(AuthorizationAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, AuthorizationAuthenticationHandler>(AuthorizationAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([AuthorizationAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                    services.AddPermissionContributor<ActivitiesDesignAuthorizationContributor>();
                    services.AddSingleton<AuthorizationObservationState>();
                    services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();
                    services.AddScoped<IPermissionResourceHandler, AuthorizationResourceHandler>();
                    services.AddOpenApi();
                    new ActivitiesDesignApiFeature().ConfigureServices(services);
                    services.AddSingleton<AuthorizationRequestSender>();
                    services.AddSingleton<IRequestSender>(provider => provider.GetRequiredService<AuthorizationRequestSender>());
                    services.AddSingleton<AuthorizationCommandSender>();
                    services.AddSingleton<ICommandSender>(provider => provider.GetRequiredService<AuthorizationCommandSender>());
                    services.AddSingleton<AuthorizationProviderProbe>();
                    services.AddSingleton<AuthorizationStoreProbe>();
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(ActivitiesDesignAuthorizationIntegrationTests).Assembly];
                        options.Filter = type => type == typeof(ActivitiesDesignFastEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        ActivitiesDesignApi.MapActivitiesDesignApi(endpoints);
                        MapAuthorizationProbeEndpoints(endpoints);
                        endpoints.MapFastEndpoints();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new ActivitiesDesignAuthorizationHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }

    private static void MapAuthorizationProbeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/test/activity-resource/{tenantId}", () => Results.Ok("resource-ok"))
            .RequirePermission("activity-design.read");

        endpoints.MapGet("/test/activity-provider-authoring", async (
                HttpContext context,
                IActivityAuthoringContextAsync authorization) =>
            {
                var provider = context.Request.Headers[ProviderHeader].ToString();
                var allowed = await authorization.CanAuthorProviderAsync(provider, context.RequestAborted);
                return allowed ? Results.Ok(new { allowed = true }) : Results.Forbid();
            })
            .RequirePermission("activity-design.manage");

        endpoints.MapGet("/test/activity-provider-payload", async (
                HttpContext context,
                IActivityAuthoringContextAsync authorization) =>
            {
                var provider = context.Request.Headers[ProviderHeader].ToString();
                var allowed = await authorization.CanReadProviderPayloadAsync(provider, context.RequestAborted);
                return Results.Ok(new { payload = allowed ? "provider-payload" : null });
            })
            .RequirePermission("activity-design.read");

        endpoints.MapGet("/test/activity-denial-boundary", (
                AuthorizationProviderProbe provider,
                AuthorizationStoreProbe store,
                AuthorizationRequestSender sender) =>
            {
                provider.Touch();
                store.Touch();
                sender.Touch();
                return Results.Ok();
            })
            .RequirePermission("activity-design.manage");
    }
}

public sealed class ActivitiesDesignAuthorizationContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Activities.Design.Tests.Authorization";

    public IEnumerable<Permission> Contribute() =>
    [
        new(
            "activity-design.admin",
            "Activities Design test administrator",
            "Activities Design tests",
            "Test-only implied permission.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                "activity-design.read",
                "activity-design.manage"
            })
    ];
}

public sealed class AuthorizationObservationState
{
    private int _resourceCalls;
    private int _evaluatorCalls;
    private int _requestSenderCalls;
    private int _commandSenderCalls;
    private int _providerCalls;
    private int _storeCalls;
    private int _normalizerCalls;

    public int ResourceCalls => Volatile.Read(ref _resourceCalls);
    public int EvaluatorCalls => Volatile.Read(ref _evaluatorCalls);
    public int RequestSenderCalls => Volatile.Read(ref _requestSenderCalls);
    public int CommandSenderCalls => Volatile.Read(ref _commandSenderCalls);
    public int ProviderCalls => Volatile.Read(ref _providerCalls);
    public int StoreCalls => Volatile.Read(ref _storeCalls);
    public int NormalizerCalls => Volatile.Read(ref _normalizerCalls);
    public List<string> ResourcePermissions { get; } = [];
    public List<string> EvaluatedPermissions { get; } = [];
    public List<string> EvaluatorImplementations { get; } = [];
    public CancellationToken LastEvaluatorCancellation { get; set; }

    public void Reset()
    {
        Interlocked.Exchange(ref _resourceCalls, 0);
        Interlocked.Exchange(ref _evaluatorCalls, 0);
        Interlocked.Exchange(ref _requestSenderCalls, 0);
        Interlocked.Exchange(ref _commandSenderCalls, 0);
        Interlocked.Exchange(ref _providerCalls, 0);
        Interlocked.Exchange(ref _storeCalls, 0);
        Interlocked.Exchange(ref _normalizerCalls, 0);
        lock (ResourcePermissions)
            ResourcePermissions.Clear();
        lock (EvaluatedPermissions)
            EvaluatedPermissions.Clear();
        lock (EvaluatorImplementations)
            EvaluatorImplementations.Clear();
        LastEvaluatorCancellation = CancellationToken.None;
    }

    internal void RecordResource(string permission)
    {
        Interlocked.Increment(ref _resourceCalls);
        lock (ResourcePermissions)
            ResourcePermissions.Add(permission);
    }

    internal void RecordEvaluation(string permission, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _evaluatorCalls);
        LastEvaluatorCancellation = cancellationToken;
        lock (EvaluatedPermissions)
            EvaluatedPermissions.Add(permission);
        lock (EvaluatorImplementations)
            EvaluatorImplementations.Add(typeof(RecordingPermissionEvaluator).FullName!);
    }

    internal void RecordRequestSender() => Interlocked.Increment(ref _requestSenderCalls);
    internal void RecordCommandSender() => Interlocked.Increment(ref _commandSenderCalls);
    internal void RecordProvider() => Interlocked.Increment(ref _providerCalls);
    internal void RecordStore() => Interlocked.Increment(ref _storeCalls);
    internal void RecordNormalizer() => Interlocked.Increment(ref _normalizerCalls);
}

public sealed class RecordingPermissionEvaluator(
    AuthorizationObservationState observations,
    IPermissionCatalog catalog) : IPermissionEvaluator
{
    private readonly ClaimsPermissionEvaluator _inner = new(catalog);

    public ValueTask<PermissionEvaluationResult> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        observations.RecordEvaluation(context.Permission, cancellationToken);
        if (context.Resource is HttpContext httpContext &&
            string.Equals(httpContext.Request.Headers[ActivitiesDesignAuthorizationHost.IdentityHeader], "cancel-evaluator", StringComparison.Ordinal))
            throw new OperationCanceledException(cancellationToken);

        return _inner.EvaluateAsync(context, cancellationToken);
    }
}

public sealed class AuthorizationResourceHandler(IHttpContextAccessor httpContextAccessor, AuthorizationObservationState observations)
    : IPermissionResourceHandler
{
    public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observations.RecordResource(context.Permission);

        if (context.TenantId is null)
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("A tenant is required."));

        if (context.Resource is HttpContext httpContext)
        {
            var expectedTenant = httpContext.Request.Headers[ActivitiesDesignAuthorizationHost.TenantHeader].ToString();
            if (!string.IsNullOrWhiteSpace(expectedTenant) && !string.Equals(expectedTenant, context.TenantId, StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("The tenant does not match the request."));

            if (httpContext.GetRouteValue("tenantId") is string routeTenant &&
                !string.Equals(routeTenant, context.TenantId, StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("The route tenant does not match the principal."));

            if (string.Equals(httpContext.Request.Headers[ActivitiesDesignAuthorizationHost.ResourceHeader], "deny", StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("The request resource was denied."));
        }

        if (context.Resource is ActivityProviderAuthorizationResource providerResource &&
            string.Equals(
                httpContextAccessor.HttpContext?.Request.Headers[ActivitiesDesignAuthorizationHost.ProviderHeader],
                "denied-provider",
                StringComparison.Ordinal) &&
            string.Equals(providerResource.ProviderKey, "denied-provider", StringComparison.Ordinal))
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Denied("The provider resource was denied."));

        return ValueTask.FromResult<PermissionEvaluationResult?>(null);
    }
}

public sealed class AuthorizationRequestSender(AuthorizationObservationState observations) : IRequestSender
{
    public void Touch() => observations.RecordRequestSender();

    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        observations.RecordRequestSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }
}

public sealed class AuthorizationProviderProbe(AuthorizationObservationState observations)
{
    public void Touch() => observations.RecordProvider();
}

public sealed class AuthorizationStoreProbe(AuthorizationObservationState observations)
{
    public void Touch() => observations.RecordStore();
}

public sealed class AuthorizationCommandSender(AuthorizationObservationState observations) : ICommandSender
{
    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
    {
        observations.RecordCommandSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }

    public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default)
    {
        observations.RecordCommandSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class AuthorizationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IClaimsNormalizer claimsNormalizer,
    AuthorizationObservationState observations)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ActivitiesDesignAuthorization";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var fixture = Request.Headers[ActivitiesDesignAuthorizationHost.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(fixture))
            return AuthenticateResult.NoResult();

        if (fixture == "ambiguous")
        {
            var first = CreateIdentity(SchemeName, "activity-design.read", "tenant-a");
            var second = CreateIdentity(SchemeName, "activity-design.read", "tenant-a");
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal([first, second]), SchemeName));
        }

        if (fixture == "external")
        {
            var external = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("external_group", "activity-readers")],
                "ActivitiesDesignExternal"));
            var rule = new ClaimMappingRule(
                "activities-design-external-read",
                "tenant-a",
                "activities-design-external",
                "external_group",
                "activity-readers",
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>([ActivityDesignPermissions.Read], StringComparer.Ordinal),
                0,
                true);
            var normalized = await claimsNormalizer.NormalizeAsync(
                new ClaimsNormalizationContext(
                    external,
                    "tenant-a",
                    "activities-design-external",
                    [rule],
                    SchemeName),
                Context.RequestAborted);
            observations.RecordNormalizer();
            return AuthenticateResult.Success(new AuthenticationTicket(normalized.Principal, SchemeName));
        }

        var identityType = fixture == "untrusted" ? "UntrustedExternal" : SchemeName;
        var marker = fixture == "invalid-normalization" ? "invalid" : "v1";
        var permissions = fixture switch
        {
            "exact" or "normalized" or "tenant-b" or "route-mismatch" or "no-tenant" => new[] { "activity-design.read" },
            "implied-no-tenant" => new[] { "activity-design.admin" },
            "wildcard-no-tenant" => new[] { PermissionKey.Wildcard },
            "manage" => new[] { "activity-design.read", "activity-design.manage" },
            "implied" => new[] { "activity-design.admin" },
            "wildcard" => new[] { PermissionKey.Wildcard },
            "provider-author" => new[] { "activity-design.manage", HttpContextActivityDesignAuthorizationContext.AuthorPermission },
            "provider-payload" => new[] { "activity-design.read", HttpContextActivityDesignAuthorizationContext.ProviderPayloadReadPermission },
            "provider-all" => new[]
            {
                "activity-design.read",
                "activity-design.manage",
                HttpContextActivityDesignAuthorizationContext.AuthorPermission,
                HttpContextActivityDesignAuthorizationContext.ProviderPayloadReadPermission
            },
            _ => new[] { "activity-design.other" }
        };

        if (fixture == "normalized")
            permissions[0] = "ACTIVITY-DESIGN.READ";

        var tenant = fixture.EndsWith("-no-tenant", StringComparison.Ordinal) || fixture == "no-tenant"
            ? null
            : fixture is "tenant-b" or "route-mismatch" ? "tenant-b" : "tenant-a";
        var identity = CreateIdentity(identityType, marker, tenant, permissions);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private static ClaimsIdentity CreateIdentity(
        string authenticationType,
        string marker = "v1",
        string? tenant = "tenant-a",
        IEnumerable<string>? permissions = null)
    {
        var claims = new List<Claim> { new(IdentityClaimTypes.Normalized, marker) };
        if (tenant is not null)
            claims.Add(new Claim(IdentityClaimTypes.TenantId, tenant));
        claims.Add(new Claim(IdentityClaimTypes.Provider, "authorization-test-provider"));
        claims.AddRange((permissions ?? ["activity-design.read"]).Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, "activities-design-authorization-actor"));
        return new ClaimsIdentity(claims, authenticationType);
    }
}

public sealed class ActivitiesDesignFastEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("test/activity-fast");
        ConfigurePermissions("activity-design.read");
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("fast", cancellationToken);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ActivitiesDesignAuthorizationHostCollection
{
    public const string Name = "activities-design-authorization-host";
}
