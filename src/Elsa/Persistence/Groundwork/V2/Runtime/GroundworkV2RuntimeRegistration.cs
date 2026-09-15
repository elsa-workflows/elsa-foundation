using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Atomically replaces the complete workflow-runtime persistence family with public Groundwork v2 stores.
/// </summary>
public static class GroundworkV2RuntimeRegistration
{
    /// <summary>Key for the uncached provider store behind the optional executable cache.</summary>
    public const string WorkflowExecutableProviderKey = "Elsa.Persistence.Groundwork.V2.WorkflowExecutableProvider";

    /// <summary>Registers the complete runtime family with bounded executable caching enabled.</summary>
    public static IServiceCollection AddGroundworkV2RuntimeStores(
        this IServiceCollection services,
        string? targetName = null) =>
        services.AddGroundworkV2RuntimeStores(new WorkflowExecutableCacheOptions(), targetName);

    /// <summary>Registers the complete runtime family with explicit executable-cache settings.</summary>
    public static IServiceCollection AddGroundworkV2RuntimeStores(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        var snapshot = services.ToArray();
        var registry = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkStorageUnitRegistry>()
            .SingleOrDefault();
        var registrySnapshot = registry?.Registrations;
        try
        {
            var cacheOptions = CopyAndValidate(workflowExecutableCacheOptions);
            var existingActivityExecutionBackend = RuntimeActivityExecutionStoreBackend.Find(services);
            RuntimeActivityExecutionStoreBackend.EnsureCheckpointCompositionCompatible(existingActivityExecutionBackend, RuntimeActivityExecutionStoreBackend.Groundwork);
            var existingDispatchBackend = RuntimeWorkflowDispatchStoreBackend.Find(services);
            if (existingDispatchBackend is null)
                RuntimeWorkflowDispatchStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(
                    services,
                    RuntimeWorkflowDispatchStoreBackend.CaptureContractRegistrations(services));
            else
                existingDispatchBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingDispatchRemoval = existingDispatchBackend is not null && existingDispatchBackend.Name != RuntimeWorkflowDispatchStoreBackend.Groundwork
                ? existingDispatchBackend.PrepareRemoveOwnedArtifacts(services)
                : null;
            var existingOutboxBackend = RuntimePostCommitOutboxStoreBackend.Find(services);
            if (existingOutboxBackend is null)
                RuntimePostCommitOutboxStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(
                    services,
                    RuntimePostCommitOutboxStoreBackend.CaptureContractRegistrations(services));
            else
                existingOutboxBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingOutboxRemoval = existingOutboxBackend is not null && existingOutboxBackend.Name != RuntimePostCommitOutboxStoreBackend.Groundwork
                ? existingOutboxBackend.PrepareRemoveOwnedArtifacts(services)
                : null;
            var existingCheckpointBackend = RuntimeCheckpointCommitStoreBackend.Find(services);
            if (existingCheckpointBackend is null)
            {
                var checkpointContracts = services.Where(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore)).ToArray();
                if (checkpointContracts.Length > 1 || checkpointContracts.Any(descriptor => !RuntimeCheckpointCommitStoreBackend.IsRuntimeDefault(descriptor)))
                    throw new InvalidOperationException("Groundwork runtime refuses to replace an unowned checkpoint writer.");
            }
            else
                existingCheckpointBackend.EnsureOwnsRegisteredContract(services);
            var existingQueueBackend = SchedulerWorkQueueStoreBackend.Find(services);
            if (existingQueueBackend is null)
                SchedulerWorkQueueStoreBackend.EnsureNoUnownedRegistrations(services);
            else
                existingQueueBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingQueueRemoval = existingQueueBackend is not null && existingQueueBackend.Name != SchedulerWorkQueueStoreBackend.Groundwork
                ? existingQueueBackend.PrepareRemoveOwnedArtifacts(services)
                : null;
            var existingTimerBackend = DurableTimerStoreBackend.Find(services);
            if (existingTimerBackend is null)
                DurableTimerStoreBackend.EnsureNoUnownedRegistrations(services);
            else
                existingTimerBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingTimerRemoval = existingTimerBackend is not null && existingTimerBackend.Name != DurableTimerStoreBackend.Groundwork
                ? existingTimerBackend.PrepareRemoveOwnedArtifacts(services)
                : null;
            var existingWorkflowExecutionStateBackend = WorkflowExecutionStateStoreBackend.Find(services);
            existingWorkflowExecutionStateBackend?.EnsureOwnsRegisteredContract(services);
            if (existingWorkflowExecutionStateBackend is null && services.Any(descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore) && descriptor.ImplementationType != typeof(Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowExecutionStateStore)))
                throw new InvalidOperationException("Groundwork runtime refuses to replace an unowned workflow execution state store.");
            var commitExistingWorkflowExecutionRemoval = existingWorkflowExecutionStateBackend?.PrepareRemoveOwnedArtifacts(services);
            var existingAlterationBackend = RuntimeWorkflowAlterationStoreBackend.Find(services);
            if (existingAlterationBackend is null)
                RuntimeWorkflowAlterationStoreBackend.EnsureNoUnownedRegistrations(services);
            else
                existingAlterationBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingAlterationRemoval = existingAlterationBackend is null || existingAlterationBackend.Name == RuntimeWorkflowAlterationStoreBackend.Groundwork
                ? null
                : existingAlterationBackend.PrepareRemoveOwnedArtifacts(services);
            if (existingAlterationBackend is not null && existingAlterationBackend.Name != RuntimeWorkflowAlterationStoreBackend.Groundwork)
                services.RemoveAll<WorkflowAlterationProviderRegistration>();

