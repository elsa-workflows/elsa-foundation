using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Verifies <see cref="GroundworkWorkflowsDesignStoreRegistration.AddGroundworkWorkflowsDesignStores"/> wires
/// every workflow-design read port to its Groundwork (document) implementation and that the registration wins
/// over a previously-registered (e.g. EF Core) store — the single-provider host-composition contract.
/// </summary>
public class GroundworkWorkflowsDesignRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? preRegister = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create()));
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        services.AddSingleton<IDistributedLockProvider, StubLockProvider>();
        services.AddSingleton<IEventPublisher, StubEventPublisher>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddSingleton<ISystemClock, FakeSystemClock>();
        preRegister?.Invoke(services);
        services.AddGroundworkWorkflowsDesignStores();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_read_ports_commands_and_lookups_as_groundwork_implementations()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<GroundworkWorkflowDefinitionStore>(sp.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionVersionStore>(sp.GetRequiredService<IWorkflowDefinitionVersionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionDraftStore>(sp.GetRequiredService<IWorkflowDefinitionDraftStore>());
        Assert.IsType<GroundworkWorkflowDefinitionVersionLayoutStore>(sp.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>());
        Assert.IsType<GroundworkAddWorkflowDefinitionCommand>(sp.GetRequiredService<IAddWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkAddWorkflowDefinitionVersionCommand>(sp.GetRequiredService<IAddCommand<WorkflowDefinitionVersion>>());
        Assert.IsType<GroundworkSaveWorkflowDefinitionCommand>(sp.GetRequiredService<ISaveWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkDeleteWorkflowDefinitionPermanentlyCommand>(sp.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>());
        Assert.IsType<GroundworkCreateDraftCommand>(sp.GetRequiredService<ICreateDraftCommand>());
        Assert.IsType<GroundworkUpdateDraftCommand>(sp.GetRequiredService<IUpdateDraftCommand>());
        Assert.IsType<GroundworkDiscardDraftCommand>(sp.GetRequiredService<IDiscardDraftCommand>());
        Assert.IsType<GroundworkPromoteDraftToVersionCommand>(sp.GetRequiredService<IPromoteDraftToVersionCommand>());
        Assert.IsType<GroundworkSubmitWorkflowDefinitionCommand>(sp.GetRequiredService<ISubmitWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkCloneDraftFromVersionCommand>(sp.GetRequiredService<ICloneDraftFromVersionCommand>());
        Assert.IsType<WorkflowDefinitionLookup>(sp.GetRequiredService<IWorkflowDefinitionLookup>());
    }

    [Fact]
    public void Groundwork_registration_overrides_a_prior_store()
    {
        using var provider = BuildProvider(services =>
            services.AddScoped<IWorkflowDefinitionStore, PriorStore>());
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        Assert.IsType<GroundworkWorkflowDefinitionStore>(resolved);
        Assert.Single(scope.ServiceProvider.GetServices<IWorkflowDefinitionStore>());
    }

    private sealed class PriorStore : IWorkflowDefinitionStore
    {
        public Task<Core.Entities.WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Core.Entities.WorkflowDefinition>> ListAsync(Core.Filters.WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
    }
}
