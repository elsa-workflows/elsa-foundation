using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Web;

/// <summary>
/// Browser-request authorization for the Studio management surface (ADR 0037). This is the single gate for every
/// browser-facing Studio host-control endpoint: the Studio management bridge, Studio's own module/feature/theme
/// management endpoints, and the console-stream hub. Studio's server has no user identity scheme of its own — user
/// auth is backend-delegated — so this scheme validates the backend-issued bearer the browser already holds by
/// forwarding it to the backend's anonymous <c>/_elsa/identity/session</c> endpoint and treating an
/// <c>authenticated</c> session as a valid Studio user.
///
/// <para>Dev/demo ergonomics: when Studio auth is disabled (<c>Studio:Auth:Enabled</c> false or absent) the gate
/// allows the request anonymously so the anonymous demo shell keeps working. When Studio auth is enabled an
/// unauthenticated request is rejected with 401.</para>
///
/// <para>Browsers cannot attach custom headers to WebSocket/EventSource handshakes, so the SignalR client sends its
/// bearer as the <c>access_token</c> query parameter (the standard SignalR convention). That credential is honoured
/// only on the paths registered in <see cref="StudioBridgeAuthOptions.QueryTokenPathPrefixes"/> (the console-stream
/// hub) — a query-string credential lands in proxy/access logs, so every other endpoint takes the bearer by header
/// only. This mirrors the standard JwtBearer <c>OnMessageReceived</c> pattern.</para>
///
/// <para>Beyond proving the caller holds a live backend session, the gate carries the user's host-control permissions
/// (#249, ADR 0037): the backend session response already lists the user's permissions, so the introspection reads them
/// and projects them onto the ticket as <c>elsa.identity.permission</c> claims. Named permission policies
/// (<see cref="ModuleManagementReadPolicyName"/>, <see cref="ModuleManagementManagePolicyName"/>,
/// <see cref="ExtensionBuilderReadPolicyName"/>, <see cref="ExtensionBuilderManagePolicyName"/>) then gate the
/// individual surfaces. A signed-in user who lacks the
/// required permission is <b>forbidden (403)</b> — distinct from an unauthenticated <b>401</b> and from the bridge's
/// backend-status states (<c>unconfigured</c>/<c>unreachable</c>/<c>unauthorized</c>).</para>
///
/// <para><b>Demo/auth-disabled posture:</b> when Studio auth is disabled (<c>Studio:Auth:Enabled</c> false/absent) every
/// permission policy allows anonymously, exactly like the base gate — the anonymous demo shell keeps full access. All
/// permission enforcement below applies <i>only</i> when Studio auth is enabled.</para>
/// </summary>
internal static class StudioBridgeAuth
{
    /// <summary>The authorization policy applied to bridge and Studio host-control endpoints. Stable seam a host can rebind.</summary>
    public const string PolicyName = "StudioManagementBridge";

    /// <summary>
    /// Policy for host-control READ surfaces (bridge status/registry, Studio's own module/feature READ endpoints).
    /// Requires <c>module-management.read</c>; a holder of <c>module-management.manage</c> satisfies it too (locally
    /// expanded — Studio never relies on the backend to expand the implication).
    /// </summary>
    public const string ModuleManagementReadPolicyName = "StudioManagementBridge:ModuleManagement.Read";

    /// <summary>
    /// Policy for Studio's own module-management MUTATION endpoints (upload, delete, reconcile, prune, feed CRUD,
    /// retention, feature apply). Requires <c>module-management.manage</c>. A mere authenticated session is not enough.
    /// </summary>
    public const string ModuleManagementManagePolicyName = "StudioManagementBridge:ModuleManagement.Manage";

    /// <summary>
    /// Policy for the bridge's Extension Builder capabilities READ. Requires <c>extension-builder.read</c>; a holder of
    /// <c>extension-builder.manage</c> satisfies it too (locally expanded).
    /// </summary>
    public const string ExtensionBuilderReadPolicyName = "StudioManagementBridge:ExtensionBuilder.Read";

    /// <summary>
    /// Policy for the bridge's Extension Builder MUTATION relays (workspace, file, source-control, build, and promote
    /// operations, #256). Requires <c>extension-builder.manage</c>. A mere authenticated session is not enough.
    /// </summary>
    public const string ExtensionBuilderManagePolicyName = "StudioManagementBridge:ExtensionBuilder.Manage";

