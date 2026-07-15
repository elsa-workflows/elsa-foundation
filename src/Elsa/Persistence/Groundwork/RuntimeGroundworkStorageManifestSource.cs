using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork;

/// <summary>Contributes the runtime family's durable Groundwork declaration.</summary>
public sealed class RuntimeGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public const string FeatureName = "elsa-workflows-runtime";
    public const string CheckpointCommitRouteIdentity = "runtime-checkpoint-commit";
    public const string MultiDocumentTransactionsTopologyIdentity = "multi-document-transactions";

    public string FeatureIdentity => FeatureName;

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = LegacyGroundworkStorageManifestPhysicalizer.Physicalize(ElsaRuntimeStorageManifest.Create());

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            manifest,
            [
                typeof(IBookmarkStateStore),
                typeof(IWorkflowExecutableStore),
                typeof(IWorkflowExecutableSourceReferenceStore),
                typeof(IActivityExecutionStateStore),
                typeof(IActivityExecutionInspectionStore),
                typeof(IActivityExecutionInspectionWriter),
                typeof(IWorkflowExecutionStateStore),
                typeof(IDurableValueStateStore),
                typeof(ISchedulerStateStore),
                typeof(IExecutionLivenessStateStore),
                typeof(IWorkflowHoldStateStore),
                typeof(IIncidentStateStore),
                typeof(IRuntimeCheckpointCommitStore),
                typeof(IRuntimePostCommitOutboxStore),
                typeof(IWorkflowSchedulerWorkQueue),
                typeof(IDurableTimerStore),
                typeof(IWorkflowTriggerBindingStore),
                typeof(IRecurringTriggerScheduleStore)
            ],
            [
                CreateCheckpointCommitRouteRequirement()
            ],
            [new GroundworkStorageTopologyRequirement(MultiDocumentTransactionsTopologyIdentity)],
            [
                "runtime-activity-execution-inspection",
                "runtime-activity-execution-state",
                "runtime-bookmark-state",
                "runtime-durable-timer",
                "runtime-durable-value-state",
                "runtime-execution-liveness",
                "runtime-incident-state",
                "runtime-recurring-trigger-schedule",
                "runtime-checkpoint-commit",
                "runtime-diagnostics-settings",
                "runtime-post-commit-outbox",
                "runtime-scheduler-state",
                "runtime-executable-source-reference",
                "runtime-workflow-executable",
                "runtime-workflow-execution-state",
                "runtime-workflow-hold-state",
                "runtime-scheduler-poison",
                "runtime-scheduler-work-queue",
                "runtime-trigger-binding",
                "runtime-publication-projection-state"
            ]));
    }

    public static GroundworkStorageRouteRequirement CreateCheckpointCommitRouteRequirement() =>
        new(
            new StorageUnitIdentity(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind),
            CheckpointCommitRouteIdentity,
            new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit });
}