            var existingTestScopeBackend = WorkflowTestScopeStoreBackend.Find(services);
            if (existingTestScopeBackend is null)
                WorkflowTestScopeStoreBackend.EnsureNoUnownedRegistrations(services);
            else
                existingTestScopeBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingTestScopeRemoval = existingTestScopeBackend is null || existingTestScopeBackend.Name == WorkflowTestScopeStoreBackend.Groundwork
                ? null
                : existingTestScopeBackend.PrepareRemoveOwnedArtifacts(services);
            if (existingTestScopeBackend is not null && existingTestScopeBackend.Name != WorkflowTestScopeStoreBackend.Groundwork)
                services.RemoveAll<WorkflowTestScopeProviderRegistration>();
            var existingOperationalStateBackend = RuntimeOperationalStateStoreBackend.Find(services);
            if (existingOperationalStateBackend is null)
                RuntimeOperationalStateStoreBackend.EnsureNoUnownedRegistrations(services);
            else
                existingOperationalStateBackend.EnsureOwnsRegisteredContracts(services);
            var commitExistingOperationalStateRemoval = existingOperationalStateBackend is null || existingOperationalStateBackend.Name == RuntimeOperationalStateStoreBackend.Groundwork
                ? null
                : existingOperationalStateBackend.PrepareRemoveOwnedArtifacts(services);
            var target = BindRuntimeTarget(services, targetName);
            if (existingActivityExecutionBackend is null)
                RuntimeActivityExecutionStoreBackend.EnsureNoUnownedRegistrations(services);
            else
            {
                existingActivityExecutionBackend.EnsureOwnsRegisteredContracts(services);
            }
            var commitExistingActivityExecutionBackendRemoval = existingActivityExecutionBackend?.PrepareRemoveOwnedArtifacts(services);
            // Withdraw the previous backend's provider-owned artifacts before the new
            // registration re-adds the manifest units. The service-collection snapshot
            // and registry snapshot above still make this atomic on a later failure.
            commitExistingActivityExecutionBackendRemoval?.Invoke(services);

        services.AddPersistenceCore();
        ReplaceExistingArtifactBackend(services);
        // Recovery cursors may outlive this process or be consumed by another node. Groundwork therefore refuses
        // the runtime core's development-only ephemeral signer unless the host supplies RuntimeRecoveryContinuationOptions.SigningKey.
        services.AddOptions<RuntimeRecoveryContinuationOptions>()
            .Configure(options => options.AllowEphemeralDevelopmentKey = false);
        // Register the durable protector here as well as in AddWorkflowRuntime so direct Groundwork composition
        // cannot accidentally construct a scanner without an authenticated continuation boundary. Resolution fails
        // closed until the host supplies a stable signing key through RuntimeRecoveryContinuationOptions.
        services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
        // Registered here as well as in AddWorkflowRuntime so a host that composes only these durable stores (a
        // worker, a test harness) still fails activation on a missing key rather than on every recovery sweep.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
        services.ClaimWorkflowTestScopeProvider(typeof(GroundworkV2WorkflowTestScopeStore));
        services.ClaimWorkflowAlterationProvider(typeof(GroundworkV2WorkflowAlterationStore));

