using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.MongoDb.Targets;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;

public static class MongoDbWorkflowDashboardRegistration
{
    public static IServiceCollection AddGroundworkMongoDbWorkflowRunHealth(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowRunHealthDataSource>(provider =>
            new MongoDbWorkflowRunHealthDataSource(() => databaseFactory(provider))));
        return services;
    }

    /// <param name="databaseFactory">Opens the design lane's database.</param>
    /// <param name="runtimeDatabaseFactory">
    /// Opens the runtime lane's database when it is a different one. Pass <c>null</c> when the lanes share a
    /// target, which keeps the correlated <c>$lookup</c> path.
    /// </param>
    public static IServiceCollection AddGroundworkMongoDbWorkflowPortfolio(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        Func<IServiceProvider, IMongoDatabase>? runtimeDatabaseFactory = null)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource>(provider =>
            new MongoDbWorkflowPortfolioDataSource(
                () => databaseFactory(provider),
                provider.GetRequiredService<IPayloadSerializer>(),
                runtimeDatabaseFactory is null ? null : () => runtimeDatabaseFactory(provider))));
        return services;
    }

    /// <summary>
    /// Wires both tiles to whichever target each lane actually landed on, rather than to one database chosen
    /// at registration time.
    /// <para>
    /// This is what makes a split MongoDB host work. The overloads above close over the database the caller
    /// happened to have, so a host that later binds the design lane to a second target keeps reading the
    /// first one: the portfolio tile then runs its co-located <c>$lookup</c> against a database holding no
    /// source-reference collection and reports a published count of <b>zero</b> instead of failing. Resolving
    /// per lane removes that whole class of answer, because the tile can no longer be pointed at a database
    /// that does not hold the lane it is counting.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGroundworkMongoDbWorkflowDashboardForLanes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Scoped<IWorkflowRunHealthDataSource>(provider =>
        {
            var runtime = RequireDatabaseSource(provider, RuntimeTargetOf(provider), "workflow run health");
            return new MongoDbWorkflowRunHealthDataSource(runtime.GetDatabase);
        }));

        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource>(provider =>
        {
            var designTarget = DesignTargetOf(provider);
            var runtimeTarget = RuntimeTargetOf(provider);
            var design = RequireDatabaseSource(provider, designTarget, "workflow portfolio (design lane)");
            var colocated = string.Equals(designTarget, runtimeTarget, StringComparison.Ordinal);
            // Same database name on two targets is still two stores -- different clusters, say -- so the
            // split path is chosen from the target names, not from the database names looking alike.
            var runtime = colocated
                ? null
                : RequireDatabaseSource(provider, runtimeTarget, "workflow portfolio (runtime lane)");

            return new MongoDbWorkflowPortfolioDataSource(
                design.GetDatabase,
                provider.GetRequiredService<IPayloadSerializer>(),
                runtime is null ? null : runtime.GetDatabase);
        }));

        return services;
    }

    private static string DesignTargetOf(IServiceProvider provider) =>
        provider.GetRequiredService<GroundworkLaneTargets>()
            .For(typeof(WorkflowsDesignGroundworkStorageManifestSource));

    private static string RuntimeTargetOf(IServiceProvider provider) =>
        provider.GetRequiredService<GroundworkLaneTargets>()
            .For(typeof(RuntimeGroundworkStorageManifestSource));

    private static IGroundworkTargetMongoDatabaseSource RequireDatabaseSource(
        IServiceProvider provider,
        string targetName,
        string tile)
    {
        return provider.GetKeyedService<IGroundworkTargetMongoDatabaseSource>(targetName)
               ?? throw new InvalidOperationException(
                   $"The {tile} tile reads Groundwork collections directly and needs the MongoDB database " +
                   $"for target '{targetName}', but no MongoDB provider leaf declared it. A host that mixes " +
                   "providers across lanes cannot serve this tile from MongoDB; bind the lane to a MongoDB " +
                   "target, or disable the dashboard persistence feature.");
    }
}
