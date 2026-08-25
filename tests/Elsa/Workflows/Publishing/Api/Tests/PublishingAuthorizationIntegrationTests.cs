using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

[Collection(PublishingAuthorizationHostCollection.Name)]
public sealed class PublishingAuthorizationIntegrationTests
{
    public static IEnumerable<object[]> PermissionMatrix() =>
    [
        ["anonymous", null!, HttpStatusCode.Unauthorized],
        ["authenticated-untrusted", "untrusted", HttpStatusCode.Unauthorized],
        ["ambiguous", "ambiguous", HttpStatusCode.Unauthorized],
        ["trusted-denied", "denied", HttpStatusCode.Forbidden],
        ["exact-read", "exact", HttpStatusCode.OK],
        ["exact-manage-on-read", "manage", HttpStatusCode.Forbidden],
        ["configured-implication", "implied", HttpStatusCode.OK],
        ["evaluator-wildcard", "wildcard", HttpStatusCode.OK],
        ["normalized", "normalized", HttpStatusCode.OK],
        ["normalized-external", "external", HttpStatusCode.OK],
        ["malformed-normalization-marker", "invalid-normalization", HttpStatusCode.Unauthorized]
    ];

    [Theory]
    [MemberData(nameof(PermissionMatrix))]
    public async Task Minimal_publishing_read_route_uses_the_fail_closed_shared_permission_matrix(
        string _,
        string? identity,
        HttpStatusCode expected)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/publishing/activities", identity));

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
            Assert.Equal(0, host.Observations.RequestSenderCalls);
    }

    [Theory]
    [InlineData("exact", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("manage", HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData("implied", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK, HttpStatusCode.OK)]
    public async Task Read_and_manage_routes_preserve_exact_and_configured_permission_relationships(
        string identity,
        HttpStatusCode expectedRead,
        HttpStatusCode expectedManage)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();

        using var read = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/publishing/activities", identity));
        using var manage = await host.Client.SendAsync(PublishingAuthorizationRequests.Post(
            "/publishing/workflows/version-1/publish", identity));

        Assert.Equal(expectedRead, read.StatusCode);
        Assert.Equal(expectedManage, manage.StatusCode);
    }

    [Fact]
    public async Task External_claim_mapping_is_normalized_before_the_publishing_evaluator_runs()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/publishing/activities", "external"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Observations.NormalizerCalls);
        Assert.Contains(
            PermissionKey.Normalize(WorkflowPublishingPermissions.Read),
            host.Observations.EvaluatedPermissions.Select(PermissionKey.Normalize));
    }

    [Fact]
    public async Task The_real_host_uses_the_replacement_evaluator_and_dynamic_permission_provider()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();

        Assert.IsType<PublishingRecordingPermissionEvaluator>(
            host.Host.Services.GetRequiredService<IPermissionEvaluator>());
        Assert.IsType<RequirePermissionPolicyProvider>(
            host.Host.Services.GetRequiredService<IAuthorizationPolicyProvider>());

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/publishing/activities", "exact"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(host.Observations.EvaluatedPermissions);
        Assert.All(host.Observations.EvaluatorImplementations, implementation =>
            Assert.Equal(typeof(PublishingRecordingPermissionEvaluator).FullName, implementation));
    }

    [Fact]
    public async Task Minimal_and_retained_FastEndpoints_routes_share_the_same_provider_normalizer_and_evaluator()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();

        foreach (var (identity, expected) in new[]
                 {
                     ("exact", HttpStatusCode.OK),
                     ("implied", HttpStatusCode.OK),
                     ("wildcard", HttpStatusCode.OK),
                     ("denied", HttpStatusCode.Forbidden),
                     ("untrusted", HttpStatusCode.Unauthorized),
                     ("ambiguous", HttpStatusCode.Unauthorized)
                 })
        {
            host.Observations.Reset();
            using var minimal = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
                "/publishing/activities", identity));
            var minimalPermissions = host.Observations.EvaluatedPermissions.ToArray();
            var minimalResourceCalls = host.Observations.ResourceCalls;

            host.Observations.Reset();
            using var fastEndpoints = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
                "/test/publishing-fast", identity));
            var fastPermissions = host.Observations.EvaluatedPermissions.ToArray();
            var fastResourceCalls = host.Observations.ResourceCalls;

            Assert.Equal(expected, minimal.StatusCode);
            Assert.Equal(expected, fastEndpoints.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                Assert.True(minimalResourceCalls > 0, "Minimal API authorization did not invoke the shared resource handler.");
                Assert.True(fastResourceCalls > 0, "FastEndpoints authorization did not invoke the shared resource handler.");
                Assert.True(
                    minimalPermissions.Select(PermissionKey.Normalize)
                        .Contains(PermissionKey.Normalize(WorkflowPublishingPermissions.Read), StringComparer.Ordinal) ||
                    minimalPermissions.Contains(PermissionKey.Wildcard),
                    $"Minimal route did not evaluate read or wildcard: {string.Join(",", minimalPermissions)}");
                Assert.True(
                    fastPermissions.Select(PermissionKey.Normalize)
                        .Contains(PermissionKey.Normalize(WorkflowPublishingPermissions.Read), StringComparer.Ordinal) ||
                    fastPermissions.Contains(PermissionKey.Wildcard),
                    $"FastEndpoints route did not evaluate read or wildcard: {string.Join(",", fastPermissions)}");
            }
        }
    }

    [Theory]
    [InlineData("/publishing/activities")]
    [InlineData("/test/publishing-fast")]
    public async Task Evaluator_cancellation_is_observed_by_both_authoring_models(string path)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        using var request = PublishingAuthorizationRequests.Create(path, "cancel-evaluator");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.SendAsync(request));

        Assert.NotEmpty(host.Observations.EvaluatedPermissions);
        Assert.True(host.Observations.LastEvaluatorCancellation.CanBeCanceled);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
    }
}