        RegisterExecutableStore(services, cacheOptions, target);
        var existingBookmarkBackend = BookmarkStateStoreBackend.Find(services);
        if (existingBookmarkBackend is null)
        {
            BookmarkStateStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(services);
            BookmarkStateStoreBackend.RemoveDefaultStimulusIndex(services);
            BookmarkStateStoreBackend.RemoveDefaultStateStore(services);
        }
        else
            existingBookmarkBackend.RemoveOwnedArtifacts(services);
        services.RemoveAll<BookmarkStateStoreBackend>();
        ReplaceScoped<GroundworkV2BookmarkStateStore>(services, Standard<GroundworkV2BookmarkStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IBookmarkStateStore), typeof(IBookmarkStimulusIndex));
        var ownedBookmarkStoreRegistration = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore));
        var bookmarkDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        var bookmarkIndexDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
        BookmarkStateStoreBackend.Register(services, new BookmarkStateStoreBackend(
            BookmarkStateStoreBackend.Groundwork,
            bookmarkDescriptor,
            bookmarkIndexDescriptor,
            collection =>
            {
                RemoveGroundworkBookmarkArtifacts(collection, ownedBookmarkStoreRegistration);
                GroundworkV2RuntimeUnitWithdrawal.RemoveBookmarkState(collection, target);
            }));
        // Withdraw the previous workflow declaration before the replacement units are declared.
        // This keeps repeated Groundwork registration idempotent while allowing EF->Groundwork
        // switching to remove the stale workflow unit through the prior backend's callback.
        commitExistingWorkflowExecutionRemoval?.Invoke(services);
        // Withdraw the old checkpoint unit before the manifest declares the new one. Removing it
        // after declaration would silently drop checkpoint storage on repeated Groundwork composition.
        existingCheckpointBackend?.RemoveOwnedRegistrations(services);
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, target);
        ReplaceScoped<GroundworkV2ExecutableActivityTemplateStore>(services, Standard<GroundworkV2ExecutableActivityTemplateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IExecutableActivityTemplateStore), typeof(IExecutableActivityTemplateReader), typeof(IExecutableActivityTemplateWriter));
        ReplaceScoped<GroundworkV2WorkflowExecutableSourceReferenceStore>(services, Standard<GroundworkV2WorkflowExecutableSourceReferenceStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowExecutableSourceReferenceStore), typeof(IWorkflowExecutableSourceReferenceReader), typeof(IWorkflowExecutableSourceReferenceWriter));
        ReplaceScoped<GroundworkV2ActivityExecutionStateStore>(services, Standard<GroundworkV2ActivityExecutionStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IActivityExecutionStateStore));
        ReplaceScoped<GroundworkV2ActivityExecutionInspectionStore>(services, Standard<GroundworkV2ActivityExecutionInspectionStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IActivityExecutionInspectionStore), typeof(IActivityExecutionInspectionWriter));
        ReplaceScoped<GroundworkV2ActivityExecutionHierarchyStore>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetService<IActivityExecutionHierarchyCursorCodec>(),
                target),
            typeof(IActivityExecutionHierarchyStore), typeof(IActivityExecutionHierarchyReader), typeof(IActivityExecutionHierarchyWriter));
        ReplaceScoped<GroundworkV2WorkflowExecutionStateStore>(services, Standard<GroundworkV2WorkflowExecutionStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowExecutionStateStore));
        services.RemoveAll<WorkflowExecutionStateStoreBackend>();
        var groundworkExecutionDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
        var groundworkExecutionConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowExecutionStateStore));
        WorkflowExecutionStateStoreBackend.Register(services, new WorkflowExecutionStateStoreBackend(
            WorkflowExecutionStateStoreBackend.Groundwork,
            [groundworkExecutionDescriptor, groundworkExecutionConcreteDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveWorkflowExecutionState(collection, target)));
        ReplaceScoped<GroundworkV2WorkflowAlterationStore>(services, Standard<GroundworkV2WorkflowAlterationStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowAlterationStore));
        services.RemoveAll<RuntimeWorkflowAlterationStoreBackend>();
        var groundworkAlterationConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowAlterationStore));
        var groundworkAlterationContractDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowAlterationStore));
        RuntimeWorkflowAlterationStoreBackend.Register(services, new(
            RuntimeWorkflowAlterationStoreBackend.Groundwork,
            [groundworkAlterationConcreteDescriptor, groundworkAlterationContractDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveWorkflowAlterations(collection, target)));
        ReplaceScoped<GroundworkV2WorkflowTestScopeStore>(services, Standard<GroundworkV2WorkflowTestScopeStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTestScopeStore), typeof(IWorkflowTestScopeAdmissionStore));
        ReplaceScoped<GroundworkV2WorkflowTestScopeCleanupStore>(services, Standard<GroundworkV2WorkflowTestScopeCleanupStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTestScopeCleanupStore));
        services.RemoveAll<WorkflowTestScopeStoreBackend>();
        var groundworkTestScopeConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeStore));
        var groundworkTestScopeCleanupDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowTestScopeCleanupStore));
        var groundworkTestScopeStoreDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeStore));
        var groundworkTestScopeAdmissionDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeAdmissionStore));
        var groundworkTestScopeCleanupContractDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
        WorkflowTestScopeStoreBackend.Register(services, new(
            WorkflowTestScopeStoreBackend.Groundwork,
            [groundworkTestScopeConcreteDescriptor, groundworkTestScopeCleanupDescriptor, groundworkTestScopeStoreDescriptor, groundworkTestScopeAdmissionDescriptor, groundworkTestScopeCleanupContractDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveWorkflowTestScope(collection, target)));
        ReplaceScoped<GroundworkV2DurableValueStateStore>(services, Standard<GroundworkV2DurableValueStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IDurableValueStateStore));
        ReplaceScoped<GroundworkV2SchedulerStateStore>(services, Standard<GroundworkV2SchedulerStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(ISchedulerStateStore));
        services.RemoveAll<RuntimeOperationalStateStoreBackend>();
        ReplaceScoped<GroundworkV2ExecutionLivenessStateStore>(services, Standard<GroundworkV2ExecutionLivenessStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IExecutionLivenessStateStore));
        ReplaceScoped<GroundworkV2RuntimeRecoveryScanner>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                target,
                provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>()),
            typeof(IRuntimeRecoveryScanner));
        ReplaceScoped<GroundworkV2WorkflowHoldStateStore>(services, Standard<GroundworkV2WorkflowHoldStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowHoldStateStore));
        var groundworkDurableValueDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IDurableValueStateStore));
        var groundworkSchedulerDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(ISchedulerStateStore));
        var groundworkDurableValueConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2DurableValueStateStore));
        var groundworkSchedulerConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2SchedulerStateStore));
        var groundworkLivenessDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IExecutionLivenessStateStore));
        var groundworkLivenessConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2ExecutionLivenessStateStore));
        var groundworkRecoveryDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IRuntimeRecoveryScanner));
        var groundworkHoldDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowHoldStateStore));
        var groundworkHoldConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowHoldStateStore));
        ReplaceScoped<GroundworkV2IncidentStateStore>(services, Standard<GroundworkV2IncidentStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IIncidentStateStore));
        ReplaceScoped<GroundworkV2WorkflowRuntimeAttentionQuery>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetService<TimeProvider>(),
            target),
            typeof(IWorkflowRuntimeAttentionQuery));
        var groundworkIncidentDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IIncidentStateStore));
        var groundworkIncidentConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2IncidentStateStore));
        var groundworkAttentionDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowRuntimeAttentionQuery));
        RuntimeOperationalStateStoreBackend.Register(services, new(
            RuntimeOperationalStateStoreBackend.Groundwork,
            [groundworkDurableValueDescriptor, groundworkSchedulerDescriptor, groundworkDurableValueConcreteDescriptor, groundworkSchedulerConcreteDescriptor,
                groundworkLivenessDescriptor, groundworkLivenessConcreteDescriptor, groundworkRecoveryDescriptor,
                groundworkHoldDescriptor, groundworkHoldConcreteDescriptor, groundworkIncidentDescriptor, groundworkIncidentConcreteDescriptor,
                groundworkAttentionDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveOperationalState(collection, target)));
        services.RemoveAll<RuntimeWorkflowDispatchStoreBackend>();
        ReplaceScoped<GroundworkV2WorkflowDispatchStore>(services, Standard<GroundworkV2WorkflowDispatchStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowDispatchStore), typeof(IWorkflowDispatchQueryStore), typeof(IWorkflowDispatchDeleteStore),
            typeof(IWorkflowDispatchRetentionRootStore), typeof(IWorkflowDispatchAdmissionStore), typeof(IWorkflowDispatchCancellationStore));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2DispatchStoreDurabilityEvidence>());
        var groundworkDispatchEvidenceDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence) &&
            descriptor.ImplementationType == typeof(GroundworkV2DispatchStoreDurabilityEvidence));
        var groundworkDispatchConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowDispatchStore));
        var groundworkDispatchContractDescriptors = RuntimeWorkflowDispatchStoreBackend.CaptureContractRegistrations(services);
        RuntimeWorkflowDispatchStoreBackend.Register(services, new(
            RuntimeWorkflowDispatchStoreBackend.Groundwork,
            [groundworkDispatchConcreteDescriptor, groundworkDispatchEvidenceDescriptor, .. groundworkDispatchContractDescriptors],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveWorkflowDispatch(collection, target)));
        ReplaceScoped<GroundworkV2RuntimeCheckpointWriter>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                target,
                provider.GetService<TimeProvider>(),
                provider.GetService<IWorkflowExecutableRootWriteLeaseManager>()),
            typeof(IRuntimeCheckpointCommitStore));
        var groundworkCheckpointConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2RuntimeCheckpointWriter));
        var groundworkCheckpointContractDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2CheckpointDurabilityEvidence>());
        var groundworkCheckpointEvidenceDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence) &&
            descriptor.ImplementationType == typeof(GroundworkV2CheckpointDurabilityEvidence));
        RuntimeCheckpointCommitStoreBackend.Register(services, new(
            RuntimeCheckpointCommitStoreBackend.Groundwork,
            groundworkCheckpointContractDescriptor,
            groundworkCheckpointConcreteDescriptor,
            groundworkCheckpointEvidenceDescriptor,
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveCheckpointCommit(collection, target)));
        ReplaceScoped<GroundworkV2RuntimePostCommitOutboxStore>(services, Standard<GroundworkV2RuntimePostCommitOutboxStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IRuntimePostCommitOutboxStore), typeof(IPostCommitOutboxLookupStore),
            typeof(IRuntimePostCommitOutboxClaimStore), typeof(IRuntimePostCommitOutboxClaimCompletionStore),
            typeof(IWorkflowDispatchRedriveStore));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2OutboxDurabilityEvidence>());
        var groundworkOutboxEvidenceDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence) &&
            descriptor.ImplementationType == typeof(GroundworkV2OutboxDurabilityEvidence));
        services.RemoveAll<RuntimePostCommitOutboxStoreBackend>();
        var groundworkOutboxConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2RuntimePostCommitOutboxStore));
        var groundworkOutboxContractDescriptors = RuntimePostCommitOutboxStoreBackend.CaptureContractRegistrations(services);
        RuntimePostCommitOutboxStoreBackend.Register(services, new(
            RuntimePostCommitOutboxStoreBackend.Groundwork,
            [groundworkOutboxConcreteDescriptor, groundworkOutboxEvidenceDescriptor, .. groundworkOutboxContractDescriptors],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemovePostCommitOutbox(collection, target)));
        ReplaceScoped<GroundworkV2WorkflowSchedulerWorkQueue>(services, Standard<GroundworkV2WorkflowSchedulerWorkQueue>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowSchedulerWorkQueue), typeof(IWorkflowSchedulerWorkClaimInspection));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2SchedulerDurabilityEvidence>());
        var groundworkSchedulerEvidenceDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchDurabilityEvidence) &&
            descriptor.ImplementationType == typeof(GroundworkV2SchedulerDurabilityEvidence));
        var groundworkQueueConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2WorkflowSchedulerWorkQueue));
        var groundworkQueueContractDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerWorkQueue));
        var groundworkQueueInspectionDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerWorkClaimInspection));
        services.RemoveAll<SchedulerWorkQueueStoreBackend>();
        SchedulerWorkQueueStoreBackend.Register(services, new(
            SchedulerWorkQueueStoreBackend.Groundwork,
            [groundworkQueueConcreteDescriptor, groundworkQueueContractDescriptor, groundworkQueueInspectionDescriptor, groundworkSchedulerEvidenceDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveSchedulerWorkQueue(collection, target)));
        ReplaceScoped<GroundworkV2WorkflowSchedulerPoisonStore>(services, Standard<GroundworkV2WorkflowSchedulerPoisonStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowSchedulerPoisonStore));
        ReplaceScoped<GroundworkV2DurableTimerStateStore>(services, Standard<GroundworkV2DurableTimerStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IDurableTimerStore));
        var groundworkTimerConcreteDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(GroundworkV2DurableTimerStateStore));
        var groundworkTimerContractDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(IDurableTimerStore));
        services.RemoveAll<DurableTimerStoreBackend>();
        DurableTimerStoreBackend.Register(services, new(
            DurableTimerStoreBackend.Groundwork,
            [groundworkTimerConcreteDescriptor, groundworkTimerContractDescriptor],
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveDurableTimer(collection, target)));
        ReplaceScoped<GroundworkV2WorkflowTriggerBindingStore>(services, Standard<GroundworkV2WorkflowTriggerBindingStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTriggerBindingStore));
        ReplaceScoped<GroundworkV2RecurringTriggerScheduleStore>(services, Standard<GroundworkV2RecurringTriggerScheduleStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IRecurringTriggerScheduleStore));
        ReplaceScoped<GroundworkV2WorkflowActivationAuthority>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetRequiredService<GroundworkStorageTransactionFactory>(),
                target), typeof(IWorkflowActivationAuthority));

        RegisterArtifactBackend(services, target);
        RegisterActivityExecutionBackend(services, target);
        commitExistingOperationalStateRemoval?.Invoke(services);
        commitExistingAlterationRemoval?.Invoke(services);
        commitExistingTestScopeRemoval?.Invoke(services);
        commitExistingDispatchRemoval?.Invoke(services);
        commitExistingOutboxRemoval?.Invoke(services);
        commitExistingQueueRemoval?.Invoke(services);
        commitExistingTimerRemoval?.Invoke(services);
        return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            registry?.Restore(registrySnapshot!);
            throw;
        }
    }

    private static WorkflowExecutableCacheOptions CopyAndValidate(WorkflowExecutableCacheOptions options)
    {
        var copy = new WorkflowExecutableCacheOptions { Enabled = options.Enabled, Capacity = options.Capacity };
        copy.Validate();
        return copy;
    }

    private static string BindRuntimeTarget(IServiceCollection services, string? targetName)
    {
        var target = GroundworkTargetNames.Normalize(targetName);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkV2RuntimeTarget))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkV2RuntimeTarget>()
            .SingleOrDefault();
        if (existing is not null && !StringComparer.Ordinal.Equals(existing.Name, target))
        {
            throw new InvalidOperationException(
                $"The Groundwork v2 runtime is already bound to '{existing.Name}' and cannot also bind to '{target}'. " +
                "Elsa's unkeyed runtime contracts can use only one physical target.");
        }

        if (existing is null)
            services.AddSingleton(new GroundworkV2RuntimeTarget(target));
        return target;
    }

    private static void RegisterExecutableStore(
        IServiceCollection services,
        WorkflowExecutableCacheOptions options,
        string? targetName)
    {
        services.RemoveAll<GroundworkV2WorkflowExecutableStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        RemoveKeyed<IWorkflowExecutableStore>(services, WorkflowExecutableProviderKey);
        services.RemoveAll<CachingWorkflowExecutableStore>();
        services.RemoveAll<InvalidatingWorkflowExecutableStore>();
        services.RemoveAll<WorkflowExecutableCache>();
        services.RemoveAll<GroundworkV2WorkflowExecutableCacheLoader>();
        services.RemoveAll<WorkflowExecutableCacheOptions>();
        services.AddSingleton(options);
        services.AddScoped(provider => new GroundworkV2WorkflowExecutableStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName,
            provider.GetService<ILogger<GroundworkV2WorkflowExecutableStore>>()));

        if (!options.Enabled)
        {
            services.AddScoped<IWorkflowExecutableStore>(provider =>
                provider.GetRequiredService<GroundworkV2WorkflowExecutableStore>());
            return;
        }

        services.AddKeyedScoped<IWorkflowExecutableStore>(WorkflowExecutableProviderKey, (provider, _) =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableStore>());
        services.AddSingleton<WorkflowExecutableCache>();
        services.AddSingleton<GroundworkV2WorkflowExecutableCacheLoader>();
        services.AddScoped<CachingWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            if (context.AccessPolicy != PersistenceAccessPolicy.Ordinary || context.Scope is null)
                throw new InvalidOperationException("The workflow executable cache adapter requires an ordinary persistence scope.");
            var persistenceScope = context.Scope;
            var loader = provider.GetRequiredService<GroundworkV2WorkflowExecutableCacheLoader>();
            return new(
                provider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey),
                provider.GetRequiredService<WorkflowExecutableCache>(),
                persistenceScope.Value,
                (artifactId, cancellationToken) => loader.LoadAsync(persistenceScope, artifactId, cancellationToken));
        });
        services.AddScoped<InvalidatingWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            return new(
                provider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey),
                provider.GetRequiredService<WorkflowExecutableCache>(),
                context.Scope?.Value);
        });
        services.AddScoped<IWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            return context.AccessPolicy == PersistenceAccessPolicy.Ordinary && context.Scope is not null
                ? provider.GetRequiredService<CachingWorkflowExecutableStore>()
                : provider.GetRequiredService<InvalidatingWorkflowExecutableStore>();
        });
    }

    private static Func<IServiceProvider, TImplementation> Standard<TImplementation>(
        string? targetName,
        Func<IGroundworkStorageSessionSource, IPersistenceAccessContextAccessor, string?, TImplementation> factory)
        where TImplementation : class =>
        provider => factory(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName);

    private static void ReplaceScoped<TImplementation>(
        IServiceCollection services,
        Func<IServiceProvider, TImplementation> factory,
        params Type[] contracts)
        where TImplementation : class
    {
        services.RemoveAll<TImplementation>();
        services.AddScoped(factory);
        foreach (var contract in contracts)
        {
            services.RemoveAll(contract);
            services.AddScoped(contract, provider => provider.GetRequiredService<TImplementation>());
        }
    }

    private static void RemoveKeyed<TContract>(IServiceCollection services, object key)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(TContract) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, key))
                services.RemoveAt(index);
        }
    }

    private static void RemoveGroundworkBookmarkArtifacts(
        IServiceCollection services,
        ServiceDescriptor ownedStoreRegistration)
    {
        var storeRegistrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkV2BookmarkStateStore))
            .ToArray();
        if (storeRegistrations.Length != 1 || !ReferenceEquals(storeRegistrations[0], ownedStoreRegistration))
            throw new InvalidOperationException("The Groundwork bookmark backend no longer exclusively owns its concrete implementation registration.");

        services.Remove(ownedStoreRegistration);
    }

    private static void ReplaceExistingArtifactBackend(IServiceCollection services)
    {
        var existing = RuntimeArtifactStoreBackend.Find(services);
        if (existing is not null)
        {
            existing.EnsureOwnsRegisteredContracts(services);
            existing.RemoveOwnedArtifacts(services);
            return;
        }

        RuntimeArtifactStoreBackend.EnsureNoUnownedArtifactRegistrations(services);
    }

    private static void RegisterArtifactBackend(IServiceCollection services, string target)
    {
        var infrastructureTypes = new[]
        {
            typeof(WorkflowExecutableCacheOptions),
            typeof(WorkflowExecutableCache),
            typeof(GroundworkV2WorkflowExecutableCacheLoader),
            typeof(CachingWorkflowExecutableStore),
            typeof(InvalidatingWorkflowExecutableStore)
        };
        RuntimeArtifactStoreBackend.Register(services, new RuntimeArtifactStoreBackend(
            RuntimeArtifactStoreBackend.Groundwork,
            RuntimeArtifactStoreBackend.CaptureArtifactSurfaceRegistrations(services)
                .Concat(services.Where(descriptor => infrastructureTypes.Contains(descriptor.ServiceType)))
                .ToArray(),
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveArtifacts(collection, target)));
    }

    private static void RegisterActivityExecutionBackend(IServiceCollection services, string target)
    {
        var concreteTypes = new[]
        {
            typeof(GroundworkV2ActivityExecutionStateStore),
            typeof(GroundworkV2ActivityExecutionInspectionStore),
            typeof(GroundworkV2ActivityExecutionHierarchyStore)
        };
        RuntimeActivityExecutionStoreBackend.Register(services, new RuntimeActivityExecutionStoreBackend(
            RuntimeActivityExecutionStoreBackend.Groundwork,
            RuntimeActivityExecutionStoreBackend.CaptureSurfaceRegistrations(services)
                .Concat(services.Where(descriptor => concreteTypes.Contains(descriptor.ServiceType)))
                .ToArray(),
            collection => GroundworkV2RuntimeUnitWithdrawal.RemoveActivityExecutions(collection, target)));
    }
}

