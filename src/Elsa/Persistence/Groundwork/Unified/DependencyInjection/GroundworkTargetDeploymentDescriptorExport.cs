using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.SchemaEvolution;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Unified.DependencyInjection;

/// <summary>
/// Exports the per-target deployment plan a composed host holds, for <c>Groundwork.Tool</c> to apply.
/// <para>
/// The export is taken from the built container rather than from configuration, because that is the only
/// place both halves are true at once: the deployment source names the lanes, and the manifest bindings say
/// where each one goes, and neither is settled until feature composition has run.
/// </para>
/// </summary>
public static class GroundworkTargetDeploymentDescriptorExport
{
    /// <summary>Builds the descriptor for the host <paramref name="services"/> composed.</summary>
    /// <exception cref="InvalidOperationException">
    /// The host did not select a deployment schema source, so there is no host-wide plan to narrow.
    /// </exception>
    public static GroundworkTargetDeploymentDescriptor CreateGroundworkDeploymentDescriptor(
        this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var source = services.GetService<IPhysicalSchemaManifestSource>() as GroundworkDeploymentSchemaManifestSource
                     ?? throw new InvalidOperationException(
                         "This host has not selected a Groundwork deployment schema source, so there is no " +
                         $"per-target plan to export. Call {nameof(GroundworkStorageCompositionRegistration.AddGroundworkStorageComposition)} " +
                         "with the host's deployment source first.");

        return GroundworkTargetDeploymentDescriptorFactory.Create(
            source,
            services.GetService<GroundworkManifestBindings>());
    }

    /// <summary>Writes the descriptor for the host <paramref name="services"/> composed to <paramref name="path"/>.</summary>
    public static void WriteGroundworkDeploymentDescriptor(this IServiceProvider services, string path) =>
        services.CreateGroundworkDeploymentDescriptor().WriteTo(path);
}