internal static class PublishingAuthorizationRequests
{
    public static HttpRequestMessage Create(string path, string? identity, string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(request, identity, tenant);
        return request;
    }

    public static HttpRequestMessage Post(string path, string? identity, string json = "{}", string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        AddHeaders(request, identity, tenant);
        return request;
    }

    private static void AddHeaders(HttpRequestMessage request, string? identity, string? tenant)
    {
        if (identity is null)
            return;

        request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.IdentityHeader, identity);
        if (tenant is not null)
            request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.TenantHeader, tenant);
        else if (identity != "no-tenant" && !identity.EndsWith("-no-tenant", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.TenantHeader, "tenant-a");

        if (identity.Contains("resource-denied", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.ResourceHeader, "deny");
    }
}

internal sealed class PublishingAuthorizationHost(IHost host) : IAsyncDisposable
{
    public const string IdentityHeader = "X-Publishing-Authorization-Identity";
    public const string TenantHeader = "X-Publishing-Authorization-Tenant";
    public const string ResourceHeader = "X-Publishing-Authorization-Resource";
    public const string ActivityTenantHeader = "X-Publishing-Authorization-Activity-Tenant";

    public HttpClient Client { get; } = host.GetTestClient();
    public IHost Host { get; } = host;
    public PublishingAuthorizationObservationState Observations { get; } =
        host.Services.GetRequiredService<PublishingAuthorizationObservationState>();
    public IReadOnlyList<Endpoint> Endpoints =>
        Host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    public static async Task<PublishingAuthorizationHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-workflows-publishing-authorization");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddWorkflowRuntime();
                    services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
                    services.AddAuthentication(PublishingAuthorizationAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, PublishingAuthorizationAuthenticationHandler>(
                            PublishingAuthorizationAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(
                            [PublishingAuthorizationAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                    services.AddOpenApi();
                    services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([]));
                    new WorkflowsPublishingFeature().ConfigureServices(services);
                    new WorkflowsPublishingApiFeature().ConfigureServices(services);
                    Support.PublishingDomainSeams.Register(services);
                    services.AddPermissionContributor<PublishingAuthorizationContributor>();
                    services.ReplacePermissionEvaluator<PublishingRecordingPermissionEvaluator>();
                    services.AddScoped<IPermissionResourceHandler, PublishingAuthorizationResourceHandler>();
                    services.AddSingleton<PublishingAuthorizationObservationState>();
                    services.AddSingleton<PublishingAuthorizationRequestSender>();
                    services.AddSingleton<IRequestSender>(provider =>
                        provider.GetRequiredService<PublishingAuthorizationRequestSender>());
                    services.RemoveAll<IWorkflowExecutableCompiler>();
                    services.AddScoped<IWorkflowExecutableCompiler, PublishingAuthorizationCompiler>();
                    services.AddSingleton<PublishingAuthorizationBoundaryProbe>();
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(PublishingAuthorizationFastEndpoint).Assembly];
                        options.Filter = type => type == typeof(PublishingAuthorizationFastEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        WorkflowsPublishingApi.MapWorkflowsPublishingApi(endpoints);
                        MapAuthorizationProbeEndpoints(endpoints);
                        endpoints.MapFastEndpoints();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new PublishingAuthorizationHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }

    private static void MapAuthorizationProbeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/test/publishing/resource/{tenantId}", () => Results.Ok("resource-ok"))
            .RequirePermission(WorkflowPublishingPermissions.Read);

        endpoints.MapGet("/test/publishing/activity-authorizer", (
                HttpContext context,
                IActivityPublishingAuthorizationContext authorization,
                PublishingAuthorizationBoundaryProbe probe) =>
            {
                probe.RecordActivityAuthorizer();
                var tenant = context.Request.Headers[ActivityTenantHeader].ToString();
                return authorization.CanAccessTenant(tenant)
                    ? Results.Ok(new { authorized = true })
                    : Results.Forbid();
            })
            .RequirePermission(WorkflowPublishingPermissions.Manage);

        endpoints.MapGet("/test/publishing/activity-payload", (
                HttpContext context,
                IActivityPublishingAuthorizationContext authorization) =>
            {
                var tenant = context.Request.Headers[ActivityTenantHeader].ToString();
                var allowed = authorization.CanAccessTenant(tenant);
                return Results.Ok(new { payload = allowed ? "provider-payload" : (string?)null });
            })
            .RequirePermission(WorkflowPublishingPermissions.Read);

        endpoints.MapGet("/test/publishing/denial-boundary", (PublishingAuthorizationBoundaryProbe probe) =>
            {
                probe.TouchAll();
                return Results.Ok();
            })
            .RequirePermission(WorkflowPublishingPermissions.Manage);
    }
}

internal sealed class PublishingAuthorizationObservationState
{
    private int _evaluatorCalls;
    private int _requestSenderCalls;
    private int _normalizerCalls;
    private int _compilerCalls;
    private int _resourceCalls;

