namespace Elsa.Workflows.Runtime.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "runtime/workflows";
    internal const string RuntimeDiagnosticsSettings = "runtime/workflows/diagnostics/settings";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Executables => GetRoute("executables");
    internal static string Executable => GetRoute("executables/{artifactId}");
    internal static string ExecutableProvenance => GetRoute("executables/{artifactId}/provenance");
    internal static string Instances => GetRoute("instances");
    internal static string InstancesPage => GetRoute("instances/page");
}
