using System.Net;
using System.Text.Json;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Microsoft.Extensions.Logging;

namespace Elsa.Studio.Api.Services;

/// <summary>
/// Base for Studio's server-to-server read clients against the backend Elsa host (ADR 0037). It owns the single
/// fail-closed probe pipeline every bridge read shares: the unconfigured gate, the management-key attachment
/// (<see cref="StudioBackendManagementOptions.ManagementApiKeyHeaderName"/>, on Studio→backend calls only — the browser
/// never sees it), and the mapping of the backend answer onto an explicit <see cref="StudioBackendManagementStatus"/>.
/// A derived client declares its <see cref="StudioBackendReadSurface"/> and projects the returned payload; it never
/// re-implements the gate, so the fail-closed guarantee and the unauthorized/unreachable/degraded mapping live in
/// exactly one place.
/// </summary>
public abstract class StudioBackendReadClient(
    HttpClient httpClient,
    StudioBackendManagementOptions options,
    ILogger logger)
{
    /// <summary>
    /// Shared probe for every bridge read: fails closed with zero outbound calls when unconfigured, sends the single
    /// management-keyed Studio->backend request for the given surface, and maps the response to a (status, payload)
    /// pair. The payload is non-null only when the status is <see cref="StudioBackendManagementStatus.Available"/>.
    /// </summary>
    protected async Task<(StudioBackendManagementStatus Status, JsonElement? Payload)> ProbeBackendAsync(
        StudioBackendReadSurface surface,
        CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var backendBaseUrl = options.NormalizedBackendBaseUrl;

        // Fail closed: without a backend base URL or a management key we make ZERO outbound calls (ADR 0037: possession
        // of no credential must never reach the bridge's backend call path).
        if (!options.IsConfigured)
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
        StudioBackendReadSurface surface,
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
}
