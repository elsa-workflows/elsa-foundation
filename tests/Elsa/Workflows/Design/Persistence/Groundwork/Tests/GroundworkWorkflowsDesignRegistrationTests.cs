using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowsDesignRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? preRegister = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<DesignGroundworkTestPersistence>();
        services.AddSingleton<IGroundworkStorageSessionSource>(sp => sp.GetRequiredService<DesignGroundworkTestPersistence>());
        services.AddSingleton<IPersistenceAccessContextAccessor>(DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        services.AddSingleton<IPayloadSerializer, FakePayloadSerializer>();
        services.AddSingleton<IDistributedLockProvider, StubLockProvider>();
        services.AddSingleton<StubEventPublisher>();
        services.AddSingleton<IInlineEventPublisher>(sp => sp.GetRequiredService<StubEventPublisher>());
        services.AddSingleton<IDeferredEventPublisher>(sp => sp.GetRequiredService<StubEventPublisher>());
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
        var concreteVersionStore = sp.GetRequiredService<GroundworkWorkflowDefinitionVersionStore>();
        Assert.Same(concreteVersionStore, sp.GetRequiredService<IWorkflowDefinitionVersionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionDraftStore>(sp.GetRequiredService<IWorkflowDefinitionDraftStore>());
        Assert.IsType<GroundworkWorkflowDefinitionListProjectionStore>(sp.GetRequiredService<IWorkflowDefinitionListProjectionStore>());
        Assert.IsType<GroundworkWorkflowDefinitionVersionLayoutStore>(sp.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>());
        Assert.IsType<GroundworkAddWorkflowDefinitionCommand>(sp.GetRequiredService<IAddWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkMaterializeWorkflowDefinitionCommand>(sp.GetRequiredService<IMaterializeWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkAddWorkflowDefinitionVersionCommand>(sp.GetRequiredService<IAddWorkflowDefinitionVersionCommand>());
        Assert.IsType<GroundworkMaterializeWorkflowDefinitionVersionCommand>(sp.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>());
        Assert.IsType<GroundworkSaveWorkflowDefinitionCommand>(sp.GetRequiredService<ISaveWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkDeleteWorkflowDefinitionPermanentlyCommand>(sp.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>());
        Assert.IsType<GroundworkCreateDraftCommand>(sp.GetRequiredService<ICreateDraftCommand>());
        Assert.IsType<GroundworkUpdateDraftCommand>(sp.GetRequiredService<IUpdateDraftCommand>());
        Assert.IsType<GroundworkDiscardDraftCommand>(sp.GetRequiredService<IDiscardDraftCommand>());
        Assert.IsType<GroundworkPromoteDraftToVersionCommand>(sp.GetRequiredService<IPromoteDraftToVersionCommand>());
        Assert.IsType<GroundworkSubmitWorkflowDefinitionCommand>(sp.GetRequiredService<ISubmitWorkflowDefinitionCommand>());
        Assert.IsType<GroundworkCloneDraftFromVersionCommand>(sp.GetRequiredService<ICloneDraftFromVersionCommand>());
        Assert.IsType<WorkflowDefinitionLookup>(sp.GetRequiredService<IWorkflowDefinitionLookup>());
        Assert.IsType<GroundworkDesignAtomicWrite>(sp.GetRequiredService<IDesignAtomicWriter>());
        Assert.IsType<DraftOriginator>(sp.GetRequiredService<IDraftOriginator>());
        var storage = sp.GetRequiredService<GroundworkDesignStorage>();
        Assert.IsType<GroundworkDesignStorage>(storage);
        Assert.Equal(5, WorkflowsDesignStorageManifest.CreateUnits().Count);
    }

    [Fact]
    public void Registers_default_entity_factories()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<WorkflowDefinitionFactory>(sp.GetRequiredService<IWorkflowDefinitionFactory>());
        Assert.IsType<WorkflowDefinitionVersionFactory>(sp.GetRequiredService<IWorkflowDefinitionVersionFactory>());
        Assert.IsType<WorkflowDefinitionDraftFactory>(sp.GetRequiredService<IWorkflowDefinitionDraftFactory>());
    }

    [Fact]
    public void Groundwork_registration_overrides_a_prior_store()
    {
        using var provider = BuildProvider(services => services.AddScoped<IWorkflowDefinitionStore, PriorStore>());
        using var scope = provider.CreateScope();
        Assert.IsType<GroundworkWorkflowDefinitionStore>(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.Single(scope.ServiceProvider.GetServices<IWorkflowDefinitionStore>());
    }

    [Fact]
    public void Groundwork_registration_preserves_a_prior_draft_originator()
    {
        using var provider = BuildProvider(services => services.AddScoped<IDraftOriginator, PriorDraftOriginator>());
        using var scope = provider.CreateScope();
        Assert.IsType<PriorDraftOriginator>(scope.ServiceProvider.GetRequiredService<IDraftOriginator>());
        Assert.Single(scope.ServiceProvider.GetServices<IDraftOriginator>());
    }

    [Fact]
    public void Groundwork_registration_preserves_a_prior_design_atomic_writer()
    {
        using var provider = BuildProvider(services => services.AddScoped<IDesignAtomicWriter, PriorDesignAtomicWriter>());
        using var scope = provider.CreateScope();
        Assert.IsType<PriorDesignAtomicWriter>(scope.ServiceProvider.GetRequiredService<IDesignAtomicWriter>());
        Assert.Single(scope.ServiceProvider.GetServices<IDesignAtomicWriter>());
    }

    [Fact]
    public void Groundwork_registration_allows_one_post_composition_design_atomic_writer_replacement()
    {
        var services = new ServiceCollection();
        services.AddGroundworkWorkflowsDesignStores();
        services.Replace(ServiceDescriptor.Scoped<IDesignAtomicWriter, PriorDesignAtomicWriter>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<PriorDesignAtomicWriter>(scope.ServiceProvider.GetRequiredService<IDesignAtomicWriter>());
        Assert.Single(scope.ServiceProvider.GetServices<IDesignAtomicWriter>());
    }

    [Fact]
    public void Groundwork_registration_allows_one_post_composition_draft_originator_replacement()
    {
        var services = new ServiceCollection();
        services.AddGroundworkWorkflowsDesignStores();
        services.Replace(ServiceDescriptor.Scoped<IDraftOriginator, PriorDraftOriginator>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<PriorDraftOriginator>(scope.ServiceProvider.GetRequiredService<IDraftOriginator>());
        Assert.Single(scope.ServiceProvider.GetServices<IDraftOriginator>());
    }

    [Fact]
    public void Repeated_registration_keeps_scoped_commands_and_stores_registered_once()
    {
        var services = new ServiceCollection();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkWorkflowsDesignStores();

        foreach (var serviceType in new[]
                 {
                     typeof(IWorkflowDefinitionStore), typeof(IWorkflowDefinitionVersionStore), typeof(IWorkflowDefinitionDraftStore),
                     typeof(IWorkflowDefinitionListProjectionStore), typeof(IWorkflowDefinitionVersionLayoutStore),
                     typeof(IAddWorkflowDefinitionCommand), typeof(IMaterializeWorkflowDefinitionCommand),
                     typeof(IAddWorkflowDefinitionVersionCommand), typeof(IMaterializeWorkflowDefinitionVersionCommand),
                     typeof(ISaveWorkflowDefinitionCommand), typeof(IDeleteWorkflowDefinitionPermanentlyCommand),
                     typeof(ICreateDraftCommand), typeof(IUpdateDraftCommand), typeof(IDiscardDraftCommand),
                     typeof(IPromoteDraftToVersionCommand), typeof(ISubmitWorkflowDefinitionCommand), typeof(ICloneDraftFromVersionCommand)
                 })
            AssertScopedOnce(services, serviceType);

        AssertScopedOnce(services, typeof(IDesignAtomicWriter));
        AssertScopedOnce(services, typeof(IDraftOriginator));
        AssertScopedOnce(services, typeof(GroundworkDesignStorage));
        Assert.Equal(5, WorkflowsDesignStorageManifest.CreateUnits().Count);
        Assert.Equal(5, WorkflowsDesignStorageManifest.CreateUnits().Select(unit => unit.Id.Value).Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertScopedOnce(IServiceCollection services, Type serviceType)
    {
        var registration = Assert.Single(services, x => x.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    private sealed class PriorStore : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PriorDraftOriginator : IDraftOriginator
    {
        public Task<string> ExecuteAsync<TRequest>(DesignOperationKey operationKey, string operationKind, TRequest requestMaterial, Func<CancellationToken, Task<DraftOriginationInput>> resolveInput, CancellationToken cancellationToken)
            where TRequest : notnull => Task.FromResult("prior-draft");
    }

    private sealed class PriorDesignAtomicWriter : IDesignAtomicWriter
    {
        public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(GroundworkDesignAtomicWriteRequest request, Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(GroundworkDesignAtomicWriteRequest request, Func<CancellationToken, Task>? beforeAttempt, Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubEventPublisher : IInlineEventPublisher, IDeferredEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}