    // Host-control permission keys owned by the backend features (mirrored here from
    // Elsa.Modularity.Api.Authorization.ModuleManagementPermissionKeys and
    // Elsa.Modularity.ExtensionBuilder.Authorization.ExtensionBuilderPermissionKeys). These string values are the
    // stable identity-permission contract; Studio only needs the keys to check the user's permission claims.
    public const string ModuleManagementReadPermission = "module-management.read";
    public const string ModuleManagementManagePermission = "module-management.manage";
    public const string ExtensionBuilderReadPermission = "extension-builder.read";
    public const string ExtensionBuilderManagePermission = "extension-builder.manage";

    /// <summary>
    /// The backend identity claim type carrying a granted permission key. Matches
    /// <c>Elsa.Foundation.Identity.Abstractions.Authorization.IdentityClaimTypes.Permission</c>; the introspection
    /// projects each permission from the backend session onto a claim of this type.
    /// </summary>
    public const string PermissionClaimType = "elsa.identity.permission";

    /// <summary>The authentication scheme backing the built-in bearer-introspection gate.</summary>
    public const string SchemeName = "StudioManagementBridgeAuth";

    /// <summary>
    /// The query-string parameter carrying the browser bearer on the console-stream hub handshake. See the class
    /// remarks for why a query credential is accepted at all and why it is scoped to the hub path only.
    /// </summary>
    public const string AccessTokenQueryParameterName = "access_token";

    private const string AuthEnabledConfigurationKey = "Studio:Auth:Enabled";

    /// <summary>
    /// TTL, in seconds, of the in-memory bearer-introspection cache (<c>0</c> disables caching entirely). Every bridge
    /// request re-validates the browser bearer against the backend session endpoint; without a cache the Extension
    /// Builder build poll alone produces ~2 backend round-trips per second per user. The TTL bounds staleness: a
    /// permission change or logout takes effect within this window at worst.
    /// </summary>
    public const string SessionCacheSecondsConfigurationKey = "Studio:Auth:SessionCacheSeconds";

    /// <summary>Upper bound on cached introspection entries, so a flood of unique (e.g. forged) bearers cannot grow
    /// memory unboundedly. When full, the entry closest to expiry is evicted first. Values below 1 fall back to the
    /// default bound — disabling the cache is <see cref="SessionCacheSecondsConfigurationKey"/>'s job (set it to 0).</summary>
    public const string SessionCacheMaxEntriesConfigurationKey = "Studio:Auth:SessionCacheMaxEntries";

    public const int DefaultSessionCacheSeconds = 30;
    public const int DefaultSessionCacheMaxEntries = 1000;

