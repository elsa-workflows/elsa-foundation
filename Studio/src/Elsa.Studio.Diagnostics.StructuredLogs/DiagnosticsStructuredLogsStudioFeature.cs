using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.StructuredLogs;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Structured Logs")]
[ShellFeature(
    name: "StructuredLogsStudio",
    DisplayName = "Structured logs module",
    Description = "Contributes the Studio structured-log diagnostics UI module."
)]
public sealed class DiagnosticsStructuredLogsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDiagnosticsStructuredLogsStudio();
    }
}
