using CShells.Features;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.DiagnosticRecords;
using Groundwork.MongoDb.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;

/// <summary>Materializes the host-selected Elsa store families through one MongoDB provider target.</summary>
public static class GroundworkMongoDbUnifiedRegistration
{
    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkMongoDbUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            databaseName,
            autoApplyOnStartup);

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false)
    {
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkMongoDbUnifiedPersistenceCore(connectionString, databaseName, autoApplyOnStartup);
    }

    /// <summary>
    /// Registers the unified MongoDB substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkMongoDbUnifiedPersistenceCore(connectionString, databaseName, autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkMongoDbUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        services.AddMongoDbGroundworkDocumentStore(connectionString, databaseName, autoApplyOnStartup);
        services.TryAddSingleton<IDiagnosticRecordDeploymentApplier>(
            _ => MongoDbDiagnosticRecordStoreFactory.CreateDeploymentApplier(connectionString, databaseName));
        services.TryAddSingleton<IDiagnosticRecordStoreSessionFactory>(
            _ => MongoDbDiagnosticRecordStoreFactory.CreateSessionFactory(connectionString, databaseName));
        services.AddGroundworkDiagnosticRecordDeploymentInitializer(autoApplyOnStartup);
        return services.AddGroundworkUnifiedStoreFamilies();
    }
}
