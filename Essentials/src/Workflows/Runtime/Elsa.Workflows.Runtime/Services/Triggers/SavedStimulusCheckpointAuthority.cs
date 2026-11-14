using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;

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
        var envelope = SavedStimulusStartSeal.Read(source, state.WorkflowExecutionId);
        var start = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        if (!origin.IsEquivalentTo(start.StimulusDeliveryOrigin) ||
            state.HistoryCaptureEnabledAtAdmission != start.HistoryCaptureEnabledAtAdmission ||
            !(start.HistoryCapturePolicy?.IsEquivalentTo(state.HistoryCapturePolicy) ?? state.HistoryCapturePolicy is null) ||
            !NativeIngressTargetEvidence.Equal(state.Authority, start.Authority) || state.CorrelationId != start.CorrelationId ||
            state.DispatchNestingDepth != start.DispatchNestingDepth || state.CreatedAt != envelope.EnqueuedAt || state.StartedAt != envelope.EnqueuedAt ||
            state.PinnedSource != start.PinnedSource || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, start.PinnedExecutable))
            throw new InvalidDataException("The saved start changed its physically accepted command authority or execution context.");
        var rootIntent = commit.PostCommitIntents.SingleOrDefault() ?? throw new InvalidDataException("A saved start requires exactly its root continuation.");
        var root = rootIntent.Payload?.Deserialize<RuntimeSchedulerWorkItem>() ?? throw new InvalidDataException("The saved root continuation is invalid.");
        var original = new RuntimeSchedulerWorkItem(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            envelope.Command.Kind, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, root.RecordedAt,
            envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);
        var expected = NativeIngressStartLineage.Build(original, start, material.RootActivity.ExecutableNodeId);
        var payload = expected.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        var seeds = RuntimeWorkflowStateSeed.BuildSeedChanges(state.WorkflowExecutionId,
            start.Inputs.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal), commit.Checkpoint.OccurredAt,
            start.StimulusInput, start.TriggerNodeId, start.TriggerMetadata, start.CorrelationId);
        if (commit.CommitId != "commit:" + expected.WorkItemId || commit.Checkpoint.CheckpointId != "checkpoint:" + expected.WorkItemId ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId) != expected.WorkItemId ||
            !NativeIngressTargetEvidence.Equal(rootIntent, payload.PostCommitIntents.Single()) ||
            !NativeIngressTargetEvidence.Equal(state.RootVariableFrame, WorkflowCheckpointSchedulerWorkHandler.CreateRootVariableFrame(state.WorkflowExecutionId, start.Variables, material)) ||
            !NativeIngressTargetEvidence.Equal(commit.StateChanges.DurableValues.OrderBy(item => item.StateId, StringComparer.Ordinal).ToArray(),
                seeds.OrderBy(item => item.StateId, StringComparer.Ordinal).ToArray()))
            throw new InvalidDataException("The initial saved checkpoint changed its sealed inputs, variables or exact root continuation lineage.");
    }
}
