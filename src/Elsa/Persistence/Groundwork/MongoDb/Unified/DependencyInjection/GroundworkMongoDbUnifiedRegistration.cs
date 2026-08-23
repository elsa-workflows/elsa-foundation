using CShells.Features;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.MongoDb;
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
        // The v2 provider takes one connection string, so the explicitly named database wins over any
        // database the caller happened to encode in the string itself.
        services.AddGroundworkStorageProviderConnection(_ => new MongoProviderFactory().Create(
            new MongoUrlBuilder(connectionString) { DatabaseName = databaseName }.ToString()));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Both tiles read the v2 lanes through the storage session source, so they need no connection
        // of their own and follow whatever target each lane is bound to.
        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
        return services;
    }
}