    public int EvaluatorCalls => Volatile.Read(ref _evaluatorCalls);
    public int RequestSenderCalls => Volatile.Read(ref _requestSenderCalls);
    public int NormalizerCalls => Volatile.Read(ref _normalizerCalls);
    public int CompilerCalls => Volatile.Read(ref _compilerCalls);
    public int ResourceCalls => Volatile.Read(ref _resourceCalls);
    public List<string> EvaluatedPermissions { get; } = [];
    public List<string> EvaluatorImplementations { get; } = [];
    public CancellationToken LastEvaluatorCancellation { get; set; }

    public void Reset()
    {
        Interlocked.Exchange(ref _evaluatorCalls, 0);
        Interlocked.Exchange(ref _requestSenderCalls, 0);
        Interlocked.Exchange(ref _normalizerCalls, 0);
        Interlocked.Exchange(ref _compilerCalls, 0);
        Interlocked.Exchange(ref _resourceCalls, 0);
        lock (EvaluatedPermissions)
            EvaluatedPermissions.Clear();
        lock (EvaluatorImplementations)
            EvaluatorImplementations.Clear();
        LastEvaluatorCancellation = CancellationToken.None;
    }

    internal void RecordEvaluation(string permission, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _evaluatorCalls);
        LastEvaluatorCancellation = cancellationToken;
        lock (EvaluatedPermissions)
            EvaluatedPermissions.Add(permission);
        lock (EvaluatorImplementations)
            EvaluatorImplementations.Add(typeof(PublishingRecordingPermissionEvaluator).FullName!);
    }

