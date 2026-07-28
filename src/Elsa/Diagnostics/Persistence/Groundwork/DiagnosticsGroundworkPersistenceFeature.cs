using CShells.Features;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Selects the deployable Groundwork diagnostics persistence family for a host composition.
/// </summary>
/// <remarks>
/// This is the one catalog-discoverable replacement feature for both diagnostics store contracts. It
/// contributes the document schema and delegates concrete adapter registration to the two domain-owned
/// Groundwork features. The host-selected provider supplies the diagnostic-record session factory and
/// selects the matching parameterless deployment source for Groundwork.Tool and runtime admission.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsGroundworkPersistence",
    DisplayName = "Diagnostics Groundwork Persistence",
    Description = "Replaces both diagnostics stores with Groundwork adapters and contributes their combined document and diagnostic-record schema to the host-selected Groundwork composition and Groundwork.Tool.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry", "DiagnosticsStructuredLogs" },
    Metadata = [GroundworkSchemaFeatureMetadata.Key, GroundworkSchemaFeatureMetadata.Diagnostics])]
public class DiagnosticsGroundworkPersistenceFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The concrete features deliberately remain ordinary composition classes rather than separately
        // cataloged shell features: selecting this one feature makes the two diagnostics replacements atomic.
        new GroundworkOpenTelemetryPersistenceFeature().ConfigureServices(services);
        new GroundworkStructuredLogsPersistenceFeature().ConfigureServices(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, DiagnosticsGroundworkStorageManifestSource>());
    }
}

/// <summary>
/// Registers the Diagnostics deployment schema as the one authority for Groundwork.Tool and runtime
/// readiness/admission. Call this once when a host selects the Diagnostics-only Groundwork composition;
/// provider registrations may select <see cref="DiagnosticsGroundworkDeploymentSchema"/> directly instead.
/// </summary>
public static class DiagnosticsGroundworkDeploymentRegistration
{
    public static IServiceCollection AddGroundworkDiagnosticsDeployment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGroundworkStorageComposition<DiagnosticsGroundworkDeploymentSchema>();
        services.TryAddSingleton<global::Groundwork.DiagnosticRecords.IDiagnosticRecordDeploymentManifestSource>(
            provider => provider.GetRequiredService<DiagnosticsGroundworkDeploymentSchema>());
        return services;
    }
}
