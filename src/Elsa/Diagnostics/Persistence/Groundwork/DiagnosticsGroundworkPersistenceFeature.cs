using CShells.Features;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Selects the deployable Groundwork diagnostics persistence family for a host composition.
/// </summary>
/// <remarks>
/// This is the one catalog-discoverable replacement feature for both diagnostics store contracts. It
/// delegates concrete adapter registration to the two domain-owned Groundwork v2 features. The
/// host-selected provider supplies the shared v2 provider connection; each adapter admits its own units.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsGroundworkPersistence",
    DisplayName = "Diagnostics Groundwork Persistence",
    Description = "Replaces both diagnostics stores with clean-break Groundwork v2 adapters.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry", "DiagnosticsStructuredLogs" })]
public class DiagnosticsGroundworkPersistenceFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The concrete features deliberately remain ordinary composition classes rather than separately
        // cataloged shell features: selecting this one feature makes the two diagnostics replacements atomic.
        new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);
        new GroundworkStructuredLogsPersistenceFeature().ConfigureServices(services);
    }
}
