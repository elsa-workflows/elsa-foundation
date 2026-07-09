using System.Net;
using System.Text.Json;

namespace Elsa.Studio.Web;

/// <summary>
/// The Studio management bridge (ADR 0037): a Studio-owned server-side surface that reports the availability of the
/// backend Elsa host's management surface as an explicit Studio concept. The browser asks Studio for this status
/// instead of probing backend host-control endpoints directly, so the backend host management key never leaves the
/// server and the SPA never issues doomed, 401-noisy requests against the backend.
/// </summary>
internal static class StudioBackendManagementBridge
{
    /// <summary>The Studio-owned bridge route group. Routes and DTOs express Studio concepts, not backend endpoint paths.</summary>
    public const string RouteGroup = "/_elsa/studio/backend-management";

    public static IEndpointRouteBuilder MapStudioBackendManagementBridge(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(RouteGroup)
            .RequireAuthorization(StudioBridgeAuth.PolicyName);

        group.MapGet("/status", GetStatusAsync);
        group.MapGet("/extension-builder/capabilities", GetExtensionBuilderCapabilitiesAsync);

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        StudioBackendManagementClient client,
        CancellationToken cancellationToken)
    {
        var status = await client.GetManagementStatusAsync(cancellationToken);
        return Results.Ok(status);
    }

    private static async Task<IResult> GetExtensionBuilderCapabilitiesAsync(
        StudioBackendManagementClient client,
        CancellationToken cancellationToken)
    {
        var capabilities = await client.GetExtensionBuilderCapabilitiesAsync(cancellationToken);
        return Results.Ok(capabilities);
    }
}

/// <summary>
/// Backend Extension Builder capabilities as seen by Studio (ADR 0037): a Studio concept ("host capabilities") the
/// browser reads from the Studio origin instead of probing the backend host-control endpoint directly. The
/// <see cref="Status"/> field carries the same explicit envelope as <see cref="StudioBackendManagementStatus"/> so the
/// frontend branches on state, not on an HTTP failure. <see cref="Capabilities"/> is populated only when
/// <see cref="Status"/> is <c>available</c>; for every other state the frontend renders an explicit
/// "backend management unavailable" surface and gates actions rather than issuing doomed backend requests.
/// </summary>
internal sealed record StudioExtensionBuilderCapabilitiesResult(
    string Status,
    string Detail,
    StudioExtensionBuilderCapabilities? Capabilities,
    string? BackendBaseUrl,
    DateTimeOffset CheckedAt)
{
    // Same envelope vocabulary as StudioBackendManagementStatus, re-declared here so the capabilities DTO is a
    // self-contained Studio concept rather than reaching into the status DTO's constants.
    public const string Available = StudioBackendManagementStatus.Available;
    public const string Unconfigured = StudioBackendManagementStatus.Unconfigured;
    public const string Unauthorized = StudioBackendManagementStatus.Unauthorized;
    public const string Unreachable = StudioBackendManagementStatus.Unreachable;
    public const string Degraded = StudioBackendManagementStatus.Degraded;
}

/// <summary>
/// The Studio-owned view of the backend Extension Builder capability flags. Mirrors the backend's capability contract
/// but is a Studio DTO: the frontend derives which Extension Builder actions to enable from these flags. Server
/// enforcement on the backend remains authoritative regardless of what the browser is shown.
/// </summary>
internal sealed record StudioExtensionBuilderCapabilities(
    bool CanCreateWorkspace,
    bool CanEditFiles,
    bool CanBuild,
    bool CanPromote,
    bool CanRollback);

/// <summary>
/// The status of the backend host's management surface as seen by Studio.
/// <list type="bullet">
/// <item><c>available</c>: Studio reached the backend management surface and it responded with a sane registry.</item>
/// <item><c>unconfigured</c>: Studio has no backend base URL and/or no backend management key, so it fails closed
/// without issuing any outbound backend call.</item>
/// <item><c>unauthorized</c>: the backend rejected Studio's management key (401), or reported the surface as disabled
/// because the backend itself has no key configured (404). From Studio's vantage point these are indistinguishable
/// remediation-wise — the operator must fix the key on one side — so both collapse to <c>unauthorized</c>.</item>
/// <item><c>unreachable</c>: the backend could not be reached (network error, DNS failure, or timeout).</item>
/// <item><c>degraded</c>: Studio reached the backend but it answered with a server error (5xx) or an otherwise
/// unexpected non-success response — the surface exists but is not healthy.</item>
/// </list>
/// The DTO never echoes the management key or raw backend error bodies (which can leak backend topology); the backend
/// base URL is already browser-known via runtime configuration today, so it is safe to include for operator clarity.
/// </summary>
internal sealed record StudioBackendManagementStatus(
    string Status,
    string Detail,
    string? BackendBaseUrl,
    DateTimeOffset CheckedAt)
{
    public const string Available = "available";
    public const string Unconfigured = "unconfigured";
    public const string Unauthorized = "unauthorized";
    public const string Unreachable = "unreachable";
    public const string Degraded = "degraded";
}

