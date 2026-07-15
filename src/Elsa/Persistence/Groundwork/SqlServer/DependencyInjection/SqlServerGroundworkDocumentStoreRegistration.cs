using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;

/// <summary>
/// Registers the admission-gated SQL Server Groundwork leaf. The initializer inspects the exact
/// host-selected physical target and never applies schema at runtime.
/// </summary>
public static class SqlServerGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqlServerGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.SelectGroundworkProviderLeaf("sqlserver");

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(SqlServerGroundworkDocumentStoreInitializer)))
        {
            return services;
        }

        services.AddGroundworkStorageComposition();
        services.RemoveAll<IDocumentStore>();
        services.RemoveAll<IBoundedDocumentStore>();
        services.TryAddSingleton<GroundworkDocumentStoreHolder>();

        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();
        services.AddSingleton<IGroundworkWorkflowExecutionStatePageQuery>(serviceProvider =>
            new SqlServerWorkflowExecutionStatePageQuery(
                connectionString,
                serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>(),
                serviceProvider.GetRequiredService<IGroundworkRuntimeDocumentSerializer>()));

        services.AddSingleton(serviceProvider => new SqlServerGroundworkDocumentStoreInitializer(
            connectionString,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider,
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>()));
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<SqlServerGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlServerGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(SqlServerGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(SqlServerGroundworkDocumentStoreRegistration)}.{nameof(AddSqlServerGroundworkDocumentStore)}"));
        services.AddSingleton<IDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>().Store);
        services.AddSingleton<IBoundedDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>().BoundedStore);
        return services;
    }
}
