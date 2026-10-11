namespace Elsa.Studio.Api.Models;

/// <summary>Routes of the Studio management bridge (ADR 0037).</summary>
public static class StudioBackendManagementRoutes
{
    /// <summary>
    /// The Studio-owned bridge route group. Routes and DTOs under it express Studio concepts, not backend endpoint
    /// paths. The host maps the bridge's own surfaces here, and a feature that relays its own backend surface nests
    /// its routes under the same group.
    /// </summary>
    public const string RouteGroup = "/_elsa/studio/backend-management";
}
