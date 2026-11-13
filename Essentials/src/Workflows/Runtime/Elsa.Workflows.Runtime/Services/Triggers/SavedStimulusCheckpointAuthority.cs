using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Scope admission against a provider's actual immutable ordinary cohort, after canonical start dispatch.</summary>
internal static class SavedStimulusCheckpointAuthority
{
    internal static void ValidateInitial(RuntimeCheckpointCommit commit, RuntimePostCommitOutboxItem source, PersistenceScope scope, WorkflowExecutable material)
    {
        var state = commit.StateChanges.WorkflowExecution?.State ?? throw new InvalidDataException("Saved stimulus admission needs workflow state.");
        NativeIngressCheckpointAuthority.ValidateOriginShape(state);
        var origin = state.StimulusDeliveryOrigin ?? throw new InvalidDataException("The saved stimulus scope origin is missing.");
        var cohort = source.RecipientSnapshot ?? throw new InvalidDataException("The actual saved source has no immutable cohort.");
        if (origin.Scope != scope || origin.SourceOutboxItemId != source.OutboxItemId || source.Intent is null ||
            source.SourceKind != RuntimeDeliverySourceKind.WorkflowCheckpoint || source.StimulusDeliveryProtocolVersion != StimulusDeliverySnapshot.CurrentVersion ||
            source.StimulusDeliverySourceScope != scope || origin.IntentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent) ||
            cohort.SourceOutboxItemId != source.OutboxItemId || cohort.IntentFingerprint != origin.IntentFingerprint ||
            cohort.RetentionAcceptanceVersion != StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion || cohort.Scope.PersistenceScope != scope)
            throw new InvalidDataException("The saved start scope differs from its actual retained source and cohort.");
        var recipient = cohort.Starts.SingleOrDefault(item => item.Binding.TriggerBindingId == origin.TriggerBindingId)
            ?? throw new InvalidDataException("The actual saved cohort does not contain this target binding.");
        var identity = source.Intent.IdempotencyKey is { } key ? KeyedWorkflowStartIdentity.For(key, recipient.Executable.ArtifactId) : null;
        if (!NativeIngressCheckpointAuthority.HasBoundScope(state, scope.Value) || identity is null || state.WorkflowExecutionId != identity.WorkflowExecutionId || commit.WorkflowExecutionId != state.WorkflowExecutionId ||
            !recipient.SourceProvenanceCaptured || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(recipient.Executable, state.PinnedExecutable) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.PinnedExecutable) || state.PinnedSource != recipient.SourceProvenance ||
            state.TenantId != cohort.Scope.TenantId || state.Partition != cohort.Scope.Partition || state.Partition?.Value != scope.Value ||
            state.RunKind != WorkflowRunKind.PublishedRun || state.ParentWorkflowExecutionId is not null || state.TestScope is not null ||
            state.IngressOrigin is not null || state.ModelWorkOrigin is not null || commit.Checkpoint.Name != RuntimeCheckpointNames.WorkflowStarted ||
            state.Status != WorkflowExecutionStatus.Running || state.StartedAt != state.CreatedAt)
            throw new InvalidDataException("The initial saved start changed its exact target identity, material or captured scope.");
        var roots = commit.PostCommitIntents.Where(item => item.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork)
            .Select(item => item.Payload?.Deserialize<RuntimeSchedulerWorkItem>()).Where(item => item is not null).ToArray();
        if (!roots.Any(item => item!.WorkflowExecutionId == state.WorkflowExecutionId && item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
            item.Payload?.Deserialize<RuntimeScheduleActivityCommandPayload>() is { } payload &&
            WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, material.Identity) && payload.ExecutableNodeId == material.RootActivity.ExecutableNodeId))
            throw new InvalidDataException("The saved start checkpoint must retain its real root continuation.");
    }
}
