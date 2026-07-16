using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.OpenTelemetry;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("OpenTelemetry")]
[StudioModule("open-telemetry", "OpenTelemetry", "1.0.1", "navigation", "routes", "http", "diagnostics", "otel", "traces", "metrics", "logs")]
[ShellFeature(
    name: "OpenTelemetryStudio",
    DisplayName = "OpenTelemetry module",
    Description = "Contributes the Studio OpenTelemetry diagnostics UI module."
)]
public sealed class DiagnosticsOpenTelemetryStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
