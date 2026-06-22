using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.OpenTelemetry;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("OpenTelemetry")]
[ShellFeature(
    name: "OpenTelemetryStudio",
    DisplayName = "OpenTelemetry module",
    Description = "Contributes the Studio OpenTelemetry diagnostics UI module."
)]
public sealed class DiagnosticsOpenTelemetryStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDiagnosticsOpenTelemetryStudio();
    }
}
