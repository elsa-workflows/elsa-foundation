using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Pure checks used inside the actual checkpoint writer's physical transaction or native write gate.</summary>
internal static class NativeIngressCheckpointAuthority
{
    internal static void ValidateWait(RuntimeCheckpointCommit commit, RuntimeCommittedIngressWait committed)
    {
        commit.EnsureExactBookmarkResumeShape();
        RuntimeIngressWaitCommand.ValidateEnvelope(committed, RuntimeIngressWaitCommand.ReadEnvelope(committed.Seal));
        var id = committed.Recipient.Identity;
        var state = commit.StateChanges.ActivityExecutions.SingleOrDefault(item => item.StateId == committed.Recipient.Expected.ActivityExecutionId)?.State;
        if (commit.IngressWaitOrigin != committed.Seal.Origin || commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityAttemptClaimed ||
            !NativeIngressTargetEvidence.Equal(commit.ExpectedBookmarkResume, committed.Recipient.Expected) ||
            commit.WorkflowExecutionId != id.WorkflowExecutionId || !commit.RetainSchedulerWork ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId) != id.EnvelopeId ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CommandId) != id.CommandId ||
            state is null || state.Metadata.GetValueOrDefault(RuntimeIngressWaitCommand.AcceptanceSealMetadataKey) != committed.Seal.Fingerprint ||
            state.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId) != id.EnvelopeId)
            throw new InvalidDataException("The initial wait-consumption checkpoint differs from its immutable physical source seal.");
    }

    internal static void ValidateInitialStart(RuntimeCheckpointCommit commit, RuntimeCommittedRecipient committed, WorkflowExecutable executable)
    {
        var state = commit.StateChanges.WorkflowExecution?.State ?? throw new InvalidDataException("Ingress admission requires initial workflow state.");
        NativeIngressTargetEvidence.ValidateEnvelope(committed, NativeIngressTargetEvidence.ReadEnvelope(committed.Seal));
        NativeIngressTargetEvidence.ValidateState(committed, state);
        NativeIngressTargetEvidence.ValidateExecutable(committed, executable);
        if (commit.Checkpoint.Name != RuntimeCheckpointNames.WorkflowStarted || state.CreatedAt != committed.Item.RecordedAt ||
            state.StartedAt != committed.Item.RecordedAt || state.Status != WorkflowExecutionStatus.Running)
            throw new InvalidDataException("The initial workflow checkpoint differs from its immutable physical source seal.");
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(committed.Seal);
        var start = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        var intentId = $"{envelope.EnvelopeId}:postcommit:schedule:root:{executable.RootActivity.ExecutableNodeId}";
        var intent = commit.PostCommitIntents.SingleOrDefault(item => item.IntentId == intentId)
            ?? throw new InvalidDataException("An initial ingress checkpoint must commit its exact root continuation.");
        var root = intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>() ?? throw new InvalidDataException("The ingress root continuation is invalid.");
        var original = new RuntimeSchedulerWorkItem(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, root.RecordedAt,
            envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);
        var expected = NativeIngressStartLineage.Build(original, start, executable.RootActivity.ExecutableNodeId);
        var payload = expected.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        var seeds = RuntimeWorkflowStateSeed.BuildSeedChanges(state.WorkflowExecutionId,
            start.Inputs.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal), commit.Checkpoint.OccurredAt,
            start.StimulusInput, start.TriggerNodeId, start.TriggerMetadata, start.CorrelationId);
        if (commit.CommitId != "commit:" + expected.WorkItemId || commit.Checkpoint.CheckpointId != "checkpoint:" + expected.WorkItemId ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId) != expected.WorkItemId ||
            !NativeIngressTargetEvidence.Equal(intent, payload.PostCommitIntents.Single()) ||
            !NativeIngressTargetEvidence.Equal(state.RootVariableFrame, WorkflowCheckpointSchedulerWorkHandler.CreateRootVariableFrame(state.WorkflowExecutionId, start.Variables, executable)) ||
            !NativeIngressTargetEvidence.Equal(commit.StateChanges.DurableValues.OrderBy(item => item.StateId, StringComparer.Ordinal).ToArray(),
                seeds.OrderBy(item => item.StateId, StringComparer.Ordinal).ToArray()))
            throw new InvalidDataException("The initial ingress checkpoint does not preserve its sealed input and root continuation lineage.");
    }

    internal static void ValidateContinuation(WorkflowExecutionState proposed, WorkflowExecutionState prior)
    {
        var sameOrigin = prior.IngressOrigin is { } direct
            ? direct.IsEquivalentTo(proposed.IngressOrigin) && proposed.ModelWorkOrigin is null
            : prior.ModelWorkOrigin is { IngressOrigin: not null } work && proposed.IngressOrigin is null && work.IsEquivalentTo(proposed.ModelWorkOrigin);
        if (prior.WorkflowExecutionId != proposed.WorkflowExecutionId || !sameOrigin ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(prior.PinnedExecutable, proposed.PinnedExecutable) ||
            prior.TenantId != proposed.TenantId || prior.Partition != proposed.Partition || prior.RunKind != proposed.RunKind ||
            prior.ParentWorkflowExecutionId != proposed.ParentWorkflowExecutionId || prior.TestScope != proposed.TestScope ||
            prior.CreatedAt != proposed.CreatedAt || prior.StartedAt != proposed.StartedAt || prior.DispatchNestingDepth != proposed.DispatchNestingDepth ||
            !NativeIngressTargetEvidence.Equal(prior.PinnedSource, proposed.PinnedSource) ||
            !NativeIngressTargetEvidence.Equal(prior.Authority, proposed.Authority))
            throw new InvalidDataException("A persisted ingress workflow cannot replace its accepted origin, material, scope or authority.");
    }
}
