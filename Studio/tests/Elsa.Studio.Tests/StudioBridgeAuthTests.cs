using System.Net;
using System.Text.Json;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the real <see cref="StudioBridgeAuth"/> wiring (backend-bearer introspection scheme + policy) end to end
/// against a TestServer that maps probe endpoints behind <see cref="StudioBridgeAuth.PolicyName"/>. This is the single
/// gate for every browser-facing Studio host-control endpoint after ADR 0037 removed the browser-held management key:
/// module/feature/theme management, the console-stream hub, and the Studio management bridge all sit behind it.
///
/// The scheme validates the browser bearer by forwarding it to the backend's <c>/_elsa/identity/session</c> endpoint;
/// these tests route that named client through a stub backend so the assertions are about the gate's HTTP behaviour,
/// not the network. The browser never carries an Elsa host management key on any of these requests.
/// </summary>
public sealed class StudioBridgeAuthTests : IAsyncDisposable
{
    private const string BackendBaseUrl = "https://backend.example/";
    private const string ValidBearer = "valid-user-bearer";

    // Bearers the stub backend recognizes, each mapped to the permission set the backend session reports for it. These
    // exercise the permission policies (#249): a session with no host-control permissions, a read-only holder, a
    // manage holder (which must satisfy read too), and Extension Builder read/manage holders.
    private const string NoPermissionBearer = "user-without-permissions";
    private const string ModuleReadBearer = "user-module-read";
    private const string ModuleManageBearer = "user-module-manage";
    private const string ExtensionBuilderReadBearer = "user-extension-builder-read";
    private const string ExtensionBuilderManageBearer = "user-extension-builder-manage";

    // A path under the query-token prefix (a browser WebSocket/SSE handshake) stands in for the console-stream hub,
    // and a plain REST probe that is not under it.
    private const string HubPath = "/hub";

    // The routes the gated host maps behind each policy, so the permission tests can hit a real endpoint per surface.
    private const string ReadRoute = "/module-management-read";
    private const string ManageRoute = "/module-management-manage";
    private const string ExtensionBuilderReadRoute = "/extension-builder-read";
    private const string ExtensionBuilderManageRoute = "/extension-builder-manage";

    private static readonly IReadOnlyDictionary<string, string[]> BearerPermissions = new Dictionary<string, string[]>
    {
        [ValidBearer] = [],
        [NoPermissionBearer] = [],
        [ModuleReadBearer] = [StudioBridgeAuth.ModuleManagementReadPermission],
        [ModuleManageBearer] = [StudioBridgeAuth.ModuleManagementManagePermission],
        [ExtensionBuilderReadBearer] = [StudioBridgeAuth.ExtensionBuilderReadPermission],
        [ExtensionBuilderManageBearer] = [StudioBridgeAuth.ExtensionBuilderManagePermission]
    };

    private WebApplication? _app;
    private StubBackendSession? _backendStub;

