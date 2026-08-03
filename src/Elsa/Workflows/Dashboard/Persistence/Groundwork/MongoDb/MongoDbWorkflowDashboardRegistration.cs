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

    public static IServiceCollection AddGroundworkMongoDbWorkflowPortfolio(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource>(provider =>
            new MongoDbWorkflowPortfolioDataSource(
                () => databaseFactory(provider),
                provider.GetRequiredService<IPayloadSerializer>())));
        return services;
    }
}
