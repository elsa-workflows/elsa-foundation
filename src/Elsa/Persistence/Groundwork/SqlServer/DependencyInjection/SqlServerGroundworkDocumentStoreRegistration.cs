using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Core;
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
        string connectionString,
        bool autoApplyOnStartup = false)
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
        services.AddGroundworkStoreSessions();

        services.AddSingleton(serviceProvider => new SqlServerGroundworkDocumentStoreInitializer(
            connectionString,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<GroundworkStoreSessionSource>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SqlServerGroundworkDocumentStoreInitializer>>()));
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<SqlServerGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlServerGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(SqlServerGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(SqlServerGroundworkDocumentStoreRegistration)}.{nameof(AddSqlServerGroundworkDocumentStore)}"));
        return services;
    }
}