    public static IServiceCollection AddStudioBridgeAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] queryTokenPathPrefixes)
    {
        // When Studio auth is disabled/absent the shell boots anonymously; the gate must not break that path. When
        // enabled, an authenticated backend session is required. This mirrors how `auth.enabled` is derived for the
        // browser runtime config in Program.cs.
        var authEnabled = configuration.GetValue(AuthEnabledConfigurationKey, defaultValue: false);
        var backendBaseUrl = configuration[StudioBackendManagementOptions.BackendBaseUrlConfigurationKey];

        // Named client the handler uses to introspect the browser bearer. Short timeout so an unauthenticated request
        // to a slow backend still resolves quickly.
        services.AddHttpClient(StudioBridgeAuthHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            if (!string.IsNullOrWhiteSpace(backendBaseUrl))
                client.BaseAddress = new Uri(backendBaseUrl, UriKind.Absolute);
        });

        // The introspection cache (see StudioBridgeSessionCache). TimeProvider is TryAdd'd so tests (and the bridge
        // registration, which TryAdds the same) can substitute a fake clock to drive expiry.
        var sessionCacheSeconds = configuration.GetValue(SessionCacheSecondsConfigurationKey, DefaultSessionCacheSeconds);
        var sessionCacheMaxEntries = configuration.GetValue(SessionCacheMaxEntriesConfigurationKey, DefaultSessionCacheMaxEntries);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(serviceProvider => new StudioBridgeSessionCache(
            serviceProvider.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(Math.Max(0, sessionCacheSeconds)),
            // "0 = no cap" is a natural misreading of the cap key; a non-positive value falls back to the default
            // bound rather than silently disabling the cache — disabling is the Seconds key's documented job.
            sessionCacheMaxEntries > 0 ? sessionCacheMaxEntries : DefaultSessionCacheMaxEntries));

        services.AddAuthentication()
            .AddScheme<StudioBridgeAuthOptions, StudioBridgeAuthHandler>(SchemeName, schemeOptions =>
            {
                schemeOptions.AuthEnabled = authEnabled;
                schemeOptions.BackendBaseUrl = backendBaseUrl;
                schemeOptions.QueryTokenPathPrefixes = queryTokenPathPrefixes;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(SchemeName);
                policy.RequireAuthenticatedUser();
            })
            // Each host-control policy requires the base authenticated session PLUS the surface's permission. When
            // Studio auth is disabled the requirement passes anonymously (authEnabled captured below), so the demo shell
            // keeps full access — permission enforcement is an auth-enabled-only concern.
            .AddPolicy(ModuleManagementReadPolicyName, policy =>
                ConfigurePermissionPolicy(policy, authEnabled, ModuleManagementReadPermission, ModuleManagementManagePermission))
            .AddPolicy(ModuleManagementManagePolicyName, policy =>
                ConfigurePermissionPolicy(policy, authEnabled, ModuleManagementManagePermission))
            .AddPolicy(ExtensionBuilderReadPolicyName, policy =>
                ConfigurePermissionPolicy(policy, authEnabled, ExtensionBuilderReadPermission, ExtensionBuilderManagePermission))
            .AddPolicy(ExtensionBuilderManagePolicyName, policy =>
                ConfigurePermissionPolicy(policy, authEnabled, ExtensionBuilderManagePermission));

        return services;
    }

    /// <summary>
    /// Builds a host-control permission policy: the base bearer-introspection scheme, an authenticated user, and (only
    /// when Studio auth is enabled) at least one of <paramref name="satisfyingPermissions"/> present as an
    /// <c>elsa.identity.permission</c> claim. Passing more than one key is how implication is expanded <i>locally</i>:
    /// e.g. a read surface lists both <c>read</c> and <c>manage</c>, so a <c>manage</c>-only holder satisfies the read
    /// gate without Studio depending on the backend to expand <c>manage ⇒ read</c>. A signed-in user missing every key
    /// fails the requirement while remaining authenticated, so ASP.NET returns 403 (not 401).
    /// </summary>
    private static void ConfigurePermissionPolicy(
        AuthorizationPolicyBuilder policy,
        bool authEnabled,
        params string[] satisfyingPermissions)
    {
        policy.AddAuthenticationSchemes(SchemeName);
        policy.RequireAuthenticatedUser();

        // Demo mode: the base authenticated-user check already passes anonymously (the gate issues an anonymous ticket),
        // so add no permission requirement — the surface stays fully open, matching the base policy's posture.
        if (!authEnabled)
            return;

        policy.RequireAssertion(context =>
            satisfyingPermissions.Any(permission =>
                context.User.HasClaim(PermissionClaimType, permission)));
    }
}

internal sealed class StudioBridgeAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>When false, every request is authenticated anonymously so the anonymous demo shell keeps working.</summary>
    public bool AuthEnabled { get; set; }

    /// <summary>Backend base URL the incoming bearer is validated against. When absent, the enabled gate fails closed.</summary>
    public string? BackendBaseUrl { get; set; }

    /// <summary>
    /// Request-path prefixes on which the <c>access_token</c> query parameter is honoured (segment-aware,
    /// ordinal-ignore-case). Empty by default so the query credential is rejected everywhere; the console-stream
    /// hub path is added at registration. See <see cref="StudioBridgeAuth.AccessTokenQueryParameterName"/>.
    /// </summary>
    public string[] QueryTokenPathPrefixes { get; set; } = [];
}

