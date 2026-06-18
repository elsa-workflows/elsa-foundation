using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Serialization.Core;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Verifies <see cref="GroundworkActivitiesDesignStoreRegistration.AddGroundworkActivitiesDesignStores"/> wires
/// every activity-design read port to its Groundwork (document) implementation and that the registration wins
/// over a previously-registered (e.g. EF Core) store — the single-provider host-composition contract.
/// </summary>
public class GroundworkActivitiesDesignRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? preRegister = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create()));
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        preRegister?.Invoke(services);
        services.AddGroundworkActivitiesDesignStores();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_both_read_ports_as_groundwork_implementations()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<GroundworkActivityDefinitionStore>(sp.GetRequiredService<IActivityDefinitionStore>());
        Assert.IsType<GroundworkActivityDefinitionVersionStore>(sp.GetRequiredService<IActivityDefinitionVersionStore>());
    }

    [Fact]
    public void Groundwork_registration_overrides_a_prior_store()
    {
        using var provider = BuildProvider(services =>
            services.AddScoped<IActivityDefinitionStore, PriorStore>());
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        Assert.IsType<GroundworkActivityDefinitionStore>(resolved);
        Assert.Single(scope.ServiceProvider.GetServices<IActivityDefinitionStore>());
    }

    private sealed class PriorStore : IActivityDefinitionStore
    {
        public Task<Core.Entities.ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.ActivityDefinition?> FindAsync(Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
