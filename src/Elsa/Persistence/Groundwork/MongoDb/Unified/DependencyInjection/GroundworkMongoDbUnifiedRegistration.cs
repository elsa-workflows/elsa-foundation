using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.MongoDb;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;

/// <summary>
/// Backs every provider-level Elsa store family — runtime, distributed runtime, workflows design,
/// activities design and publishing — with one MongoDB Groundwork target. Identity is selected
/// explicitly by its own feature.
/// </summary>
/// <remarks>
/// The storage session source admits each lane's declared units against this connection when the host
/// starts, so there is nothing for a caller to schedule or opt out of.
/// </remarks>
public static class GroundworkMongoDbUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The MongoDB connection string the single provider connection opens.</param>
    /// <param name="databaseName">The database every lane's collections live in.</param>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName) =>
        services.AddGroundworkMongoDbUnifiedPersistence(
            connectionString,
            databaseName,
            new WorkflowExecutableCacheOptions());

    /// <summary>Registers unified MongoDB persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
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
