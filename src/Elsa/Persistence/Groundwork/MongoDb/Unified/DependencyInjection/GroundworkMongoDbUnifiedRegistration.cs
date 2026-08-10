using CShells.Features;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.DiagnosticRecords;
using Groundwork.MongoDb.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

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
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers unified MongoDB persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        return services.AddGroundworkMongoDbUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            databaseName,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkMongoDbUnifiedPersistence(
            connectionString,
            databaseName,
            context,
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers the current shell schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        ShellFeatureContext context,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkMongoDbUnifiedPersistenceCore(
            connectionString,
            databaseName,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
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
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new() =>
        services.AddGroundworkMongoDbUnifiedPersistence<TDeploymentSource>(
            connectionString,
            databaseName,
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers an explicit deployment schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkMongoDbUnifiedPersistenceCore(
            connectionString,
            databaseName,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkMongoDbUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddMongoDbGroundworkDocumentStore(connectionString, databaseName, autoApplyOnStartup);
        services.TryAddSingleton<IDiagnosticRecordDeploymentApplier>(
            _ => MongoDbDiagnosticRecordStoreFactory.CreateDeploymentApplier(connectionString, databaseName));
        services.TryAddSingleton<IDiagnosticRecordStoreSessionFactory>(
            _ => MongoDbDiagnosticRecordStoreFactory.CreateSessionFactory(connectionString, databaseName));
        services.AddGroundworkDiagnosticRecordDeploymentInitializer(autoApplyOnStartup);
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Resolved per lane rather than closed over this preset's single database. The preset is a
        // one-target default, not a promise that the lanes stay together: a host can bind the design lane to
        // a second target afterwards, and the tiles have to follow it there instead of quietly counting the
        // wrong database.
        services.AddGroundworkMongoDbWorkflowDashboardForLanes();
        return services;
    }
}