    internal void RecordRequestSender() => Interlocked.Increment(ref _requestSenderCalls);
    internal void RecordNormalizer() => Interlocked.Increment(ref _normalizerCalls);
    internal void RecordCompiler() => Interlocked.Increment(ref _compilerCalls);
    internal void RecordResource(string permission)
    {
        Interlocked.Increment(ref _resourceCalls);
    }
}

internal sealed class PublishingRecordingPermissionEvaluator(
    PublishingAuthorizationObservationState observations,
    IPermissionCatalog catalog) : IPermissionEvaluator
{
    private readonly ClaimsPermissionEvaluator _inner = new(catalog);

    public ValueTask<PermissionEvaluationResult> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        observations.RecordEvaluation(context.Permission, cancellationToken);
        if (context.Resource is HttpContext httpContext &&
            string.Equals(httpContext.Request.Headers[PublishingAuthorizationHost.IdentityHeader],
                "cancel-evaluator", StringComparison.Ordinal))
            throw new OperationCanceledException(cancellationToken);

        return _inner.EvaluateAsync(context, cancellationToken);
    }
}

internal sealed class PublishingAuthorizationResourceHandler(
    PublishingAuthorizationObservationState observations) : IPermissionResourceHandler
{
    public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observations.RecordResource(context.Permission);
        if (context.TenantId is null)
            return ValueTask.FromResult<PermissionEvaluationResult?>(
                PermissionEvaluationResult.Denied("A tenant is required."));

        if (context.Resource is HttpContext httpContext)
        {
            var expectedTenant = httpContext.Request.Headers[PublishingAuthorizationHost.TenantHeader].ToString();
            if (!string.IsNullOrWhiteSpace(expectedTenant) &&
                !string.Equals(expectedTenant, context.TenantId, StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(
                    PermissionEvaluationResult.Denied("The request tenant does not match the principal."));

            if (httpContext.GetRouteValue("tenantId") is string routeTenant &&
                !string.Equals(routeTenant, context.TenantId, StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(
                    PermissionEvaluationResult.Denied("The route tenant does not match the principal."));

            if (string.Equals(httpContext.Request.Headers[PublishingAuthorizationHost.ResourceHeader],
                    "deny", StringComparison.Ordinal))
                return ValueTask.FromResult<PermissionEvaluationResult?>(
                    PermissionEvaluationResult.Denied("The request resource was denied."));
        }

        return ValueTask.FromResult<PermissionEvaluationResult?>(null);
    }
}

internal sealed class PublishingAuthorizationContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Publishing.Api.Tests.Authorization";

    public IEnumerable<Permission> Contribute() =>
    [
        new(
            "publishing.authorization.admin",
            "Publishing authorization test administrator",
            "Publishing authorization tests",
            "Test-only configured implication for both catalog actions.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                WorkflowPublishingPermissions.Read,
                WorkflowPublishingPermissions.Manage
            })
    ];
}

internal sealed class PublishingAuthorizationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IClaimsNormalizer claimsNormalizer,
    PublishingAuthorizationObservationState observations)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PublishingAuthorization";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var fixture = Request.Headers[PublishingAuthorizationHost.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(fixture))
            return AuthenticateResult.NoResult();

        if (fixture == "ambiguous")
        {
            var first = CreateIdentity(SchemeName, "v1", "tenant-a", [WorkflowPublishingPermissions.Read]);
            var second = CreateIdentity(SchemeName, "v1", "tenant-a", [WorkflowPublishingPermissions.Read]);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal([first, second]), SchemeName));
        }

        if (fixture is "external" or "external-resource-denied")
        {
            var external = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("external_group", "publishing-readers")], "PublishingExternal"));
            var normalized = await claimsNormalizer.NormalizeAsync(
                new ClaimsNormalizationContext(
                    external,
                    "tenant-a",
                    "publishing-external",
                    [new ClaimMappingRule(
                        "publishing-external-read",
                        "tenant-a",
                        "publishing-external",
                        "external_group",
                        "publishing-readers",
                        new HashSet<string>(StringComparer.Ordinal),
                        new HashSet<string>(StringComparer.Ordinal) { WorkflowPublishingPermissions.Read },
                        0,
                        StopOnMatch: true)],
                    SchemeName),
                Context.RequestAborted);
            observations.RecordNormalizer();
            return AuthenticateResult.Success(new AuthenticationTicket(normalized.Principal, SchemeName));
        }

        var authenticationType = fixture == "untrusted" ? "PublishingUntrusted" : SchemeName;
        var marker = fixture == "invalid-normalization" ? "invalid" : "v1";
        var permissions = fixture switch
        {
            "exact" or "normalized" or "tenant-b" or "route-mismatch" or "no-tenant" =>
                new[] { WorkflowPublishingPermissions.Read },
            "manage" => new[] { WorkflowPublishingPermissions.Manage },
            "implied" or "implied-no-tenant" => new[] { "publishing.authorization.admin" },
            "wildcard" or "wildcard-no-tenant" => new[] { PermissionKey.Wildcard },
            "cancel-evaluator" => new[] { WorkflowPublishingPermissions.Read },
            _ => new[] { "workflow-publishing.other" }
        };
        if (fixture == "normalized")
            permissions[0] = "WORKFLOW-PUBLISHING.READ";

        var tenant = fixture == "no-tenant" || fixture.EndsWith("-no-tenant", StringComparison.Ordinal)
            ? null
            : fixture == "tenant-b" ? "tenant-b" : "tenant-a";
        var identity = CreateIdentity(authenticationType, marker, tenant, permissions);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private static ClaimsIdentity CreateIdentity(
        string authenticationType,
        string marker,
        string? tenant,
        IEnumerable<string> permissions)
    {
        var claims = new List<Claim> { new(IdentityClaimTypes.Normalized, marker) };
        if (tenant is not null)
            claims.Add(new Claim(IdentityClaimTypes.TenantId, tenant));
        claims.Add(new Claim(IdentityClaimTypes.Provider, "publishing-authorization-test-provider"));
        claims.AddRange(permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, "publishing-authorization-actor"));
        return new ClaimsIdentity(claims, authenticationType);
    }
}

