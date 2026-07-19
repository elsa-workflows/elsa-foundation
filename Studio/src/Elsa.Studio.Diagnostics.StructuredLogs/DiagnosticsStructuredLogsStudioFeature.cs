using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.StructuredLogs;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Structured Logs")]
[StudioModule("structured-logs", "Structured logs", "1.0.4", "navigation", "routes", "panels", "http", "sse", "diagnostics")]
[ShellFeature(
    name: "StructuredLogsStudio",
    DisplayName = "Structured logs module",
    Description = "Contributes the Studio structured-log diagnostics UI module."
)]
public sealed class DiagnosticsStructuredLogsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
