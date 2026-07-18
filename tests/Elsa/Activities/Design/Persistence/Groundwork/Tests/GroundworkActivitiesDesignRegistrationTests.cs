using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
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
        var documents = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        services.AddSingleton<IDocumentStore>(documents);
        services.AddSingleton<IBoundedDocumentStore>(
            new GroundworkReusableActivityStoreTests.ActivityDesignTestBoundedDocumentStore(documents));
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        services.AddSingleton<ISystemClock, FakeClock>();
        services.AddSingleton<IDistributedLockProvider, ImmediateDistributedLockProvider>();
        preRegister?.Invoke(services);
        services.AddGroundworkActivitiesDesignStores();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_read_ports_commands_and_lookup_as_groundwork_implementations()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<GroundworkActivityDefinitionStore>(sp.GetRequiredService<IActivityDefinitionStore>());
        Assert.IsType<GroundworkActivityDefinitionVersionStore>(sp.GetRequiredService<IActivityDefinitionVersionStore>());
        Assert.IsType<GroundworkAddActivityDefinitionCommand>(sp.GetRequiredService<IAddActivityDefinitionCommand>());
        Assert.IsType<GroundworkAddActivityDefinitionVersionCommand>(sp.GetRequiredService<IAddCommand<ActivityDefinitionVersion>>());
        Assert.IsType<ActivityDefinitionLookup>(sp.GetRequiredService<IActivityDefinitionLookup>());
        Assert.IsType<GroundworkActivityAvailabilitySettingsStore>(sp.GetRequiredService<IActivityAvailabilitySettingsStore>());
        var reusable = Assert.IsType<GroundworkReusableActivityStores>(sp.GetRequiredService<IActivityDefinitionAuthoringStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityDefinitionDraftStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityDefinitionVersionPublicationStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityDefinitionLayoutStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityDraftValidationStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityForkStore>());
        Assert.Same(reusable, sp.GetRequiredService<IActivityDirectDependencyStore>());
        var projection = Assert.IsType<GroundworkActivityDependencyProjection>(sp.GetRequiredService<IActivityDependencyProjectionStore>());
        Assert.Same(projection, sp.GetRequiredService<IActivityDependencyProjectionRebuilder>());
        Assert.IsType<GroundworkActivityUpgradePlanStore>(sp.GetRequiredService<IActivityUpgradePlanStore>());
        Assert.Same(reusable, sp.GetRequiredService<ICreateActivityDefinitionCommand>());
        Assert.Same(reusable, sp.GetRequiredService<ISaveActivityForkCandidateCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IApplyActivityForkCandidateCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IUpdateActivityDefinitionPresentationCommand>());
        Assert.Same(reusable, sp.GetRequiredService<ICreateActivityDraftCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IReplaceActivityDraftCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IDiscardActivityDraftCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IStoreActivityDraftValidationCommand>());
        Assert.Same(reusable, sp.GetRequiredService<IChangeActivityVersionLifecycleCommand>());
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
        public Task<IReadOnlyList<Core.Entities.ActivityDefinition>> ListAsync(Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

}
