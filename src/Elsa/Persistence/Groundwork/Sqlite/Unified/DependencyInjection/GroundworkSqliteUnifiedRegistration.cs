using CShells.Features;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;

/// <summary>
/// Selects the provider-level Elsa Groundwork families (runtime, secrets, distributed runtime,
/// workflows design, activities design and publishing) and exposes one SQLite physical store for
/// their validated host-selected composition. Runtime startup admits the exact applied target; schema
/// application remains an operator/CLI responsibility. Identity is selected explicitly by its own feature.
/// </summary>
public static class GroundworkSqliteUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string the single document store opens.</param>
    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkSqliteUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(connectionString, autoApplyOnStartup);

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false)
    {
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkSqliteUnifiedPersistenceCore(connectionString, autoApplyOnStartup);
    }

    /// <summary>
    /// Registers the unified SQLite substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        services.AddGroundworkStorageComposition<TDeploymentSource>();
        return services.AddGroundworkSqliteUnifiedPersistenceCore(connectionString, autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkSqliteUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSqliteGroundworkDocumentStore(connectionString, autoApplyOnStartup);
        return services.AddGroundworkUnifiedStoreFamilies();
    }
}
