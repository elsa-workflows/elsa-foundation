using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Web;

/// <summary>
/// Authentication and authorization wiring that gates the Studio management surface (module management,
/// feature management, console-stream). The gate is intentionally a thin API-key scheme validating
/// <c>Studio:BackendModuleManagementApiKey</c>; a host that wants a real identity scheme can register its
/// own authentication and reuse <see cref="PolicyName"/> as the seam without touching the endpoint mappings.
/// </summary>
internal static class ModuleManagementAuth
{
    /// <summary>The authorization policy applied to every management endpoint. This is the stable seam a host can rebind.</summary>
    public const string PolicyName = "ModuleManagementApi";

    /// <summary>The authentication scheme backing the built-in API-key gate.</summary>
    public const string SchemeName = "ModuleManagementApiKey";

    /// <summary>The request header carrying the management API key. Matches the header the Studio SPA already attaches.</summary>
    public const string ApiKeyHeaderName = "X-Elsa-Module-Management-Key";

    /// <summary>
    /// The query-string parameter carrying the management API key. Browsers cannot attach custom headers to
    /// WebSocket or EventSource requests, so the SignalR client sends its access token as <c>access_token</c>
    /// (the standard SignalR convention) — without this fallback the console-stream hub's WebSocket handshake
    /// is rejected and every connection degrades to long polling. Because a query-string credential lands in
    /// proxy and access logs, it is accepted only on the paths listed in
    /// <see cref="ModuleManagementApiKeyOptions.QueryTokenPathPrefixes"/> (the hub path), mirroring the standard
    /// JwtBearer <c>OnMessageReceived</c> pattern; every other management endpoint takes the key by header only.
    /// </summary>
    public const string AccessTokenQueryParameterName = "access_token";

    private const string ApiKeyConfigurationKey = "Studio:BackendModuleManagementApiKey";
    private const string AllowAnonymousConfigurationKey = "Studio:AllowAnonymousManagementApi";

    public static IServiceCollection AddModuleManagementAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] queryTokenPathPrefixes)
    {
        var apiKey = configuration[ApiKeyConfigurationKey];
        var allowAnonymous = configuration.GetValue(AllowAnonymousConfigurationKey, defaultValue: false);

        services.AddAuthentication(SchemeName)
            .AddScheme<ModuleManagementApiKeyOptions, ModuleManagementApiKeyHandler>(SchemeName, options =>
            {
                options.ApiKey = apiKey;
                options.AllowAnonymous = allowAnonymous;
                options.QueryTokenPathPrefixes = queryTokenPathPrefixes;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(SchemeName);
                policy.RequireAuthenticatedUser();
            });

        return services;
    }
}

internal sealed class ModuleManagementApiKeyOptions : AuthenticationSchemeOptions
{
    /// <summary>The expected API key. When null/empty the gate falls back to <see cref="AllowAnonymous"/>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Development opt-out. When true and no API key is configured, requests are authenticated anonymously so a
    /// fresh local checkout keeps working. Defaulted true only in <c>appsettings.Development.json</c>; it fails
    /// closed everywhere else.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// Request-path prefixes on which the <c>access_token</c> query parameter is honoured (segment-aware,
    /// ordinal-ignore-case). Empty by default so the query credential is rejected everywhere; the console-stream
    /// hub path is added at registration. See <see cref="ModuleManagementAuth.AccessTokenQueryParameterName"/>.
    /// </summary>
    public string[] QueryTokenPathPrefixes { get; set; } = [];
}

/// <summary>
/// Validates the management API key supplied via <see cref="ModuleManagementAuth.ApiKeyHeaderName"/>, an
/// <c>Authorization: Bearer</c> value, or (only on the configured hub paths) the <c>access_token</c> query
/// parameter. Succeeds if any channel matches. Fails closed: a missing/empty configured key rejects every
/// request unless the development opt-out is set.
/// </summary>
internal sealed class ModuleManagementApiKeyHandler(
    IOptionsMonitor<ModuleManagementApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ModuleManagementApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = Options.ApiKey;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return Task.FromResult(Options.AllowAnonymous
                ? Success("anonymous")
                : AuthenticateResult.Fail("The Studio management API key is not configured."));
        }

        // Evaluate every channel the key can ride in — a foreign gateway-injected bearer or an empty dedicated
        // header must not shadow a valid key presented on another channel of the same request.
        var dedicatedHeaderPresented = false;

        if (Request.Headers.TryGetValue(ModuleManagementAuth.ApiKeyHeaderName, out var headerKey))
        {
            dedicatedHeaderPresented = true;
            if (CryptographicEquals(headerKey.ToString(), configuredKey))
                return Task.FromResult(Success("module-management"));
        }

        foreach (var authorization in Request.Headers.Authorization)
        {
            const string bearerPrefix = "Bearer ";
            if (authorization is not null && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                && CryptographicEquals(authorization[bearerPrefix.Length..], configuredKey))
                return Task.FromResult(Success("module-management"));
        }

        // See AccessTokenQueryParameterName: query credential honoured only on the configured (hub) paths.
        if (IsQueryTokenPathAllowed()
            && Request.Query.TryGetValue(ModuleManagementAuth.AccessTokenQueryParameterName, out var queryKey)
            && CryptographicEquals(queryKey.ToString(), configuredKey))
            return Task.FromResult(Success("module-management"));

        // The dedicated header is unambiguous intent for this gate, so a mismatch there fails the request.
        // A foreign bearer (or nothing) yields NoResult: 401 under the built-in single scheme, but a host that
        // rebinds the policy to a real identity scheme can still authenticate it.
        return Task.FromResult(dedicatedHeaderPresented
            ? AuthenticateResult.Fail("The supplied management API key is invalid.")
            : AuthenticateResult.NoResult());
    }

    private bool IsQueryTokenPathAllowed() =>
        Options.QueryTokenPathPrefixes.Any(prefix => Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private AuthenticateResult Success(string name)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name)],
            ModuleManagementAuth.SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static bool CryptographicEquals(string provided, string expected)
    {
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
