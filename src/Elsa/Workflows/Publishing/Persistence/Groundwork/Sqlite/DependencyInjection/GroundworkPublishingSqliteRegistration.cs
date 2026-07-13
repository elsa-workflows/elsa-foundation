using CShells.Lifecycle;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite.DependencyInjection;

public static class GroundworkPublishingSqliteRegistration
{
    public static IServiceCollection AddGroundworkPublishingSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<PublishingGroundworkDocumentStoreHolder>();
        services.AddSingleton(sp => new PublishingGroundworkSqliteInitializer(
            connectionString,
            sp.GetRequiredService<PublishingGroundworkDocumentStoreHolder>()));
        services.AddHostedService(sp => sp.GetRequiredService<PublishingGroundworkSqliteInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<PublishingGroundworkSqliteInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(PublishingGroundworkSqliteInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(GroundworkPublishingSqliteRegistration)}.{nameof(AddGroundworkPublishingSqlitePersistence)}"));
        services.AddGroundworkPublishingStores(sp =>
            sp.GetRequiredService<PublishingGroundworkDocumentStoreHolder>().Store);
        return services;
    }
}