/// <summary>
/// Validates the browser's bearer against the backend's <c>/_elsa/identity/session</c> endpoint. The endpoint is
/// anonymous and reflects the caller's identity: forwarding the bearer yields <c>{ "status": "authenticated", ... }</c>
/// for a valid token and <c>{ "status": "anonymous", ... }</c> otherwise. The bearer is read from the
/// <c>Authorization: Bearer</c> header, or (only on the configured hub paths) from the <c>access_token</c> query
/// parameter for the SignalR WebSocket/SSE handshake.
/// </summary>
internal sealed class StudioBridgeAuthHandler(
    IOptionsMonitor<StudioBridgeAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHttpClientFactory httpClientFactory,
    StudioBridgeSessionCache sessionCache)
    : AuthenticationHandler<StudioBridgeAuthOptions>(options, logger, encoder)
{
    /// <summary>The named HttpClient used to introspect the browser bearer against the backend session endpoint.</summary>
    public const string HttpClientName = "StudioBridgeAuth";

    private const string SessionPath = "/_elsa/identity/session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Anonymous demo mode: no user auth configured, so let the request through as an anonymous principal.
        if (!Options.AuthEnabled)
            return Success("anonymous");

        var bearer = ResolveBearer();
        if (string.IsNullOrWhiteSpace(bearer))
            return AuthenticateResult.Fail("An authenticated Studio user is required.");

        if (string.IsNullOrWhiteSpace(Options.BackendBaseUrl))
        {
            Logger.LogWarning("Studio:Auth:Enabled is true but Studio:BackendBaseUrl is not configured; the bridge cannot validate the browser bearer.");
            return AuthenticateResult.Fail("Studio cannot validate the user session because no backend base URL is configured.");
        }

        // Cache both definitive outcomes of an introspection: a recognized session (positive) spares the per-request
        // backend round-trip (the Extension Builder build poll alone is ~2 introspections/second per user), and an
        // explicit anonymous session (negative, shorter TTL) absorbs repeated introspections of a rejected token.
        // Concurrent first misses still introspect independently — bounded by the in-flight request count, accepted
        // here over per-key single-flight complexity.
        if (sessionCache.TryGet(bearer, out var cached))
            return ToResult(cached);

        try
        {
            var session = await ReadBackendSessionAsync(bearer, Context.RequestAborted);
            var outcome = new StudioBridgeCachedSession(
                session is { IsAuthenticated: true },
                session?.Subject,
                session?.Permissions ?? []);

            // Only a definitive backend session (authenticated or anonymous) is cacheable. A null session means the
            // backend answered but yielded no session — a 5xx, an unauthorized response, or an unparseable body — an
            // indeterminate result that must fail only this request: replaying it would serve cached 401s to a valid
            // bearer for the negative TTL after a single backend blip. Transport failures below are likewise uncached.
            if (session is not null)
                sessionCache.Set(bearer, outcome);
            return ToResult(outcome);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !Context.RequestAborted.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Studio could not validate the browser bearer against the backend session endpoint.");
            return AuthenticateResult.Fail("The user session could not be validated against the backend.");
        }
    }

    private AuthenticateResult ToResult(StudioBridgeCachedSession session) =>
        session.IsAuthenticated
            ? Success(session.Subject ?? "backend-user", session.Permissions)
            : AuthenticateResult.Fail("The backend did not recognize the supplied user session.");

    /// <summary>
    /// Reads the browser bearer from the <c>Authorization: Bearer</c> header, or from the <c>access_token</c> query
    /// parameter on a configured hub path (the SignalR WebSocket/SSE handshake cannot carry headers).
    /// </summary>
    private string? ResolveBearer()
    {
        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return authorization[bearerPrefix.Length..].Trim();

        if (IsQueryTokenPathAllowed() && Request.Query.TryGetValue(StudioBridgeAuth.AccessTokenQueryParameterName, out var queryToken))
        {
            var token = queryToken.ToString();
            if (!string.IsNullOrWhiteSpace(token))
                return token.Trim();
        }

        return null;
    }

    private bool IsQueryTokenPathAllowed() =>
        Options.QueryTokenPathPrefixes.Any(prefix => Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private async Task<BackendSession?> ReadBackendSessionAsync(string bearer, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        httpClient.BaseAddress ??= new Uri(Options.BackendBaseUrl!, UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Get, SessionPath);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonSerializer.DeserializeAsync<BackendSession>(stream, SessionJsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            // A 200 whose body is not the session contract (e.g. a proxy error page or SPA fallback served on the
            // session path) must fail this one request as unauthenticated — letting the exception escape would surface
            // a 500 out of the authentication pipeline instead of a 401.
            Logger.LogWarning(ex, "The backend session endpoint returned a response that could not be parsed as a session.");
            return null;
        }
    }

    // Anonymous demo mode carries no permissions; the permission policies allow anonymously anyway (see
    // ConfigurePermissionPolicy), so an empty permission set here is correct.
    private AuthenticateResult Success(string name, IReadOnlyCollection<string>? permissions = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };

        // Project the user's host-control permissions from the backend session onto the ticket so the named permission
        // policies can evaluate them (#249, ADR 0037). The backend session is the single source of truth for the
        // caller's permissions; Studio never re-derives them.
        foreach (var permission in permissions ?? [])
        {
            if (!string.IsNullOrWhiteSpace(permission))
                claims.Add(new Claim(StudioBridgeAuth.PermissionClaimType, permission));
        }

        var identity = new ClaimsIdentity(claims, StudioBridgeAuth.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);

    // Mirrors the backend AuthSession contract's fields the gate needs: `status` (authenticated/anonymous), `subject`,
    // and the flat `permissions` array the backend already emits (camelCase over the wire).
    private sealed record BackendSession(string? Status, string? Subject, IReadOnlyList<string>? Permissions)
    {
        public bool IsAuthenticated => string.Equals(Status, "authenticated", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// A completed bearer-introspection outcome as the cache stores it: authenticated-or-not plus the subject and
/// permission set the gate projects onto the ticket. Anonymous/invalid outcomes are cached too (with
/// <see cref="StudioBridgeSessionCache"/>'s shorter negative TTL) so a rejected token cannot stampede the backend.
/// </summary>
internal sealed record StudioBridgeCachedSession(bool IsAuthenticated, string? Subject, IReadOnlyList<string> Permissions);

/// <summary>
/// Bounded, short-TTL in-memory cache for the gate's bearer introspections. Every browser-facing bridge request
/// re-validates its bearer against the backend session endpoint; the Extension Builder build poll alone re-reads build
/// status and log every ~900ms, so without a cache each polling user costs ~2 backend round-trips per second. The TTL
/// (<see cref="StudioBridgeAuth.SessionCacheSecondsConfigurationKey"/>, default 30s, 0 = disabled) bounds staleness:
/// a permission change or logout takes effect within the TTL at worst. Negative (anonymous/invalid) outcomes use the
/// shorter of the TTL and <see cref="NegativeTtlCeiling"/> so a fresh login is not rejected for long while still
/// absorbing repeated introspections of a rejected token.
///
/// <para>Entries are keyed by a SHA-256 hash of the bearer — the raw token is never stored or logged. The entry count
/// is capped (<see cref="StudioBridgeAuth.SessionCacheMaxEntriesConfigurationKey"/>, default 1000; the registration
/// guarantees a positive value) so a flood of unique tokens cannot grow memory unboundedly; when full, expired entries
/// are purged first, then the entry closest to expiry is evicted — never the entry just written (see
/// <see cref="EvictLocked"/>). Reads and writes take a plain lock — at bridge request rates contention is
/// negligible.</para>
/// </summary>
internal sealed class StudioBridgeSessionCache(TimeProvider timeProvider, TimeSpan ttl, int maxEntries)
{
    private static readonly TimeSpan NegativeTtlCeiling = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _negativeTtl = ttl < NegativeTtlCeiling ? ttl : NegativeTtlCeiling;
    private readonly Dictionary<string, (StudioBridgeCachedSession Session, DateTimeOffset ExpiresAt)> _entries = new();
    private readonly Lock _gate = new();

    private bool Enabled => ttl > TimeSpan.Zero;

    public bool TryGet(string bearer, out StudioBridgeCachedSession session)
    {
        session = null!;
        if (!Enabled)
            return false;

        var key = KeyFor(bearer);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return false;

            if (entry.ExpiresAt <= timeProvider.GetUtcNow())
            {
                _entries.Remove(key);
                return false;
            }

            session = entry.Session;
            return true;
        }
    }

    public void Set(string bearer, StudioBridgeCachedSession session)
    {
        if (!Enabled)
            return;

        var key = KeyFor(bearer);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + (session.IsAuthenticated ? ttl : _negativeTtl);
        lock (_gate)
        {
            _entries[key] = (session, expiresAt);
            if (_entries.Count > maxEntries)
                EvictLocked(now, key);
        }
    }

    // Cap enforcement — the count can only ever overshoot by the single entry Set just wrote, so at most one live
    // eviction is needed. One pass removes expired entries in place (Dictionary permits Remove during enumeration)
    // while tracking the earliest-expiring survivor as the eviction victim. The just-written key is exempt: a
    // short-TTL negative entry is always the earliest-expiring when the cache is saturated with fresher positives,
    // and evicting it on insert would defeat negative caching exactly under the load the cap exists for.
    private void EvictLocked(DateTimeOffset now, string justWrittenKey)
    {
        string? victim = null;
        var earliest = DateTimeOffset.MaxValue;
        foreach (var (key, entry) in _entries)
        {
            if (key == justWrittenKey)
                continue;

            if (entry.ExpiresAt <= now)
                _entries.Remove(key);
            else if (entry.ExpiresAt < earliest)
                (victim, earliest) = (key, entry.ExpiresAt);
        }

        if (_entries.Count > maxEntries && victim is not null)
            _entries.Remove(victim);
    }

    // The raw bearer must never sit in memory as a lookup key (defense in depth against heap dumps); a SHA-256 hash
    // is collision-safe as an identity and cheap at bridge request rates.
    private static string KeyFor(string bearer) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearer)));
}
