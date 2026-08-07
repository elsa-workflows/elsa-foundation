using Elsa.Serialization.Core;
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
}
