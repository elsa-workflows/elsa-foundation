using CShells.Features;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Makes the deployable Diagnostics Groundwork declaration available to a host composition.
/// </summary>
/// <remarks>
/// This composition feature does not replace <c>IOpenTelemetryStore</c> or <c>IStructuredLogStore</c>.
/// It supplies the document contributor used at runtime and selects the parameterless combined source
/// used by Groundwork.Tool. The concrete diagnostics features own store activation.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "DiagnosticsGroundworkPersistence",
    DisplayName = "Diagnostics Groundwork Persistence",
    Description = "Contributes the combined Diagnostics document and diagnostic-record schema to a host-selected Groundwork composition and Groundwork.Tool.",
    DependsOn = new object[] { "DiagnosticsOpenTelemetry", "DiagnosticsStructuredLogs" })]
public class DiagnosticsGroundworkPersistenceFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
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
        return services.AddGroundworkStorageComposition<DiagnosticsGroundworkDeploymentSchema>();
    }
}