/// <summary>
/// Studio's first server-to-server HTTP client to the backend Elsa host. It attaches the backend host management key
/// (<see cref="ModuleManagementAuth.ApiKeyHeaderName"/>) only on these Studio→backend calls; the browser never sees it.
/// The client fails closed: when no backend base URL or management key is configured it returns <c>unconfigured</c>
/// without issuing any outbound request.
/// </summary>
internal sealed class StudioBackendManagementClient(
    HttpClient httpClient,
    StudioBackendManagementOptions options,
    ILogger<StudioBackendManagementClient> logger)
{
    // The backend read-only host-control endpoints the bridge probes. These paths are Studio→backend implementation
    // details; they are never surfaced to the browser.
    private const string BackendRegistryPath = "/_elsa/module-management/registry";
    private const string BackendExtensionBuilderCapabilitiesPath = "/_elsa/extension-builder/capabilities";

    private static readonly JsonSerializerOptions BackendJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StudioBackendManagementStatus> GetManagementStatusAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        // Fail closed: without a backend base URL or a management key we make ZERO outbound calls (ADR 0037: possession
        // of no credential must never reach the bridge's backend call path).
        if (string.IsNullOrWhiteSpace(options.BackendBaseUrl) || string.IsNullOrWhiteSpace(options.ManagementApiKey))
        {
            return new(
                StudioBackendManagementStatus.Unconfigured,
                "Backend management is not configured on the Studio host. Set Studio:BackendBaseUrl and Studio:BackendModuleManagementApiKey to enable it.",
                NormalizeBaseUrl(options.BackendBaseUrl),
                checkedAt);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BackendRegistryPath);
            request.Headers.TryAddWithoutValidation(ModuleManagementAuth.ApiKeyHeaderName, options.ManagementApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return await MapResponseAsync(response, checkedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // A caller-cancelled request bubbles up; only our own timeout / transport failures map to `unreachable`.
            if (cancellationToken.IsCancellationRequested)
                throw;

            logger.LogWarning(ex, "Studio could not reach the backend management surface at {BackendBaseUrl}.", options.BackendBaseUrl);
            return new(
                StudioBackendManagementStatus.Unreachable,
                "The backend management surface could not be reached. Check that the backend host is running and Studio:BackendBaseUrl is correct.",
                NormalizeBaseUrl(options.BackendBaseUrl),
                checkedAt);
        }
    }

    public async Task<StudioExtensionBuilderCapabilitiesResult> GetExtensionBuilderCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var backendBaseUrl = NormalizeBaseUrl(options.BackendBaseUrl);

        // Fail closed: same zero-outbound-call guarantee as the status read (ADR 0037).
        if (string.IsNullOrWhiteSpace(options.BackendBaseUrl) || string.IsNullOrWhiteSpace(options.ManagementApiKey))
        {
            return new(
                StudioExtensionBuilderCapabilitiesResult.Unconfigured,
                "Backend management is not configured on the Studio host. Set Studio:BackendBaseUrl and Studio:BackendModuleManagementApiKey to enable Extension Builder.",
                Capabilities: null,
                backendBaseUrl,
                checkedAt);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BackendExtensionBuilderCapabilitiesPath);
            request.Headers.TryAddWithoutValidation(ModuleManagementAuth.ApiKeyHeaderName, options.ManagementApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return await MapCapabilitiesResponseAsync(response, backendBaseUrl, checkedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            logger.LogWarning(ex, "Studio could not reach the backend Extension Builder capabilities surface at {BackendBaseUrl}.", options.BackendBaseUrl);
            return new(
                StudioExtensionBuilderCapabilitiesResult.Unreachable,
                "The backend Extension Builder capabilities surface could not be reached. Check that the backend host is running and Studio:BackendBaseUrl is correct.",
                Capabilities: null,
                backendBaseUrl,
                checkedAt);
        }
    }

    private async Task<StudioExtensionBuilderCapabilitiesResult> MapCapabilitiesResponseAsync(
        HttpResponseMessage response,
        string? backendBaseUrl,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        // 401/403/404 collapse to unauthorized, identical to the status read's remediation ("fix the management key").
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new(
                StudioExtensionBuilderCapabilitiesResult.Unauthorized,
                "The backend rejected the Studio management key (or the Extension Builder surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.",
                Capabilities: null,
                backendBaseUrl,
                checkedAt);
        }

        if (response.IsSuccessStatusCode)
        {
            var capabilities = await TryReadCapabilitiesAsync(response, cancellationToken);
            if (capabilities is not null)
            {
                return new(
                    StudioExtensionBuilderCapabilitiesResult.Available,
                    "The backend Extension Builder capabilities are reachable.",
                    capabilities,
                    backendBaseUrl,
                    checkedAt);
            }

            // A 200 that isn't a recognizable capabilities payload (e.g. an SPA fallback page) is degraded, not available.
            return new(
                StudioExtensionBuilderCapabilitiesResult.Degraded,
                "The backend responded but did not return recognizable Extension Builder capabilities.",
                Capabilities: null,
                backendBaseUrl,
                checkedAt);
        }

        logger.LogWarning("Backend Extension Builder capabilities surface at {BackendBaseUrl} responded with {StatusCode}.", options.BackendBaseUrl, (int)response.StatusCode);
        return new(
            StudioExtensionBuilderCapabilitiesResult.Degraded,
            $"The backend Extension Builder capabilities surface responded with an unexpected status ({(int)response.StatusCode}).",
            Capabilities: null,
            backendBaseUrl,
            checkedAt);
    }

    private static async Task<StudioExtensionBuilderCapabilities?> TryReadCapabilitiesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<StudioExtensionBuilderCapabilities>(stream, BackendJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<StudioBackendManagementStatus> MapResponseAsync(
        HttpResponseMessage response,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var backendBaseUrl = NormalizeBaseUrl(options.BackendBaseUrl);

        // 401: our management key was rejected. 404: the backend has no key configured, so it hides the surface — from
        // Studio's side that is the same remediation ("fix the management key wiring"), so collapse both to `unauthorized`.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return new(
                StudioBackendManagementStatus.Unauthorized,
                "The backend rejected the Studio management key (or the backend management surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.",
                backendBaseUrl,
                checkedAt);
        }

        if (response.IsSuccessStatusCode)
        {
            // Guard against a 200 that isn't actually the registry (e.g. an SPA fallback page): a sane payload must be
            // JSON. A non-JSON 200 means we hit something other than the management surface, so treat it as degraded.
            if (await IsJsonPayloadAsync(response, cancellationToken))
            {
                return new(
                    StudioBackendManagementStatus.Available,
                    "The backend management surface is reachable.",
                    backendBaseUrl,
                    checkedAt);
            }

            return new(
                StudioBackendManagementStatus.Degraded,
                "The backend responded but did not return a recognizable management registry.",
                backendBaseUrl,
                checkedAt);
        }

        // 5xx (and any other unexpected non-success): the surface exists but is unhealthy.
        logger.LogWarning("Backend management surface at {BackendBaseUrl} responded with {StatusCode}.", options.BackendBaseUrl, (int)response.StatusCode);
        return new(
            StudioBackendManagementStatus.Degraded,
            $"The backend management surface responded with an unexpected status ({(int)response.StatusCode}).",
            backendBaseUrl,
            checkedAt);
    }

    private static async Task<bool> IsJsonPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NormalizeBaseUrl(string? baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
}

