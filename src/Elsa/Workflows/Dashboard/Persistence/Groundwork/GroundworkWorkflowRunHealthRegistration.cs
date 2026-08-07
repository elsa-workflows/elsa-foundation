using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

public static class GroundworkWorkflowRunHealthRegistration
{
    /// <summary>
    /// Registers the run-health tile against the runtime lane's database. Run health reads only runtime
    /// documents, so one connection is always enough.
    /// </summary>
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

    /// <summary>
    /// Registers the portfolio tile. It counts design-lane definitions and drafts and how many are published,
    /// which is a runtime-lane fact, so it needs both lanes.
    /// </summary>
    /// <param name="connectionFactory">Opens the design lane's database.</param>
    /// <param name="runtimeConnectionFactory">
    /// Opens the runtime lane's database when it is a different one. Pass <c>null</c> when the lanes share a
    /// target, which keeps the single-statement path.
    /// </param>
    public static IServiceCollection AddGroundworkWorkflowPortfolio(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        GroundworkRunHealthDialect dialect,
        Func<IServiceProvider, DbConnection>? runtimeConnectionFactory = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        services.Replace(ServiceDescriptor.Scoped<IWorkflowPortfolioDataSource>(provider =>
            new GroundworkWorkflowPortfolioDataSource(
                () => connectionFactory(provider),
                dialect,
                provider.GetRequiredService<IPayloadSerializer>(),
                runtimeConnectionFactory is null ? null : () => runtimeConnectionFactory(provider))));
        return services;
    }
}
