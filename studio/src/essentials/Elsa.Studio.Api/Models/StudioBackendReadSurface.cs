namespace Elsa.Studio.Api.Models;

/// <summary>
/// A backend read surface a <see cref="Services.StudioBackendReadClient"/> probes: its Studio→backend path plus the
/// surface-specific operator-facing detail strings. <see cref="Description"/> feeds logs and the degraded
/// unexpected-status detail.
/// </summary>
public sealed record StudioBackendReadSurface(
    string Path,
    string Description,
    string UnconfiguredDetail,
    string AvailableDetail,
    string UnauthorizedDetail,
    string UnreachableDetail,
    string UnrecognizedPayloadDetail);
