using CShells.Features;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// Selects the reference deployment schema from the schema-affecting features enabled for one shell.
/// </summary>
public static class GroundworkReferenceDeploymentSchemaSelector
{
    /// <summary>
    /// Registers the exact reference deployment schema required by the enabled shell features.
    /// </summary>
    public static IServiceCollection AddGroundworkReferenceDeploymentSchema(
        this IServiceCollection services,
        ShellFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        return services.AddGroundworkReferenceDeploymentSchema<GroundworkAllFeaturesDeploymentSchema>();
    }

    /// <summary>
    /// Registers an explicit reference deployment schema.
    /// </summary>
    public static IServiceCollection AddGroundworkReferenceDeploymentSchema<TDeploymentSource>(
        this IServiceCollection services)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGroundworkStorageComposition<TDeploymentSource>();
        return services;
    }

    /// <summary>
    /// Returns the public deployment schema source type required by the enabled shell features.
    /// </summary>
    public static Type Select(ShellFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return typeof(GroundworkAllFeaturesDeploymentSchema);
    }
}
