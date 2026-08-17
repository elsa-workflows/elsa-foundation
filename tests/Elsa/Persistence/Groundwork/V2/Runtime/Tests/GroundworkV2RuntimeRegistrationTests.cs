using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeRegistrationTests
{
    [Fact]
    public void Aggregate_registration_declares_the_complete_manifest_and_replaces_every_runtime_boundary()
    {
        var services = new ServiceCollection();

        services.AddGroundworkV2RuntimeStores(new WorkflowExecutableCacheOptions { Enabled = false });

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.CreateUnits().Select(unit => unit.Id.Value).Order(StringComparer.Ordinal),
            registry.Registrations.Select(registration => registration.Unit.Id.Value));

        AssertScopedAlias<IBookmarkStateStore, GroundworkV2BookmarkStateStore>(services);
        AssertScopedAlias<IBookmarkStimulusIndex, GroundworkV2BookmarkStateStore>(services);
        AssertScopedAlias<IWorkflowExecutableStore, GroundworkV2WorkflowExecutableStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateStore, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateReader, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IExecutableActivityTemplateWriter, GroundworkV2ExecutableActivityTemplateStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceStore, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceReader, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IWorkflowExecutableSourceReferenceWriter, GroundworkV2WorkflowExecutableSourceReferenceStore>(services);
        AssertScopedAlias<IActivityExecutionStateStore, GroundworkV2ActivityExecutionStateStore>(services);
        AssertScopedAlias<IActivityExecutionInspectionStore, GroundworkV2ActivityExecutionInspectionStore>(services);
        AssertScopedAlias<IActivityExecutionInspectionWriter, GroundworkV2ActivityExecutionInspectionStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyStore, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyReader, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IActivityExecutionHierarchyWriter, GroundworkV2ActivityExecutionHierarchyStore>(services);
        AssertScopedAlias<IWorkflowExecutionStateStore, GroundworkV2WorkflowExecutionStateStore>(services);
        AssertScopedAlias<IWorkflowAlterationStore, GroundworkV2WorkflowAlterationStore>(services);
        AssertScopedAlias<IWorkflowTestScopeStore, GroundworkV2WorkflowTestScopeStore>(services);
        AssertScopedAlias<IWorkflowTestScopeAdmissionStore, GroundworkV2WorkflowTestScopeStore>(services);
        AssertScopedAlias<IWorkflowTestScopeCleanupStore, GroundworkV2WorkflowTestScopeCleanupStore>(services);
        AssertScopedAlias<IDurableValueStateStore, GroundworkV2DurableValueStateStore>(services);
        AssertScopedAlias<ISchedulerStateStore, GroundworkV2SchedulerStateStore>(services);
        AssertScopedAlias<IExecutionLivenessStateStore, GroundworkV2ExecutionLivenessStateStore>(services);
        AssertScopedAlias<IRuntimeRecoveryScanner, GroundworkV2RuntimeRecoveryScanner>(services);
        AssertScopedAlias<IWorkflowHoldStateStore, GroundworkV2WorkflowHoldStateStore>(services);
        AssertScopedAlias<IIncidentStateStore, GroundworkV2IncidentStateStore>(services);
        AssertScopedAlias<IWorkflowRuntimeAttentionQuery, GroundworkV2WorkflowRuntimeAttentionQuery>(services);
        AssertScopedAlias<IWorkflowDispatchStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchQueryStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchDeleteStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchRetentionRootStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchAdmissionStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IWorkflowDispatchCancellationStore, GroundworkV2WorkflowDispatchStore>(services);
        AssertScopedAlias<IRuntimeCheckpointCommitStore, GroundworkV2RuntimeCheckpointWriter>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IPostCommitOutboxLookupStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxClaimStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IRuntimePostCommitOutboxClaimCompletionStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IWorkflowDispatchRedriveStore, GroundworkV2RuntimePostCommitOutboxStore>(services);
        AssertScopedAlias<IWorkflowSchedulerWorkQueue, GroundworkV2WorkflowSchedulerWorkQueue>(services);
        AssertScopedAlias<IWorkflowSchedulerWorkClaimInspection, GroundworkV2WorkflowSchedulerWorkQueue>(services);
        AssertScopedAlias<IWorkflowSchedulerPoisonStore, GroundworkV2WorkflowSchedulerPoisonStore>(services);
        AssertScopedAlias<IDurableTimerStore, GroundworkV2DurableTimerStateStore>(services);
        AssertScopedAlias<IWorkflowTriggerBindingStore, GroundworkV2WorkflowTriggerBindingStore>(services);
        AssertScopedAlias<IRecurringTriggerScheduleStore, GroundworkV2RecurringTriggerScheduleStore>(services);
    }

    [Fact]
    public void Repeated_default_registration_is_idempotent_and_retains_the_bounded_cache_boundary()
    {
        var services = new ServiceCollection();

        services.AddGroundworkV2RuntimeStores();
        services.AddGroundworkV2RuntimeStores();

        var options = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions));
        var configured = Assert.IsType<WorkflowExecutableCacheOptions>(options.ImplementationInstance);
        Assert.True(configured.Enabled);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, configured.Capacity);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) && !descriptor.IsKeyedService);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) && descriptor.IsKeyedService);
        Assert.Equal(
            4,
            services.Count(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence)));

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(ElsaRuntimeV2StorageManifest.CreateUnits().Count, registry.Registrations.Count);
    }

    [Fact]
    public void Named_public_provider_composition_resolves_cache_modes_and_atomic_durability_evidence()
    {
        using var connection = new SqliteProviderFactory().Create("Data Source=:memory:");
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection, "runtime")
            .AddGroundworkV2RuntimeStores(targetName: "runtime");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using (var ordinaryScope = provider.CreateScope())
        {
            var store = ordinaryScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<CachingWorkflowExecutableStore>(store);
        }

        using (var privilegedScope = provider.CreateScope())
        {
            privilegedScope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("runtime-maintenance")));
            var store = privilegedScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<InvalidatingWorkflowExecutableStore>(store);

            var checkpoint = Assert.Single(
                privilegedScope.ServiceProvider.GetServices<IWorkflowDispatchDurabilityEvidence>(),
                evidence => evidence.Component == WorkflowDispatchDurabilityComponents.Checkpoint);
            Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, checkpoint.Level);
        }
    }

    private static void AssertScopedAlias<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var implementation = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TImplementation));
        Assert.Equal(ServiceLifetime.Scoped, implementation.Lifetime);
        Assert.NotNull(implementation.ImplementationFactory);

        var contract = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TContract));
        Assert.Equal(ServiceLifetime.Scoped, contract.Lifetime);
        Assert.NotNull(contract.ImplementationFactory);
    }
}
