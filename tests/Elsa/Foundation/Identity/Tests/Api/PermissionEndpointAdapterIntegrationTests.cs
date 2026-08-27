using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
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

namespace Elsa.Foundation.Identity.Tests.Api;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class PermissionEndpointAdapterIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddElsaEndpoints();
                    services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization(options =>
                        options.AddPolicy("host.custom", policy => policy.RequireClaim("host-access")));
                    services.AddFoundationIdentityApi();
                    services.Configure<FoundationIdentityOptions>(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
                        {
                            HeaderAuthenticationHandler.SchemeName
                        });
                    services.AddSingleton<AdapterCallState>();
                    services.ReplacePermissionEvaluator<RecordingClaimsPermissionEvaluator>();
                    services.AddScoped<IPermissionResourceHandler, RecordingResourceHandler>();
                    services.AddScoped<IPermissionResourceHandler, LaterResourceHandler>();
                    services.AddScoped<IClaimsNormalizer, FixtureClaimsNormalizer>();
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(PermissionEndpointAdapterIntegrationTests).Assembly];
                        options.Filter = type => type == typeof(FastSinglePermissionEndpoint) ||
                                                       type == typeof(FastAnyPermissionEndpoint) ||
                                                       type == typeof(FastAllPermissionEndpoint) ||
                                                       type == typeof(FastImpliedPermissionEndpoint) ||
                                                       type == typeof(FastWildcardPermissionEndpoint) ||
                                                       type == typeof(FastUnrelatedPolicyEndpoint);
                    });
                });
                webHost.Configure(app =>
                {
                    // Regression order: authentication deliberately runs before endpoint selection.
                    // Permission challenge classification must not depend on route metadata at this stage.
                    app.UseAuthentication();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/minimal/single", () => Results.Ok()).RequirePermission("read");
                        endpoints.MapGet("/minimal/any", () => Results.Ok()).RequireAnyPermission("read", "write");
                        endpoints.MapGet("/minimal/all", () => Results.Ok()).RequireAllPermissions("read", "write");
                        endpoints.MapGet("/minimal/implied", () => Results.Ok())
                            .RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersRead);
                        endpoints.MapGet("/minimal/wildcard", () => Results.Ok()).RequirePermission("*");
                        endpoints.MapGet("/minimal/unrelated", () => Results.Ok()).RequireAuthorization("host.custom");
                        FoundationIdentityApi.MapFoundationIdentityApi(endpoints);
                        endpoints.MapFastEndpoints();
                    });
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData("read", HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData("capabilities-exact", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("capabilities-implied", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("wildcard", HttpStatusCode.OK, HttpStatusCode.OK)]
    public async Task Capabilities_Uses_The_Same_Permission_Evaluator_As_The_Remaining_FastEndpoints(
        string? identity,
        HttpStatusCode expectedCapabilities,
        HttpStatusCode expectedFastEndpoint)
    {
        Assert.Equal(expectedCapabilities, await SendAsync("/_elsa/identity/capabilities", identity));
        Assert.Equal(expectedFastEndpoint, await SendAsync("/fast/single", identity == "capabilities-exact" ? "read" : identity));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, "", "")]
    [InlineData("raw", HttpStatusCode.Unauthorized, "", "")]
    [InlineData("unmarked", HttpStatusCode.Unauthorized, "", "")]
    [InlineData("ambiguous", HttpStatusCode.Unauthorized, "", "")]
    [InlineData("denied", HttpStatusCode.Forbidden, "READ", "*,READ")]
    [InlineData("read", HttpStatusCode.OK, "READ", "*,READ")]
    [InlineData("wildcard", HttpStatusCode.OK, "READ", "*")]
    [InlineData("normalizer-failure", HttpStatusCode.Unauthorized, "", "")]
    public async Task SinglePermissionOutcomesMatchAcrossEndpointStyles(
        string? identity,
        HttpStatusCode expected,
        string minimalPermissions,
        string fastPermissions)
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();
        state.Reset();

        Assert.Equal(expected, await SendAsync("/minimal/single", identity));
        var minimal = state.Snapshot();
        state.Reset();
        Assert.Equal(expected, await SendAsync("/fast/single", identity));
        var fast = state.Snapshot();

        AssertParity(minimal, fast);
        AssertNormalCallSequence(minimal, minimalPermissions);
        AssertNormalCallSequence(fast, fastPermissions);
        if (identity is null or "raw" or "unmarked" or "ambiguous" or "normalizer-failure")
        {
            AssertNoAuthorizationCalls(minimal);
            AssertNoAuthorizationCalls(fast);
        }

        if (identity == "normalizer-failure")
        {
            Assert.False(minimal.AuthenticationTicketIssued);
            Assert.False(minimal.AuthenticationTicketContainsNormalizedMarker);
            Assert.False(fast.AuthenticationTicketIssued);
            Assert.False(fast.AuthenticationTicketContainsNormalizedMarker);
        }
    }

    [Theory]
    [InlineData("read", HttpStatusCode.OK, "READ", "*,READ")]
    [InlineData("write", HttpStatusCode.OK, "READ,WRITE", "*,READ,WRITE")]
    [InlineData("denied", HttpStatusCode.Forbidden, "READ,WRITE", "*,READ,WRITE")]
    [InlineData("wildcard", HttpStatusCode.OK, "READ", "*")]
    public async Task AnyPermissionOutcomesMatchAcrossEndpointStyles(
        string identity,
        HttpStatusCode expected,
        string minimalPermissions,
        string fastPermissions)
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();
        state.Reset();
        Assert.Equal(expected, await SendAsync("/minimal/any", identity));
        var minimal = state.Snapshot();
        state.Reset();
        Assert.Equal(expected, await SendAsync("/fast/any", identity));
        var fast = state.Snapshot();
        AssertParity(minimal, fast);
        AssertNormalCallSequence(minimal, minimalPermissions);
        AssertNormalCallSequence(fast, fastPermissions);
    }

    [Theory]
    [InlineData("read", HttpStatusCode.Forbidden)]
    [InlineData("read-write", HttpStatusCode.OK)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    public async Task AllPermissionOutcomesMatchAcrossEndpointStyles(string identity, HttpStatusCode expected)
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();
        state.Reset();
        Assert.Equal(expected, await SendAsync("/minimal/all", identity));
        var minimal = state.Snapshot();
        state.Reset();
        Assert.Equal(expected, await SendAsync("/fast/all", identity));
        var fast = state.Snapshot();
        AssertParity(minimal, fast);
        AssertNormalCallSequence(minimal, "READ,WRITE");
        AssertNormalCallSequence(fast, "READ,WRITE");
    }

    [Fact]
    public async Task ImpliedGrantAndReplacementEvaluatorMatchAcrossEndpointStyles()
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();
        state.Reset();

        Assert.Equal(HttpStatusCode.OK, await SendAsync("/minimal/implied", "implied"));
        var minimal = state.Snapshot();
        Assert.True(minimal.EvaluatorCalls > 0);

        state.Reset();
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/fast/implied", "implied"));
        var fast = state.Snapshot();
        AssertParity(minimal, fast);
        Assert.Equal(1, minimal.ResourceCalls);
        Assert.Equal(1, minimal.LaterResourceCalls);
        Assert.Equal(1, minimal.EvaluatorCalls);
        Assert.Equal(2, fast.ResourceCalls);
        Assert.Equal(2, fast.LaterResourceCalls);
        Assert.Equal(2, fast.EvaluatorCalls);
    }

    [Fact]
    public async Task TrustedIdentityIsIsolatedFromUntrustedClaimsAcrossEndpointStyles()
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();

        foreach (var path in new[] { "/minimal/single", "/fast/single" })
        {
            state.Reset();
            Assert.Equal(HttpStatusCode.OK, await SendAsync(path, "trusted-plus-untrusted"));
            Assert.Equal(1, state.LastPrincipalIdentityCount);
            Assert.Equal(["read"], state.LastPrincipalPermissions);
        }
    }

    [Fact]
    public async Task ProjectedTenantAndProviderReachBothEndpointAdapters()
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();

        state.Reset();
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/minimal/single", "projected-read"));
        var minimal = state.Snapshot();
        AssertNormalCallSequence(minimal, "READ");

        state.Reset();
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/fast/single", "projected-read"));
        var fast = state.Snapshot();
        AssertNormalCallSequence(fast, "*,READ");

        AssertParity(minimal, fast);
        Assert.All(minimal.ResourceObservations.Concat(minimal.EvaluatorObservations), AssertProjectedContext);
        Assert.All(fast.ResourceObservations.Concat(fast.EvaluatorObservations), AssertProjectedContext);
    }

    [Theory]
    [InlineData("anonymous-first")]
    [InlineData("untrusted-first")]
    [InlineData("ambiguous-tenants")]
    public async Task UntrustedOrAmbiguousIdentityOrderingFailsClosedAcrossEndpointStyles(string identity)
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();

        foreach (var path in new[] { "/minimal/single", "/fast/single" })
        {
            state.Reset();
            Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(path, identity));
            Assert.Equal(0, state.ResourceCalls);
            Assert.Equal(0, state.EvaluatorCalls);
        }
    }

    [Theory]
    [InlineData("read", HttpStatusCode.Forbidden)]
    [InlineData("wildcard", HttpStatusCode.OK)]
    public async Task WildcardRequestRequiresAnExplicitWildcardGrantAcrossEndpointStyles(string identity, HttpStatusCode expected)
    {
        Assert.Equal(expected, await SendAsync("/minimal/wildcard", identity));
        Assert.Equal(expected, await SendAsync("/fast/wildcard", identity));
    }

    [Fact]
    public async Task UnrelatedHostPolicyDelegatesWithoutNormalizedPrincipalRequirementAcrossEndpointStyles()
    {
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/minimal/unrelated", "host-access"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/fast/unrelated", "host-access"));
    }

    [Fact]
    public async Task MemberLocalResourceDenialAndOperationalFailuresMatchAcrossEndpointStyles()
    {
        var state = _host.Services.GetRequiredService<AdapterCallState>();

        state.Reset();
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/minimal/any", "resource-any"));
        var minimalResourceAny = state.Snapshot();
        state.Reset();
        Assert.Equal(HttpStatusCode.OK, await SendAsync("/fast/any", "resource-any"));
        var fastResourceAny = state.Snapshot();
        AssertParity(minimalResourceAny, fastResourceAny);
        AssertExactCalls(minimalResourceAny, ["READ", "WRITE"], [], 2);
        AssertExactCalls(fastResourceAny, ["*"], [], 1);

        state.Reset();
        await Assert.ThrowsAsync<TimeoutException>(() => SendAsync("/minimal/single", "resource-timeout"));
        var minimalResourceTimeout = state.Snapshot();
        AssertExactCalls(minimalResourceTimeout, ["READ"], [], 0);

        state.Reset();
        await Assert.ThrowsAsync<TimeoutException>(() => SendAsync("/fast/single", "resource-timeout"));
        var fastResourceTimeout = state.Snapshot();
        AssertParity(minimalResourceTimeout, fastResourceTimeout);
        AssertExactCalls(fastResourceTimeout, ["*"], [], 0);

        state.Reset();
        await Assert.ThrowsAsync<TimeoutException>(() => SendAsync("/minimal/all", "evaluator-timeout"));
        var minimalEvaluatorTimeout = state.Snapshot();
        AssertExactCalls(minimalEvaluatorTimeout, ["READ"], ["READ"], 1);

        state.Reset();
        await Assert.ThrowsAsync<TimeoutException>(() => SendAsync("/fast/all", "evaluator-timeout"));
        var fastEvaluatorTimeout = state.Snapshot();
        AssertParity(minimalEvaluatorTimeout, fastEvaluatorTimeout);
        AssertExactCalls(fastEvaluatorTimeout, ["READ"], ["READ"], 1);

        state.Reset();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync("/minimal/single", "cancelled"));
        var minimalCancelled = state.Snapshot();
        AssertCancellationPropagation(minimalCancelled, "READ");

        state.Reset();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync("/fast/single", "cancelled"));
        var fastCancelled = state.Snapshot();
        AssertCancellationPropagation(fastCancelled, "*");
        AssertParity(minimalCancelled, fastCancelled);
    }

    private static void AssertParity(AdapterCallSnapshot expected, AdapterCallSnapshot actual)
    {
        Assert.Equal(
            expected.ResourceObservations.Select(ToContext).Distinct().ToArray(),
            actual.ResourceObservations.Select(ToContext).Distinct().ToArray());
        Assert.Equal(
            expected.EvaluatorObservations.Select(ToContext).Distinct().ToArray(),
            actual.EvaluatorObservations.Select(ToContext).Distinct().ToArray());
        Assert.Equal(expected.AuthenticationTicketIssued, actual.AuthenticationTicketIssued);
        Assert.Equal(expected.AuthenticationTicketContainsNormalizedMarker, actual.AuthenticationTicketContainsNormalizedMarker);
    }

    private static void AssertNormalCallSequence(AdapterCallSnapshot snapshot, string permissions)
    {
        var expected = string.IsNullOrEmpty(permissions) ? [] : permissions.Split(',');
        AssertExactCalls(snapshot, expected, expected, expected.Length);
    }

    private static void AssertExactCalls(
        AdapterCallSnapshot snapshot,
        IReadOnlyList<string> resourcePermissions,
        IReadOnlyList<string> evaluatorPermissions,
        int laterResourceCalls)
    {
        Assert.Equal(resourcePermissions.Count, snapshot.ResourceCalls);
        Assert.Equal(laterResourceCalls, snapshot.LaterResourceCalls);
        Assert.Equal(evaluatorPermissions.Count, snapshot.EvaluatorCalls);
        Assert.Equal(resourcePermissions, snapshot.ResourceObservations.Select(x => x.Permission).ToArray());
        Assert.Equal(evaluatorPermissions, snapshot.EvaluatorObservations.Select(x => x.Permission).ToArray());
        Assert.All(snapshot.ResourceObservations, AssertCompleteContext);
        Assert.All(snapshot.EvaluatorObservations, AssertCompleteContext);
    }

    private static void AssertCompleteContext(AdapterCallObservation observation)
    {
        Assert.Contains("elsa.identity.normalized=v1", observation.PrincipalShape, StringComparison.Ordinal);
        Assert.Equal(typeof(DefaultHttpContext).FullName, observation.ResourceType);
        Assert.True(observation.CancellationCanBeCanceled);
        Assert.True(observation.MethodTokenMatchesRequestAborted);
        Assert.True(observation.ContextTokenMatchesRequestAborted);
    }

    private static void AssertProjectedContext(AdapterCallObservation observation)
    {
        Assert.Equal("tenant-a", observation.TenantId);
        Assert.Equal("provider-a", observation.Provider);
    }

    private static void AssertNoAuthorizationCalls(AdapterCallSnapshot snapshot)
    {
        Assert.Equal(0, snapshot.ResourceCalls);
        Assert.Equal(0, snapshot.LaterResourceCalls);
        Assert.Equal(0, snapshot.EvaluatorCalls);
    }

    private static void AssertCancellationPropagation(AdapterCallSnapshot snapshot, string permission)
    {
        AssertExactCalls(snapshot, [permission], [], 0);
        Assert.NotNull(snapshot.RequestAbortedToken);
        Assert.Equal(snapshot.RequestAbortedToken, snapshot.ResourceMethodToken);
        Assert.Equal(snapshot.RequestAbortedToken, snapshot.ResourceContextToken);
    }

    private static AdapterContextObservation ToContext(AdapterCallObservation observation) => new(
        observation.TenantId,
        observation.Provider,
        observation.PrincipalShape,
        observation.ResourceType,
        observation.CancellationCanBeCanceled,
        observation.MethodTokenMatchesRequestAborted,
        observation.ContextTokenMatchesRequestAborted);

    private async Task<HttpStatusCode> SendAsync(string path, string? identity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.Add(HeaderAuthenticationHandler.HeaderName, identity);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed record AdapterCallObservation(
        string Permission,
        string? TenantId,
        string? Provider,
        string PrincipalShape,
        string ResourceType,
        bool CancellationCanBeCanceled,
        bool MethodTokenMatchesRequestAborted,
        bool ContextTokenMatchesRequestAborted);

    private sealed record AdapterContextObservation(
        string? TenantId,
        string? Provider,
        string PrincipalShape,
        string ResourceType,
        bool CancellationCanBeCanceled,
        bool MethodTokenMatchesRequestAborted,
        bool ContextTokenMatchesRequestAborted);

    private sealed record AdapterCallSnapshot(
        int ResourceCalls,
        int LaterResourceCalls,
        int EvaluatorCalls,
        IReadOnlyList<AdapterCallObservation> ResourceObservations,
        IReadOnlyList<AdapterCallObservation> EvaluatorObservations,
        bool AuthenticationTicketIssued,
        bool AuthenticationTicketContainsNormalizedMarker,
        CancellationToken? RequestAbortedToken,
        CancellationToken? ResourceMethodToken,
        CancellationToken? ResourceContextToken);

    private sealed class AdapterCallState
    {
        public int ResourceCalls { get; set; }
        public int LaterResourceCalls { get; set; }
        public int EvaluatorCalls { get; set; }
        public List<AdapterCallObservation> ResourceObservations { get; } = [];
        public List<AdapterCallObservation> EvaluatorObservations { get; } = [];
        public bool AuthenticationTicketIssued { get; set; }
        public bool AuthenticationTicketContainsNormalizedMarker { get; set; }
        public CancellationToken? RequestAbortedToken { get; set; }
        public CancellationToken? ResourceMethodToken { get; set; }
        public CancellationToken? ResourceContextToken { get; set; }
        public int LastPrincipalIdentityCount { get; set; }
        public IReadOnlyList<string> LastPrincipalPermissions { get; set; } = [];

        public void Reset()
        {
            ResourceCalls = 0;
            LaterResourceCalls = 0;
            EvaluatorCalls = 0;
            ResourceObservations.Clear();
            EvaluatorObservations.Clear();
            AuthenticationTicketIssued = false;
            AuthenticationTicketContainsNormalizedMarker = false;
            RequestAbortedToken = null;
            ResourceMethodToken = null;
            ResourceContextToken = null;
            LastPrincipalIdentityCount = 0;
            LastPrincipalPermissions = [];
        }

        public AdapterCallSnapshot Snapshot() => new(
            ResourceCalls,
            LaterResourceCalls,
            EvaluatorCalls,
            ResourceObservations.ToArray(),
            EvaluatorObservations.ToArray(),
            AuthenticationTicketIssued,
            AuthenticationTicketContainsNormalizedMarker,
            RequestAbortedToken,
            ResourceMethodToken,
            ResourceContextToken);
    }

    private static AdapterCallObservation Observe(
        PermissionEvaluationContext context,
        CancellationToken methodCancellationToken)
    {
        var requestAborted = (context.Resource as HttpContext)?.RequestAborted;
        return new AdapterCallObservation(
            context.Permission,
            context.TenantId,
            context.Principal.FindFirst(IdentityClaimTypes.Provider)?.Value,
            string.Join(";", context.Principal.Identities.Select(identity =>
                $"{identity.AuthenticationType}|{identity.IsAuthenticated}|{string.Join(",", identity.Claims
                    .Select(claim => $"{claim.Type}={claim.Value}")
                    .OrderBy(claim => claim, StringComparer.Ordinal))}")),
            context.Resource?.GetType().FullName ?? "<null>",
            context.CancellationToken.CanBeCanceled,
            requestAborted is not null && methodCancellationToken == requestAborted.Value,
            requestAborted is not null && context.CancellationToken == requestAborted.Value);
    }

    private sealed class RecordingResourceHandler(AdapterCallState state) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.ResourceCalls++;
            state.ResourceObservations.Add(Observe(context, cancellationToken));
            if (context.Resource is HttpContext httpContext &&
                httpContext.Request.Headers.TryGetValue(HeaderAuthenticationHandler.HeaderName, out var fixture))
            {
                state.RequestAbortedToken = httpContext.RequestAborted;
                state.ResourceMethodToken = cancellationToken;
                state.ResourceContextToken = context.CancellationToken;
                if (fixture == "resource-timeout")
                    throw new TimeoutException("resource timed out");
                if (fixture == "cancelled")
                    throw new OperationCanceledException(cancellationToken);
                if (fixture == "resource-any")
                    return ValueTask.FromResult<PermissionEvaluationResult?>(context.Permission == "READ"
                        ? PermissionEvaluationResult.Denied()
                        : PermissionEvaluationResult.Success);
            }

            return ValueTask.FromResult<PermissionEvaluationResult?>(null);
        }
    }

    private sealed class LaterResourceHandler(AdapterCallState state) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.LaterResourceCalls++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(null);
        }
    }

    private sealed class RecordingClaimsPermissionEvaluator(
        AdapterCallState state,
        IPermissionCatalog catalog) : IPermissionEvaluator
    {
        private readonly ClaimsPermissionEvaluator _inner = new(catalog);

        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            state.EvaluatorCalls++;
            state.EvaluatorObservations.Add(Observe(context, cancellationToken));
            state.LastPrincipalIdentityCount = context.Principal.Identities.Count();
            state.LastPrincipalPermissions = context.Principal.FindAll(IdentityClaimTypes.Permission)
                .Select(claim => claim.Value)
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToArray();
            if (context.Resource is HttpContext httpContext &&
                httpContext.Request.Headers[HeaderAuthenticationHandler.HeaderName] == "evaluator-timeout")
            {
                throw new TimeoutException("evaluator timed out");
            }

            return _inner.EvaluateAsync(context, cancellationToken);
        }
    }

    private sealed class FixtureClaimsNormalizer : IClaimsNormalizer
    {
        public ValueTask<ClaimsNormalizationResult> NormalizeAsync(
            ClaimsNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Claims normalization failed.");
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IClaimsNormalizer normalizer,
        AdapterCallState state)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "permission-adapter-test";
        public const string HeaderName = "X-Test-Identity";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var value))
                return AuthenticateResult.NoResult();

            var fixture = value.ToString();
            if (fixture == "normalizer-failure")
            {
                try
                {
                    await normalizer.NormalizeAsync(new ClaimsNormalizationContext(
                        new ClaimsPrincipal(new ClaimsIdentity([new Claim("external", "value")], "external")),
                        "tenant-a",
                        fixture,
                        []),
                        Context.RequestAborted);
                    throw new InvalidOperationException("The fixture normalizer was expected to fail.");
                }
                catch (InvalidOperationException exception)
                {
                    state.AuthenticationTicketIssued = false;
                    state.AuthenticationTicketContainsNormalizedMarker = false;
                    return AuthenticateResult.Fail(exception);
                }
            }

            ClaimsPrincipal principal = fixture switch
            {
                "raw" => Principal("raw-provider", marked: true, "read"),
                "unmarked" => Principal(SchemeName, marked: false, "read"),
                "ambiguous" => new ClaimsPrincipal(new[]
                {
                    Identity(SchemeName, marked: true, "read"),
                    Identity(SchemeName, marked: true, "write")
                }),
                "ambiguous-tenants" => new ClaimsPrincipal(new[]
                {
                    TrustedIdentity("tenant-a", "provider-a", "read"),
                    TrustedIdentity("tenant-b", "provider-b", "write")
                }),
                "trusted-plus-untrusted" => new ClaimsPrincipal(new[]
                {
                    Identity(SchemeName, marked: true, "read"),
                    Identity("raw-provider", marked: true, "write")
                }),
                "anonymous-first" => new ClaimsPrincipal(new[]
                {
                    new ClaimsIdentity(),
                    Identity("raw-provider", marked: true, "read")
                }),
                "untrusted-first" => new ClaimsPrincipal(new[]
                {
                    Identity("raw-provider", marked: true, "read"),
                    new ClaimsIdentity()
                }),
                "host-access" => new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("host-access", "true")],
                    "raw-provider")),
                "read" => Principal(SchemeName, marked: true, "read"),
                "capabilities-exact" => Principal(SchemeName, marked: true, DefaultIdentityPermissionKeys.IdentityProvidersRead),
                "capabilities-implied" => Principal(SchemeName, marked: true, DefaultIdentityPermissionKeys.IdentityProvidersManage),
                "projected-read" => new ClaimsPrincipal(TrustedIdentity("tenant-a", "provider-a", "read")),
                "write" => Principal(SchemeName, marked: true, "write"),
                "read-write" => Principal(SchemeName, marked: true, "read", "write"),
                "implied" => Principal(SchemeName, marked: true, DefaultIdentityPermissionKeys.IdentityUsersManage),
                "resource-any" or "resource-timeout" or "evaluator-timeout" or "cancelled" => Principal(SchemeName, marked: true),
                "wildcard" => Principal(SchemeName, marked: true, "*"),
                _ => Principal(SchemeName, marked: true)
            };

            state.AuthenticationTicketIssued = true;
            state.AuthenticationTicketContainsNormalizedMarker = principal.Identities
                .SelectMany(identity => identity.FindAll(IdentityClaimTypes.Normalized))
                .Any();
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }

        private static ClaimsPrincipal Principal(string authenticationType, bool marked, params string[] permissions) =>
            new(Identity(authenticationType, marked, permissions));

        private static ClaimsIdentity Identity(string authenticationType, bool marked, params string[] permissions)
        {
            var claims = permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)).ToList();
            if (marked)
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
            return new ClaimsIdentity(claims, authenticationType);
        }

        private static ClaimsIdentity TrustedIdentity(string tenant, string provider, params string[] permissions)
        {
            var identity = Identity(SchemeName, marked: true, permissions);
            identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, tenant));
            identity.AddClaim(new Claim(IdentityClaimTypes.Provider, provider));
            return identity;
        }
    }
}

public sealed class FastSinglePermissionEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/single");
        ConfigurePermissions("read");
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}

public sealed class FastAnyPermissionEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/any");
        ConfigurePermissions("read", "write");
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}

public sealed class FastAllPermissionEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/all");
        // The transitional base preserves FastEndpoints' action-permission OR contract; all-of is
        // available through the same Foundation policy provider when an endpoint needs it explicitly.
        Policies(new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.All("read", "write")));
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}

public sealed class FastImpliedPermissionEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/implied");
        ConfigurePermissions(DefaultIdentityPermissionKeys.IdentityUsersRead);
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}

public sealed class FastWildcardPermissionEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/wildcard");
        ConfigurePermissions();
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}

public sealed class FastUnrelatedPolicyEndpoint : ElsaEndpointWithoutRequest<string>
{
    public override void Configure()
    {
        Get("/fast/unrelated");
        Policies("host.custom");
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("ok", ct);
}
