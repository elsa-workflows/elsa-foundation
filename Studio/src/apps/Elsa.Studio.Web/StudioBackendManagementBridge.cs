using System.Text.Json;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Web;

/// <summary>
/// The Studio management bridge (ADR 0037): a Studio-owned server-side surface that reports the availability of the
/// backend Elsa host's management surface as an explicit Studio concept. The browser asks Studio for this status
/// instead of probing backend host-control endpoints directly, so the backend host management key never leaves the
/// server and the SPA never issues doomed, 401-noisy requests against the backend.
/// </summary>
internal static class StudioBackendManagementBridge
{
    public static IEndpointRouteBuilder MapStudioBackendManagementBridge(this IEndpointRouteBuilder endpoints)
    {
        // Host-control permission gating (#249, ADR 0037). Status + registry read the backend module/feature registry,
        // so they require `module-management.read`. When Studio auth is disabled every policy allows anonymously (demo
        // shell). A signed-in user lacking the permission is forbidden (403) — distinct from an unauthenticated 401 and
        // from the backend-status states this bridge also reports. A Studio module that relays its own backend surface
        // nests its routes under the same route group from its shell feature.
        var group = endpoints.MapGroup(StudioBackendManagementRoutes.RouteGroup);

        group.MapGet("/status", GetStatusAsync)
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementReadPolicyName);
        group.MapGet("/registry", GetRegistryAsync)
            .RequireAuthorization(StudioBridgeAuth.ModuleManagementReadPolicyName);

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
/// Studio's first server-to-server HTTP client to the backend Elsa host. It attaches the backend host management key
/// (<see cref="StudioBackendManagementOptions.ManagementApiKeyHeaderName"/>) only on these Studio→backend calls; the browser never sees it.
/// The client fails closed: when no backend base URL or management key is configured it returns <c>unconfigured</c>
/// without issuing any outbound request.
/// </summary>
internal sealed class StudioBackendManagementClient(
    HttpClient httpClient,
    StudioBackendManagementOptions options,
    ILogger<StudioBackendManagementClient> logger) : StudioBackendReadClient(httpClient, options, logger)
{
    // The backend read-only host-control endpoint the bridge probes. The path is a Studio→backend implementation
    // detail; it is never surfaced to the browser.
    private const string BackendRegistryPath = "/_elsa/module-management/registry";

    // The backend surface the bridge reads: its path plus the surface-specific detail strings. Status and registry share
    // the ONE probe/mapping pipeline (fail-closed gate, key attachment, status mapping) in StudioBackendReadClient.
    private static readonly StudioBackendReadSurface ManagementRegistrySurface = new(
        Path: BackendRegistryPath,
        Description: "privileged host-management surface",
        UnconfiguredDetail: "Privileged host management is not configured on the Studio host. Set Studio:BackendServerBaseUrl (or Studio:BackendBaseUrl for a shared URL) and Studio:BackendModuleManagementApiKey to enable Server module management.",
        AvailableDetail: "The privileged host-management surface is reachable.",
        UnauthorizedDetail: "The backend rejected the Studio management key (or its privileged host-management surface is disabled). Verify Studio:BackendModuleManagementApiKey matches the backend host management key.",
        UnreachableDetail: "The privileged host-management surface could not be reached. Check that the backend host is running and Studio:BackendServerBaseUrl (or Studio:BackendBaseUrl) is correct.",
        UnrecognizedPayloadDetail: "The backend responded but did not return a recognizable management registry.");

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
}

internal static class StudioBackendManagementBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared <see cref="StudioBackendManagementOptions"/> and the typed
    /// <see cref="StudioBackendManagementClient"/> over <see cref="IHttpClientFactory"/>. When no backend base URL is
    /// configured, no <c>BaseAddress</c> is set — the client still resolves and fails closed to <c>unconfigured</c>
    /// without issuing any request. Studio modules that relay their own backend surface resolve the same options
    /// instance for their clients.
    /// </summary>
    public static IServiceCollection AddStudioBackendManagementBridge(this IServiceCollection services, IConfiguration configuration)
    {
        var options = StudioBackendManagementOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<StudioBackendManagementClient>(client =>
            options.ConfigureBackendClient(client, StudioBackendManagementOptions.ReadRequestTimeout));

        return services;
    }
}
