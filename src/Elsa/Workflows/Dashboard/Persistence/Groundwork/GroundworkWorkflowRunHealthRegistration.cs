using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

public static class GroundworkWorkflowRunHealthRegistration
{
    public static IServiceCollection AddGroundworkWorkflowRunHealth(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        GroundworkRunHealthDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowRunHealthDataSource>(provider =>
            new GroundworkWorkflowRunHealthDataSource(() => connectionFactory(provider), dialect)));
        return services;
    }

    public static IServiceCollection AddGroundworkWorkflowPortfolio(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        GroundworkRunHealthDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource>(provider =>
            new GroundworkWorkflowPortfolioDataSource(
                () => connectionFactory(provider),
                dialect,
                provider.GetRequiredService<IPayloadSerializer>())));
        return services;
    }
}
