using System.Text.Json;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Services;
using Microsoft.Extensions.Logging;

namespace Elsa.Studio.ExtensionBuilder;

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
/// The Studio-owned Extension Builder capabilities read (#247): the shared fail-closed probe
/// (<see cref="StudioBackendReadClient"/>) against the backend capabilities surface, with the JSON payload projected
/// into the Studio capability flags. The management key rides only on the Studio→backend call.
/// </summary>
internal sealed class StudioExtensionBuilderCapabilitiesClient(
    HttpClient httpClient,
    StudioBackendManagementOptions options,
    ILogger<StudioExtensionBuilderCapabilitiesClient> logger) : StudioBackendReadClient(httpClient, options, logger)
{
    // The backend read-only host-control endpoint the bridge probes. A Studio→backend implementation detail; never
    // surfaced to the browser.
    private const string BackendCapabilitiesPath = StudioExtensionBuilderBridge.BackendRoot + "/capabilities";

    private static readonly JsonSerializerOptions BackendJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly StudioBackendReadSurface CapabilitiesSurface = new(
        Path: BackendCapabilitiesPath,
        Description: "backend Extension Builder capabilities surface",
        UnconfiguredDetail: StudioExtensionBuilderBridge.UnconfiguredDetail,
        AvailableDetail: "The backend Extension Builder capabilities are reachable.",
        UnauthorizedDetail: StudioExtensionBuilderBridge.UnauthorizedDetail,
        UnreachableDetail: "The backend Extension Builder capabilities surface could not be reached. Check that the backend host is running and Studio:BackendServerBaseUrl (or Studio:BackendBaseUrl) is correct.",
        UnrecognizedPayloadDetail: "The backend responded but did not return recognizable Extension Builder capabilities.");

    public async Task<StudioExtensionBuilderCapabilitiesResult> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var (status, payload) = await ProbeBackendAsync(CapabilitiesSurface, cancellationToken);
        var capabilities = TryDeserializeCapabilities(payload);

        // A JSON-object 200 whose members don't bind to the capability flags is degraded, not available — the same
        // "unrecognizable payload" mapping the probe applies to non-object bodies.
        if (status.Status == StudioBackendManagementStatus.Available && capabilities is null)
        {
            return new(
                StudioExtensionBuilderCapabilitiesResult.Degraded,
                CapabilitiesSurface.UnrecognizedPayloadDetail,
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
}