internal static class GroundworkV2RuntimeUnitWithdrawal
{
    private static readonly string[] ArtifactUnitIds =
    [
        ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
        ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];

    public static void RemoveBookmarkState(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.BookmarkStateDocumentKind, targetName);

    public static void RemoveWorkflowExecutionState(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, targetName);

    public static void RemoveArtifacts(IServiceCollection services, string? targetName)
    {
        foreach (var unitId in ArtifactUnitIds)
            services.RemoveGroundworkStorageUnit(unitId, targetName);
    }

    public static void RemoveActivityExecutions(IServiceCollection services, string? targetName)
    {
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.ActivityExecutionInspectionDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind, targetName);
    }

    public static void RemoveWorkflowAlterations(IServiceCollection services, string? targetName)
    {
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind, targetName);
    }

    public static void RemoveWorkflowTestScope(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowTestScopeDocumentKind, targetName);

    public static void RemoveWorkflowDispatch(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind, targetName);

    public static void RemovePostCommitOutbox(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind, targetName);

    public static void RemoveCheckpointCommit(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.CheckpointCommitDocumentKind, targetName);

    public static void RemoveSchedulerWorkQueue(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, targetName);

    public static void RemoveDurableTimer(IServiceCollection services, string? targetName) =>
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind, targetName);

    public static void RemoveOperationalState(IServiceCollection services, string? targetName)
    {
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind, targetName);
        services.RemoveGroundworkStorageUnit(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, targetName);
    }
}

internal sealed class GroundworkV2WorkflowExecutableCacheLoader(IPersistenceOperationScopeFactory operationScopeFactory)
{
    public async ValueTask<WorkflowExecutable?> LoadAsync(
        PersistenceScope persistenceScope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        await using var operationScope = await operationScopeFactory.CreateAsync(
            persistenceScope,
            cancellationToken);
        var store = operationScope.ServiceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkV2RuntimeRegistration.WorkflowExecutableProviderKey);
        return await store.FindAsync(artifactId, cancellationToken);
    }
}

internal sealed record GroundworkV2RuntimeTarget(string? Name);

internal sealed class GroundworkV2CheckpointDurabilityEvidence(
    IGroundworkStorageSessionSource sessions,
    GroundworkV2RuntimeTarget target) : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Checkpoint;

    public WorkflowDispatchDurabilityLevel Level =>
        sessions is IGroundworkStorageCapabilitySource capabilitySource &&
        capabilitySource.Capabilities(target.Name).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit))
            ? WorkflowDispatchDurabilityLevel.Durable
            : WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class GroundworkV2DispatchStoreDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DispatchStore;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkV2OutboxDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Outbox;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkV2SchedulerDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Scheduler;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}