/// <summary>
/// Server-side configuration for the Studio→backend management client, bound from <c>Studio:BackendBaseUrl</c> and
/// <c>Studio:BackendModuleManagementApiKey</c>. Held server-side only; never emitted to the browser by the bridge.
/// </summary>
internal sealed record StudioBackendManagementOptions(string? BackendBaseUrl, string? ManagementApiKey)
{
    public const string BackendBaseUrlConfigurationKey = "Studio:BackendBaseUrl";
    public const string ManagementApiKeyConfigurationKey = "Studio:BackendModuleManagementApiKey";

    public static StudioBackendManagementOptions FromConfiguration(IConfiguration configuration) =>
        new(configuration[BackendBaseUrlConfigurationKey], configuration[ManagementApiKeyConfigurationKey]);
}

internal static class StudioBackendManagementBridgeServiceCollectionExtensions
{
    // Short, so the browser-facing bridge status endpoint stays snappy even when the backend is slow to answer.
    private static readonly TimeSpan BackendRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers the typed <see cref="StudioBackendManagementClient"/> over <see cref="IHttpClientFactory"/>. When no
    /// backend base URL is configured, no <c>BaseAddress</c> is set — the client still resolves and fails closed to
    /// <c>unconfigured</c> without issuing any request.
    /// </summary>
    public static IServiceCollection AddStudioBackendManagementBridge(this IServiceCollection services, IConfiguration configuration)
    {
        var options = StudioBackendManagementOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        services.AddHttpClient<StudioBackendManagementClient>(client =>
        {
            client.Timeout = BackendRequestTimeout;
            if (!string.IsNullOrWhiteSpace(options.BackendBaseUrl))
                client.BaseAddress = new Uri(options.BackendBaseUrl, UriKind.Absolute);
        });

        return services;
    }
}