    [Fact]
    public async Task AllowsAnonymouslyWhenStudioAuthDisabled()
    {
        // Demo mode (Studio:Auth:Enabled absent/false): the gate must not break the anonymous shell.
        var client = await StartGatedHostAsync(authEnabled: false);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BaseGateHeaderCases))]
    public async Task BaseGateHonorsBearerHeaderWhenAuthEnabled(string? bearer, HttpStatusCode expected)
    {
        // With auth on, the base gate admits a recognized bearer and rejects a missing or unrecognized one as 401.
        var client = await StartGatedHostAsync(authEnabled: true);
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(expected, response.StatusCode);
    }

    public static TheoryData<string?, HttpStatusCode> BaseGateHeaderCases => new()
    {
        { ValidBearer, HttpStatusCode.OK },
        { "not-a-recognized-token", HttpStatusCode.Unauthorized },
        { null, HttpStatusCode.Unauthorized }
    };

    [Fact]
    public async Task UsesDedicatedServerBaseUrlToValidateBearerWhenConfigured()
    {
        var backendServerBaseUrl = "http://elsa-server:8080";
        var client = await StartGatedHostAsync(authEnabled: true, backendServerBaseUrl: backendServerBaseUrl);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidBearer);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(backendServerBaseUrl, _backendStub?.LastRequestUri?.GetLeftPart(UriPartial.Authority));
    }

    // Browsers cannot attach headers to the WebSocket/SSE handshake, so SignalR falls back to ?access_token=. It is
    // honoured only on the configured hub path; off that path the query credential is ignored so the bearer never lands
    // in the access logs of REST endpoints.
    [Theory]
    [InlineData(HubPath, ValidBearer, HttpStatusCode.OK)]
    [InlineData(HubPath, "not-a-recognized-token", HttpStatusCode.Unauthorized)]
    [InlineData("/gated", ValidBearer, HttpStatusCode.Unauthorized)]
    public async Task AccessTokenQueryParameterHonoredOnlyOnHubPathWhenAuthEnabled(string path, string token, HttpStatusCode expected)
    {
        var client = await StartGatedHostAsync(authEnabled: true);

        var response = await client.PostAsync($"{path}?{StudioBridgeAuth.AccessTokenQueryParameterName}={token}", content: null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task FailsClosedWhenAuthEnabledButNoBackendBaseUrlConfigured()
    {
        // With no backend to validate the bearer against, the enabled gate cannot trust anyone.
        var client = await StartGatedHostAsync(authEnabled: true, backendBaseUrl: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidBearer);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Permission gating (#249) ----------------------------------------------------------------------------------
    // For every gated surface: no bearer + auth enabled → 401; a valid session missing the permission → 403 (distinct
    // from 401); a holder of the read permission → 200 on reads; a manage holder passes reads AND mutations; a read-only
    // holder → 403 on mutations; and auth disabled → everything allowed anonymously (demo posture).

    [Theory]
    [MemberData(nameof(PermissionMatrixCases))]
    public async Task PermissionSurfaceEnforcesItsPolicyWhenAuthEnabled(string method, string route, string? bearer, HttpStatusCode expected)
    {
        var response = await SendWithBearerAsync(new HttpMethod(method), route, bearer);

        Assert.Equal(expected, response.StatusCode);
    }

    // Every gated surface keyed by (method, route, bearer, expected). A read surface returns 401 for no bearer ("not
    // signed in") but 403 for a live session missing the permission ("Studio denied you"); manage implies read, so a
    // manage-only holder passes the read surface. A mutation surface forbids a read-only holder. Extension Builder is
    // independently gated (#256): a module-management holder does not satisfy it, and its manage implies its read.
    public static TheoryData<string, string, string?, HttpStatusCode> PermissionMatrixCases => new()
    {
        { "GET", ReadRoute, null, HttpStatusCode.Unauthorized },
        { "GET", ReadRoute, NoPermissionBearer, HttpStatusCode.Forbidden },
        { "GET", ReadRoute, ModuleReadBearer, HttpStatusCode.OK },
        { "GET", ReadRoute, ModuleManageBearer, HttpStatusCode.OK },
        { "POST", ManageRoute, NoPermissionBearer, HttpStatusCode.Forbidden },
        { "POST", ManageRoute, ModuleReadBearer, HttpStatusCode.Forbidden },
        { "POST", ManageRoute, ModuleManageBearer, HttpStatusCode.OK },
        { "GET", ExtensionBuilderReadRoute, ModuleManageBearer, HttpStatusCode.Forbidden },
        { "GET", ExtensionBuilderReadRoute, ExtensionBuilderReadBearer, HttpStatusCode.OK },
        { "GET", ExtensionBuilderReadRoute, ExtensionBuilderManageBearer, HttpStatusCode.OK },
        { "POST", ExtensionBuilderManageRoute, ExtensionBuilderReadBearer, HttpStatusCode.Forbidden },
        { "POST", ExtensionBuilderManageRoute, ExtensionBuilderManageBearer, HttpStatusCode.OK }
    };

    [Theory]
    [InlineData("GET", ReadRoute)]
    [InlineData("POST", ManageRoute)]
    [InlineData("GET", ExtensionBuilderReadRoute)]
    [InlineData("POST", ExtensionBuilderManageRoute)]
    public async Task PermissionSurfacesAllowAnonymouslyWhenAuthDisabled(string method, string route)
    {
        // Demo/auth-disabled posture: every permission surface allows anonymously, exactly like the base gate, so the
        // anonymous demo shell keeps full access.
        var client = await StartGatedHostAsync(authEnabled: false);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Bearer-introspection cache -----------------------------------------------------------------------------
    // The gate caches completed introspections (default 30s TTL, shorter for negatives, bounded entry count) so a
    // polling frontend (e.g. the Extension Builder build poll at ~900ms) does not cost a backend round-trip per
    // request. These tests drive expiry through the fake TimeProvider and count backend hits via the stub.

    [Fact]
    public async Task RepeatedBearerWithinTtlIntrospectsBackendOnce()
    {
        var client = await StartGatedHostAsync(authEnabled: true);

        var first = await SendGatedAsync(client, ValidBearer);
        var second = await SendGatedAsync(client, ValidBearer);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, _backendStub!.IntrospectionCount);
    }

    [Fact]
    public async Task ExpiredCacheEntryReintrospectsBackend()
    {
        var time = new FakeTimeProvider();
        var client = await StartGatedHostAsync(authEnabled: true, timeProvider: time);

        await SendGatedAsync(client, ValidBearer);
        time.Advance(TimeSpan.FromSeconds(StudioBridgeAuth.DefaultSessionCacheSeconds + 1));
        var afterExpiry = await SendGatedAsync(client, ValidBearer);

        Assert.Equal(HttpStatusCode.OK, afterExpiry.StatusCode);
        Assert.Equal(2, _backendStub!.IntrospectionCount);
    }

    [Fact]
    public async Task RevokedBearerIsRejectedAfterTtlExpiryAtWorst()
    {
        // The staleness contract: a backend-side logout/permission change may be served from cache within the TTL,
        // but MUST take effect once the entry expires.
        var time = new FakeTimeProvider();
        var client = await StartGatedHostAsync(authEnabled: true, timeProvider: time);

        await SendGatedAsync(client, ValidBearer);
        _backendStub!.RevokeBearer(ValidBearer);

        var withinTtl = await SendGatedAsync(client, ValidBearer);
        Assert.Equal(HttpStatusCode.OK, withinTtl.StatusCode);

        time.Advance(TimeSpan.FromSeconds(StudioBridgeAuth.DefaultSessionCacheSeconds + 1));
        var afterExpiry = await SendGatedAsync(client, ValidBearer);
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);
    }

    [Fact]
    public async Task RejectedBearerIsNegativelyCachedWithShorterTtl()
    {
        // Negative caching absorbs a rejected-token stampede, but with a shorter TTL (5s ceiling) so a fresh login
        // is not locked out for the full positive window.
        var time = new FakeTimeProvider();
        var client = await StartGatedHostAsync(authEnabled: true, timeProvider: time);

        var first = await SendGatedAsync(client, "not-a-recognized-token");
        var cached = await SendGatedAsync(client, "not-a-recognized-token");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cached.StatusCode);
        Assert.Equal(1, _backendStub!.IntrospectionCount);

        time.Advance(TimeSpan.FromSeconds(6));
        await SendGatedAsync(client, "not-a-recognized-token");
        Assert.Equal(2, _backendStub.IntrospectionCount);
    }

    [Fact]
    public async Task SessionCacheZeroSecondsDisablesCaching()
    {
        var client = await StartGatedHostAsync(authEnabled: true, sessionCacheSeconds: 0);

        await SendGatedAsync(client, ValidBearer);
        await SendGatedAsync(client, ValidBearer);

        Assert.Equal(2, _backendStub!.IntrospectionCount);
    }

    [Fact]
    public async Task SessionCacheCapEvictsEarliestExpiringEntry()
    {
        // Cap 2, three distinct bearers arriving a second apart: inserting the third evicts the earliest-expiring
        // entry (the first bearer), so re-presenting the first re-introspects while the still-cached third does not.
        var time = new FakeTimeProvider();
        var client = await StartGatedHostAsync(authEnabled: true, sessionCacheMaxEntries: 2, timeProvider: time);

        await SendGatedAsync(client, ValidBearer);
        time.Advance(TimeSpan.FromSeconds(1));
        await SendGatedAsync(client, NoPermissionBearer);
        time.Advance(TimeSpan.FromSeconds(1));
        await SendGatedAsync(client, ModuleReadBearer);

        var evicted = await SendGatedAsync(client, ValidBearer);
        var stillCached = await SendGatedAsync(client, ModuleReadBearer);

        Assert.Equal(HttpStatusCode.OK, evicted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillCached.StatusCode);
        Assert.Equal(2, _backendStub!.IntrospectionCountFor(ValidBearer));
        Assert.Equal(1, _backendStub.IntrospectionCountFor(NoPermissionBearer));
        Assert.Equal(1, _backendStub.IntrospectionCountFor(ModuleReadBearer));
    }

    [Fact]
    public async Task BackendServerErrorIsNotCachedAndFailsOnlyTheOneRequest()
    {
        // An indeterminate backend answer (5xx) must fail exactly the request that saw it: caching it as a negative
        // would serve replayed 401s to a valid bearer for the negative TTL after a single backend blip.
        var client = await StartGatedHostAsync(authEnabled: true);
        _backendStub!.OverrideNextResponse(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var duringBlip = await SendGatedAsync(client, ValidBearer);
        var afterRecovery = await SendGatedAsync(client, ValidBearer);

        Assert.Equal(HttpStatusCode.Unauthorized, duringBlip.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterRecovery.StatusCode);
        Assert.Equal(2, _backendStub.IntrospectionCount);
    }

    [Fact]
    public async Task NonJsonBackendResponseFailsAuthenticationAndIsNotCached()
    {
        // A 200 whose body is not the session contract (proxy error page, SPA fallback) must map to 401, not bubble a
        // JsonException out of the authentication pipeline as a 500 — and must not be cached.
        var client = await StartGatedHostAsync(authEnabled: true);
        _backendStub!.OverrideNextResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!doctype html><html>error page</html>", System.Text.Encoding.UTF8, "text/html")
        });

        var duringBadPayload = await SendGatedAsync(client, ValidBearer);
        var afterRecovery = await SendGatedAsync(client, ValidBearer);

        Assert.Equal(HttpStatusCode.Unauthorized, duringBadPayload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterRecovery.StatusCode);
        Assert.Equal(2, _backendStub.IntrospectionCount);
    }

    [Fact]
    public async Task NegativeEntryIsNotEvictedByCapWhenCacheIsFullOfFresherPositives()
    {
        // The just-written entry is exempt from cap eviction: a 5s negative among 30s positives is always the
        // earliest-expiring, and evicting it on insert would defeat negative caching exactly under load.
        var client = await StartGatedHostAsync(authEnabled: true, sessionCacheMaxEntries: 2);

        await SendGatedAsync(client, ValidBearer);
        await SendGatedAsync(client, NoPermissionBearer);

        var rejectedFirst = await SendGatedAsync(client, "not-a-recognized-token");
        var rejectedSecond = await SendGatedAsync(client, "not-a-recognized-token");

        Assert.Equal(HttpStatusCode.Unauthorized, rejectedFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedSecond.StatusCode);
        Assert.Equal(1, _backendStub!.IntrospectionCountFor("not-a-recognized-token"));
    }

    [Fact]
    public async Task NonPositiveMaxEntriesFallsBackToDefaultBoundInsteadOfDisablingCache()
    {
        // "0 = no cap" is a natural misreading of the cap key; it must not silently reinstate the per-request
        // introspection storm. Disabling the cache is SessionCacheSeconds' documented job.
        var client = await StartGatedHostAsync(authEnabled: true, sessionCacheMaxEntries: 0);

        await SendGatedAsync(client, ValidBearer);
        await SendGatedAsync(client, ValidBearer);

        Assert.Equal(1, _backendStub!.IntrospectionCount);
    }

    private static Task<HttpResponseMessage> SendGatedAsync(HttpClient client, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/gated");
        request.Headers.Authorization = new("Bearer", bearer);
        return client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(HttpMethod method, string route, string? bearer)
    {
        var client = await StartGatedHostAsync(authEnabled: true);
        var request = new HttpRequestMessage(method, route);
        if (bearer is not null)
            request.Headers.Authorization = new("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private async Task<HttpClient> StartGatedHostAsync(
        bool authEnabled,
        string? backendBaseUrl = BackendBaseUrl,
        string? backendServerBaseUrl = null,
        int? sessionCacheSeconds = null,
        int? sessionCacheMaxEntries = null,
        TimeProvider? timeProvider = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Studio:Auth:Enabled"] = authEnabled ? "true" : "false",
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl,
            [StudioBackendManagementOptions.BackendServerBaseUrlConfigurationKey] = backendServerBaseUrl,
            [StudioBridgeAuth.SessionCacheSecondsConfigurationKey] = sessionCacheSeconds?.ToString(),
            [StudioBridgeAuth.SessionCacheMaxEntriesConfigurationKey] = sessionCacheMaxEntries?.ToString()
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        // A fake clock (registered before AddStudioBridgeAuth's TryAdd of TimeProvider.System) lets the cache tests
        // drive TTL expiry without wall-clock sleeps.
        if (timeProvider is not null)
            builder.Services.AddSingleton(timeProvider);
        builder.Services.AddStudioBridgeAuth(builder.Configuration, HubPath);

        // Route the bearer-introspection client through a stub backend session endpoint that recognizes the known
        // bearers and reflects their permission sets, so the tests assert real gate behaviour (including the permission
        // projection) without a network dependency. The stub counts introspections per bearer so the cache tests can
        // assert exactly when the gate goes back to the backend. The handler factory captures the local (not the
        // field): HttpClientFactory evaluates it lazily on the first request, so a field read would bind a host to
        // whichever stub a later-started host wrote last.
        var backendStub = new StubBackendSession(BearerPermissions);
        _backendStub = backendStub;
        builder.Services.AddHttpClient(StudioBridgeAuthHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => backendStub);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // Two probes behind the base session policy: "/gated" is a plain REST endpoint, "/hub" sits under the
        // query-token prefix and stands in for the console-stream hub's WebSocket/SSE handshake.
        app.MapPost("/gated", () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.PolicyName);
        app.MapPost(HubPath, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.PolicyName);
        // One probe per permission policy (#249, #256): the read/manage surfaces of both permission families, so a
        // single host exercises the whole permission matrix against the real policies.
        app.MapGet(ReadRoute, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementReadPolicyName);
        app.MapPost(ManageRoute, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementManagePolicyName);
        app.MapGet(ExtensionBuilderReadRoute, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.ExtensionBuilderReadPolicyName);
        app.MapPost(ExtensionBuilderManageRoute, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.ExtensionBuilderManagePolicyName);

        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>
    /// Stands in for the backend's anonymous <c>/_elsa/identity/session</c> endpoint: it reflects an
    /// <c>authenticated</c> session (with the bearer's permission set) only when the forwarded bearer is recognized,
    /// otherwise <c>anonymous</c>. Mirrors the real endpoint's contract the gate depends on — including the flat
    /// camelCase <c>permissions</c> array the gate projects onto the ticket (#249). It counts introspections (total and
    /// per bearer) so the cache tests can assert exactly when the gate goes back to the backend, and a bearer can be
    /// revoked mid-test to simulate a logout/permission change on the backend.
    /// </summary>
    private sealed class StubBackendSession(IReadOnlyDictionary<string, string[]> bearerPermissions) : HttpMessageHandler
    {
        private readonly Dictionary<string, string[]> _bearerPermissions = new(bearerPermissions);
        private readonly Dictionary<string, int> _introspectionsByBearer = new();
        private readonly object _gate = new();

        public int IntrospectionCount
        {
            get { lock (_gate) return _introspectionsByBearer.Values.Sum(); }
        }

        public Uri? LastRequestUri { get; private set; }

        public int IntrospectionCountFor(string bearer)
        {
            lock (_gate) return _introspectionsByBearer.GetValueOrDefault(bearer);
        }

        /// <summary>The backend no longer recognizes the bearer — subsequent introspections reflect <c>anonymous</c>.</summary>
        public void RevokeBearer(string bearer)
        {
            lock (_gate) _bearerPermissions.Remove(bearer);
        }

        /// <summary>Overrides the NEXT introspection response only — e.g. a 503 or a non-JSON 200 — to exercise the
        /// gate's handling of indeterminate backend answers (which must fail one request, never be cached).</summary>
        public void OverrideNextResponse(Func<HttpResponseMessage> response)
        {
            lock (_gate) _nextResponseOverride = response;
        }

        private Func<HttpResponseMessage>? _nextResponseOverride;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var forwarded = request.Headers.Authorization?.Parameter;
            Func<HttpResponseMessage>? overrideResponse;
            string json;
            lock (_gate)
            {
                _introspectionsByBearer[forwarded ?? ""] = _introspectionsByBearer.GetValueOrDefault(forwarded ?? "") + 1;
                overrideResponse = _nextResponseOverride;
                _nextResponseOverride = null;
                json = forwarded is not null && _bearerPermissions.TryGetValue(forwarded, out var permissions)
                    ? JsonSerializer.Serialize(new { status = "authenticated", subject = "alice", permissions })
                    : """{ "status": "anonymous" }""";
            }

            if (overrideResponse is not null)
                return Task.FromResult(overrideResponse());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

}
