using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
    IHttpClientFactory httpClientFactory)
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

        try
        {
            var session = await ReadBackendSessionAsync(bearer, Context.RequestAborted);
            return session is { IsAuthenticated: true }
                ? Success(session.Subject ?? "backend-user", session.Permissions)
                : AuthenticateResult.Fail("The backend did not recognize the supplied user session.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !Context.RequestAborted.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Studio could not validate the browser bearer against the backend session endpoint.");
            return AuthenticateResult.Fail("The user session could not be validated against the backend.");
        }
    }

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
        var session = await JsonSerializer.DeserializeAsync<BackendSession>(stream, SessionJsonOptions, cancellationToken);
        return session;
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