internal sealed class PublishingAuthorizationRequestSender(PublishingAuthorizationObservationState observations)
    : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        observations.RecordRequestSender();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }
}

internal sealed class PublishingAuthorizationCompiler(PublishingAuthorizationObservationState observations)
    : IWorkflowExecutableCompiler
{
    private readonly CaptureWorkflowExecutableCompiler _inner = new();

    public ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        observations.RecordCompiler();
        return _inner.CompileAsync(request, cancellationToken);
    }
}

internal sealed class PublishingAuthorizationBoundaryProbe
{
    public int ActivityAuthorizerCalls { get; private set; }
    public int SenderCalls { get; private set; }
    public int StoreCalls { get; private set; }
    public int CompilerCalls { get; private set; }
    public int PublisherCalls { get; private set; }
    public int TestRunCalls { get; private set; }

    public void RecordActivityAuthorizer() => ActivityAuthorizerCalls++;

    public void TouchAll()
    {
        SenderCalls++;
        StoreCalls++;
        CompilerCalls++;
        PublisherCalls++;
        TestRunCalls++;
    }
}

internal sealed class PublishingAuthorizationFastEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("test/publishing-fast");
        ConfigurePermissions(WorkflowPublishingPermissions.Read);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("fast", cancellationToken);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PublishingAuthorizationHostCollection
{
    public const string Name = "publishing-authorization-host";
}
