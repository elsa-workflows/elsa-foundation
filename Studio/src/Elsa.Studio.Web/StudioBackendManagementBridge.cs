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
        // Host-control permission gating (#249, ADR 0037). Status + registry read the backend module/feature registry,
        // so they require `module-management.read`; the Extension Builder capabilities read requires
        // `extension-builder.read`. When Studio auth is disabled every policy allows anonymously (demo shell). A
        // signed-in user lacking the permission is forbidden (403) — distinct from an unauthenticated 401 and from the
        // backend-status states this bridge also reports.
        var group = endpoints.MapGroup(RouteGroup);

        group.MapGet("/status", GetStatusAsync)
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementReadPolicyName);
        group.MapGet("/registry", GetRegistryAsync)
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementReadPolicyName);
        group.MapGet("/extension-builder/capabilities", GetExtensionBuilderCapabilitiesAsync)
            .RequireAuthorization(StudioBridgeAuth.ExtensionBuilderReadPolicyName);

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        StudioBackendManagementClient client,
        CancellationToken cancellationToken)
    {
        var status = await client.GetManagementStatusAsync(cancellationToken);
        return Results.Ok(status);
    }

    // The Studio-owned backend host-registry read (#246, ADR 0037). The browser calls this with only its normal
    // credentials; Studio attaches the server-side management key on the Studio->backend call. The result is a
    // status-bearing envelope so the SPA branches on an explicit backend-management state (available / unconfigured /
    // unreachable / unauthorized / degraded) instead of inferring it from an opaque HTTP failure.
    private static async Task<IResult> GetRegistryAsync(
        StudioBackendManagementClient client,
        CancellationToken cancellationToken)
    {
        var envelope = await client.GetRegistryAsync(cancellationToken);
        return Results.Ok(envelope);
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
/// The backend host registry as surfaced by the Studio management bridge (#246, ADR 0037). This is the Studio-owned
/// browser-facing shape: it wraps the raw backend registry payload (<see cref="Registry"/>, present only when
/// <see cref="Status"/> is <c>available</c>) in the same explicit backend-management <see cref="Status"/> the bridge
/// reports for the status endpoint, so the frontend renders the real unconfigured / unreachable / unauthorized /
/// degraded state instead of inferring it from a failed fetch. On any non-available status the bridge issues zero (when
/// unconfigured) or fail-closed outbound calls and returns a <c>null</c> registry. The management key is never echoed.
/// </summary>
internal sealed record StudioBackendManagementRegistryEnvelope(
    string Status,
    string Detail,
    string? BackendBaseUrl,
    DateTimeOffset CheckedAt,
    JsonElement? Registry)
{
    public static StudioBackendManagementRegistryEnvelope FromStatus(StudioBackendManagementStatus status, JsonElement? registry = null) =>
        new(status.Status, status.Detail, status.BackendBaseUrl, status.CheckedAt, registry);
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
/// (<see cref="StudioBackendManagementOptions.ManagementApiKeyHeaderName"/>) only on these Studio→backend calls; the browser never sees it.
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

    // The two backend surfaces the bridge reads. Each carries its path plus the surface-specific detail strings, so
    // every read shares ONE probe/mapping pipeline (fail-closed gate, key attachment, status mapping) while keeping the
    // operator-facing wording each surface shipped with.
    private static readonly BackendReadSurface ManagementRegistrySurface = new(
        Path: BackendRegistryPath,
        Description: "backend management surface",
        UnconfiguredDetail: "Backend management is not configured on the Studio host. Set Studio:BackendBaseUrl and Studio:BackendModuleManagementApiKey to enable it.",
        AvailableDetail: "The backend management surface is reachable.",
        UnauthorizedDetail: "The backend rejected the Studio management key (or the backend management surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.",
        UnreachableDetail: "The backend management surface could not be reached. Check that the backend host is running and Studio:BackendBaseUrl is correct.",
        UnrecognizedPayloadDetail: "The backend responded but did not return a recognizable management registry.");

    private static readonly BackendReadSurface ExtensionBuilderCapabilitiesSurface = new(
        Path: BackendExtensionBuilderCapabilitiesPath,
        Description: "backend Extension Builder capabilities surface",
        UnconfiguredDetail: "Backend management is not configured on the Studio host. Set Studio:BackendBaseUrl and Studio:BackendModuleManagementApiKey to enable Extension Builder.",
        AvailableDetail: "The backend Extension Builder capabilities are reachable.",
        UnauthorizedDetail: "The backend rejected the Studio management key (or the Extension Builder surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.",
        UnreachableDetail: "The backend Extension Builder capabilities surface could not be reached. Check that the backend host is running and Studio:BackendBaseUrl is correct.",
        UnrecognizedPayloadDetail: "The backend responded but did not return recognizable Extension Builder capabilities.");

    public async Task<StudioBackendManagementStatus> GetManagementStatusAsync(CancellationToken cancellationToken)
    {
        // The status probe only needs the status half of the mapped response; it discards the parsed registry payload.
        var (status, _) = await ProbeBackendAsync(ManagementRegistrySurface, cancellationToken);
        return status;
    }

    // The Studio-owned registry read (#246): the same fail-closed / send / map pipeline as the status probe, but it
    // returns the parsed backend registry payload alongside the status so the browser gets the host registry without
    // ever calling backend host-control endpoints or seeing the management key.
    public async Task<StudioBackendManagementRegistryEnvelope> GetRegistryAsync(CancellationToken cancellationToken)
    {
        var (status, registry) = await ProbeBackendAsync(ManagementRegistrySurface, cancellationToken);
        return StudioBackendManagementRegistryEnvelope.FromStatus(status, registry);
    }

    // The Studio-owned Extension Builder capabilities read (#247): the shared probe against the capabilities surface,
    // with the JSON payload projected into the Studio capability flags.
    public async Task<StudioExtensionBuilderCapabilitiesResult> GetExtensionBuilderCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var (status, payload) = await ProbeBackendAsync(ExtensionBuilderCapabilitiesSurface, cancellationToken);
        var capabilities = TryDeserializeCapabilities(payload);

        // A JSON-object 200 whose members don't bind to the capability flags is degraded, not available — the same
        // "unrecognizable payload" mapping the probe applies to non-object bodies.
        if (status.Status == StudioBackendManagementStatus.Available && capabilities is null)
        {
            return new(
                StudioExtensionBuilderCapabilitiesResult.Degraded,
                ExtensionBuilderCapabilitiesSurface.UnrecognizedPayloadDetail,
                Capabilities: null,
                status.BackendBaseUrl,
                status.CheckedAt);
        }

        return new(status.Status, status.Detail, capabilities, status.BackendBaseUrl, status.CheckedAt);
    }

    private static StudioExtensionBuilderCapabilities? TryDeserializeCapabilities(JsonElement? payload)
    {
        if (payload is null)
            return null;

        try
        {
            return payload.Value.Deserialize<StudioExtensionBuilderCapabilities>(BackendJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Shared probe for every bridge read: fails closed with zero outbound calls when unconfigured, sends the single
    // management-keyed Studio->backend request for the given surface, and maps the response to a (status, payload)
    // pair. Status, registry, and capabilities all project from this so the fail-closed guarantee and the
    // unauthorized/unreachable/degraded mapping live in exactly one place.
    private async Task<(StudioBackendManagementStatus Status, JsonElement? Payload)> ProbeBackendAsync(
        BackendReadSurface surface,
        CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var backendBaseUrl = NormalizeBaseUrl(options.BackendBaseUrl);

        // Fail closed: without a backend base URL or a management key we make ZERO outbound calls (ADR 0037: possession
        // of no credential must never reach the bridge's backend call path).
        if (string.IsNullOrWhiteSpace(options.BackendBaseUrl) || string.IsNullOrWhiteSpace(options.ManagementApiKey))
        {
            return (new(
                StudioBackendManagementStatus.Unconfigured,
                surface.UnconfiguredDetail,
                backendBaseUrl,
                checkedAt), null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, surface.Path);
            request.Headers.TryAddWithoutValidation(StudioBackendManagementOptions.ManagementApiKeyHeaderName, options.ManagementApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return await MapResponseAsync(surface, response, backendBaseUrl, checkedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // A caller-cancelled request bubbles up; only our own timeout / transport failures map to `unreachable`.
            if (cancellationToken.IsCancellationRequested)
                throw;

            logger.LogWarning(ex, "Studio could not reach the {BackendSurface} at {BackendBaseUrl}.", surface.Description, options.BackendBaseUrl);
            return (new(
                StudioBackendManagementStatus.Unreachable,
                surface.UnreachableDetail,
                backendBaseUrl,
                checkedAt), null);
        }
    }

    private async Task<(StudioBackendManagementStatus Status, JsonElement? Payload)> MapResponseAsync(
        BackendReadSurface surface,
        HttpResponseMessage response,
        string? backendBaseUrl,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        // 401: our management key was rejected. 404: the backend has no key configured, so it hides the surface — from
        // Studio's side that is the same remediation ("fix the management key wiring"), so collapse both to `unauthorized`.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return (new(
                StudioBackendManagementStatus.Unauthorized,
                surface.UnauthorizedDetail,
                backendBaseUrl,
                checkedAt), null);
        }

        if (response.IsSuccessStatusCode)
        {
            // Guard against a 200 that isn't actually the surface's payload (e.g. an SPA fallback page): a sane payload
            // must be a JSON object. Anything else means we hit something other than the surface, so treat it as degraded.
            var payload = await ReadJsonObjectAsync(response, cancellationToken);
            if (payload is not null)
            {
                return (new(
                    StudioBackendManagementStatus.Available,
                    surface.AvailableDetail,
                    backendBaseUrl,
                    checkedAt), payload);
            }

            return (new(
                StudioBackendManagementStatus.Degraded,
                surface.UnrecognizedPayloadDetail,
                backendBaseUrl,
                checkedAt), null);
        }

        // 5xx (and any other unexpected non-success): the surface exists but is unhealthy.
        logger.LogWarning("The {BackendSurface} at {BackendBaseUrl} responded with {StatusCode}.", surface.Description, options.BackendBaseUrl, (int)response.StatusCode);
        return (new(
            StudioBackendManagementStatus.Degraded,
            $"The {surface.Description} responded with an unexpected status ({(int)response.StatusCode}).",
            backendBaseUrl,
            checkedAt), null);
    }

    // Reads the response body when it is a JSON object, returning a detached clone (the response stream is disposed by
    // the caller, so the JsonElement must own its buffer). Returns null when the body is missing, not JSON, or not a
    // JSON object — those cases map to `degraded`, never `available`.
    private static async Task<JsonElement?> ReadJsonObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            // Clone so the element survives the `using document` disposal below.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A backend read surface the bridge probes: its Studio→backend path plus the surface-specific operator-facing
    /// detail strings. <see cref="Description"/> feeds logs and the degraded unexpected-status detail.
    /// </summary>
    private sealed record BackendReadSurface(
        string Path,
        string Description,
        string UnconfiguredDetail,
        string AvailableDetail,
        string UnauthorizedDetail,
        string UnreachableDetail,
        string UnrecognizedPayloadDetail);

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

    /// <summary>
    /// The request header the backend Elsa host expects the management key on. This is the backend host-control
    /// contract; Studio attaches it server-side on Studio→backend calls only. The browser never carries it (ADR 0037).
    /// </summary>
    public const string ManagementApiKeyHeaderName = "X-Elsa-Module-Management-Key";

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
