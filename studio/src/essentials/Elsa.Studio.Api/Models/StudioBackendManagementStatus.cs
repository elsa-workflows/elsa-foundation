namespace Elsa.Studio.Api.Models;

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
public sealed record StudioBackendManagementStatus(
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
