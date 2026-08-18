namespace Elsa.Workflows.Runtime.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "runtime/workflows";
    internal const string RuntimeDiagnosticsSettings = "runtime/workflows/diagnostics/settings";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Executables => GetRoute("executables");
    internal static string Executable => GetRoute("executables/{artifactId}");
    internal static string ExecutableInputSources => GetRoute("executables/{artifactId}/source-references/{sourceReferenceId}/input-sources");
    internal static string ExecutableProvenance => GetRoute("executables/{artifactId}/provenance");
    /// <summary>
    /// FR-B-006 / T117: the runtime-owned activation ledger reads. The definition id follows the
    /// <c>activation-slots</c> literal rather than preceding it (<c>{definitionId}/activation-slots</c>) so the
    /// template can never compete with the sibling <c>executables</c> / <c>instances</c> / <c>dispatches</c>
    /// literals for the segment right after <see cref="DomainPrefix"/>. Endpoint routing compares candidate
    /// endpoints by <c>Order</c> before route precedence, so a literal is not guaranteed to beat a parameter.
    /// </summary>
    internal static string ActivationSlots => GetRoute("activation-slots/{definitionId}");

    internal static string ActivationSlot => GetRoute("activation-slots/{definitionId}/{slotName}");

    internal static string Instances => GetRoute("instances");
    internal static string InstancesPage => GetRoute("instances/page");
    internal static string Dispatches => GetRoute("dispatches");
    internal static string Dispatch => GetRoute("dispatches/{dispatchId}");
    internal static string DispatchRedrive => GetRoute("dispatches/{dispatchId}/redrive");
}
