using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Validates a first terminal state against a recorded failure, never against caller metadata alone.</summary>
internal static class InitialStartFaultAuthority
{
    internal const string WorkSnapshotMetadata = "runtime.initialStartFault.work";

    internal static string? CaptureWork(RuntimeSchedulerWorkItem work)
    {
        var start = ReadPayload(work);
        return start?.StartAuthority?.StandaloneTrigger is not null || start?.StartAuthority?.ModelWork is not null || start?.StimulusDeliveryOrigin is not null
            ? JsonSerializer.Serialize(work) : null;
    }

    internal static RuntimeSchedulerWorkItem? ReadWork(RuntimeSchedulerPoisonRecord record)
    {
        if (!record.Metadata.TryGetValue(WorkSnapshotMetadata, out var snapshot)) return null;
        try { return JsonSerializer.Deserialize<RuntimeSchedulerWorkItem>(snapshot) ?? throw Invalid(); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("The parked initial scheduler work is malformed.", exception); }
    }

    internal static WorkflowExecutionStartCommandPayload? ReadPayload(RuntimeSchedulerWorkItem work)
    {
        if (work.Payload is not { } json) return null;
        if (work.CommandKind == WorkflowExecutionCommandKind.Start) return json.Deserialize<WorkflowExecutionStartCommandPayload>();
        if (work.CommandKind != WorkflowExecutionCommandKind.Checkpoint ||
            json.Deserialize<RuntimeCheckpointCommandPayload>() is not { CheckpointName: RuntimeCheckpointNames.WorkflowStarted } checkpoint) return null;
        return new(checkpoint.PinnedExecutable, checkpoint.PinnedExecutable.ArtifactId, checkpoint.SeedVariables, checkpoint.SeedInputs,
            checkpoint.SeedStimulusInput, checkpoint.SeedTriggerNodeId, checkpoint.RunKind, checkpoint.PinnedSource,
            checkpoint.ParentWorkflowExecutionId, checkpoint.CorrelationId, checkpoint.TenantId, checkpoint.Partition, checkpoint.Authority,
            checkpoint.ModelWorkOrigin is { } model ? WorkflowExecutableStartAuthority.FromModelWork(model) :
                checkpoint.IngressOrigin is { } ingress ? WorkflowExecutableStartAuthority.FromStandaloneTrigger(ingress) : null,
            checkpoint.DispatchNestingDepth, checkpoint.TestScope, checkpoint.SeedTriggerMetadata)
            { StimulusDeliveryOrigin = checkpoint.StimulusDeliveryOrigin };
    }

    // Projection only. The physical writer validates the retained failed work against its actual source and poison.
    internal static WorkflowExecutionCommandEnvelope ProjectStart(RuntimeSchedulerWorkItem work) =>
        new(work.EnvelopeId, work.WorkflowExecutionId,
            new(work.CommandId, work.WorkflowExecutionId, WorkflowExecutionCommandKind.Start, work.EnqueuedAt,
                JsonSerializer.SerializeToElement(ReadPayload(work) ?? throw Invalid()), work.CommandMetadata),
            work.IdempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, work.EnqueuedAt, work.Sequence, work.EnvelopeMetadata);

    internal static bool IsProtected(WorkflowExecutionState state) =>
        state.IngressOrigin is not null || state.StimulusDeliveryOrigin is not null || state.ModelWorkOrigin is not null;

    internal static void Validate(RuntimeCheckpointCommit commit, RuntimeSchedulerPoisonRecord actual)
    {
        var condition = commit.ExpectedInitialStartFault ?? throw Invalid();
        var work = condition.Work;
        if (!NativeIngressTargetEvidence.Equal(ReadWork(actual), work)) throw Invalid();
        if (commit.StateChanges.WorkflowExecution is not { Operation: RuntimeStateChangeOperation.Upsert, State: { } state }) throw Invalid();
        if (!IsProtected(state) || !NativeIngressTargetEvidence.Equal(actual, condition.Poison) ||
            work.Payload is null || work.WorkflowExecutionId != commit.WorkflowExecutionId || work.WorkItemId != actual.WorkItemId ||
            work.CommandKind != actual.CommandKind ||
            actual.WorkflowExecutionId != commit.WorkflowExecutionId || actual.Disposition != RuntimeSchedulerPoisonDisposition.Poisoned ||
            actual.LastFailedAt > commit.Checkpoint.OccurredAt || commit.ExpectedFence is null ||
            commit.Checkpoint.Name != RuntimeCheckpointNames.WorkflowFaulted || state.Status != WorkflowExecutionStatus.Faulted ||
            state.CreatedAt != work.EnqueuedAt || state.StartedAt != work.EnqueuedAt ||
            state.UpdatedAt != commit.Checkpoint.OccurredAt || state.CompletedAt != commit.Checkpoint.OccurredAt ||
            state.RootVariableFrame is not null || state.ParentWorkflowExecutionId is not null || state.TestScope is not null ||
            state.ModelWorkOrigin is not null && commit.ExpectedModelExecution is null)
            throw Invalid();
        if (actual.CommandKind == WorkflowExecutionCommandKind.Checkpoint
            ? actual.HandlerName != WorkflowCheckpointSchedulerWorkHandler.HandlerName
            : actual.CommandKind != WorkflowExecutionCommandKind.Start || actual.HandlerName != WorkflowStartSchedulerWorkHandler.HandlerName)
            throw Invalid();
        var fingerprint = RuntimeChainId.Fingerprint(actual.WorkItemId);
        var incidentId = PoisonedSchedulerWorkIncidentObserver.IncidentId(actual.WorkItemId);
        if (commit.StateChanges.Incidents.Count != 1) throw Invalid();
        var incidentChange = commit.StateChanges.Incidents.Single();
        var incident = incidentChange.State;
        if (incidentChange.Operation != RuntimeStateChangeOperation.Upsert || incident.Severity != IncidentSeverity.Critical) throw Invalid();
        if (commit.CommitId != $"commit:{state.WorkflowExecutionId}:scheduler-poison:{fingerprint}" ||
            commit.Checkpoint.CheckpointId != $"checkpoint:{state.WorkflowExecutionId}:scheduler-poison:{fingerprint}" ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId) != actual.WorkItemId ||
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointReason) != PoisonedSchedulerWorkIncidentObserver.IncidentFailureType ||
            incident.IncidentId != incidentId || incident.WorkflowExecutionId != state.WorkflowExecutionId ||
            incident.Status != IncidentStatus.Blocking || incident.FailureType != PoisonedSchedulerWorkIncidentObserver.IncidentFailureType ||
            incident.ResolutionOutcome?.ActionKind != IncidentResolutionActionKinds.FaultWorkflow ||
            incident.ResolutionOutcome.SystemSource != IncidentResolutionSystemSources.PoisonedSchedulerWork ||
            incident.CreatedAt != commit.Checkpoint.OccurredAt || incident.ResolvedAt is not null ||
            commit.StateChanges.Scheduler is not null || commit.StateChanges.Operational.Count != 0 ||
            commit.StateChanges.ActivityExecutionInspections.Count != 0 || commit.StateChanges.ActivityScopeCleanups.Count != 0 ||
            commit.StateChanges.WorkflowDispatches.Count != 0 || commit.StateChanges.WorkflowDispatchCancellations.Count != 0 ||
            commit.StateChanges.ConsumedSchedulerWorkItems.Count != 0 || commit.StateChanges.AlterationJobTerminalChange is not null ||
            !commit.StateChanges.BackgroundActivityWork.IsEmpty || commit.ModelState is not null ||
            commit.ExpectedModelParentWait is not null || commit.ExpectedBookmarkResume is not null || commit.IngressWaitOrigin is not null ||
            commit.RetainSchedulerWork || commit.HistoryBatches is not null || commit.Checkpoint.ActivityExecutionIds.Count != 0 ||
            commit.StateChanges.DurableValues.Count != 0 || commit.StateChanges.ActivityExecutions.Count != 0 ||
            commit.StateChanges.Bookmarks.Count != 0)
            throw Invalid();
        // Only the model participant may contribute its terminal result; no root continuation or unrelated effect
        // may hitchhike on a first-fault write, even when supplied as an already-materialized outbox row.
        var intents = commit.PostCommitIntents;
        if (state.ModelWorkOrigin is { } model)
        {
            if (intents.Count != 1 || intents[0].Kind != model.ResultIntentKind ||
                intents[0].WorkflowExecutionId != state.WorkflowExecutionId || intents[0].RecordedAt != commit.Checkpoint.OccurredAt ||
                intents[0].NotBefore is not null || intents[0].DependsOnWaitRegistrationId is not null)
                throw Invalid();
        }
        else if (intents.Count != 0) throw Invalid();
        if (commit.StateChanges.PostCommitOutbox.Count != intents.Count ||
            commit.StateChanges.PostCommitOutbox.Any(change => change.Operation != RuntimeStateChangeOperation.Upsert ||
                !intents.Any(intent => change.State.Intent.IsEquivalentTo(intent) &&
                    change.State.OutboxItemId == commit.CommitId + ":" + intent.IntentId &&
                    change.State.Status == RuntimePostCommitOutboxStatus.Pending && change.State.StimulusDeliverySourceScope == ModelScope(state))))
            throw Invalid();
        WorkflowExecutionStartCommandPayload start;
        try { start = ReadPayload(work) ?? throw Invalid(); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new InvalidDataException("The first fault has a malformed accepted start.", exception); }
        if (state.ModelWorkOrigin is { } workOrigin &&
            (start.PinnedSource is not null || start.Variables.Count != 0 || start.StimulusInput is not null ||
             start.TriggerNodeId is not null || start.TriggerMetadata.Count != 0 || start.CorrelationId is not null ||
             start.RunKind != WorkflowRunKind.PublishedRun || start.DispatchNestingDepth != 0 ||
             start.RequestedArtifactId != workOrigin.WorkerExecutable.ArtifactId ||
             start.Inputs.Count != (workOrigin.Inputs?.Count ?? 0) ||
             workOrigin.Inputs is { } inputs && inputs.Any(item => !start.Inputs.TryGetValue(item.Key, out var value) || !JsonElement.DeepEquals(item.Value, value))))
            throw Invalid();
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, start.PinnedExecutable) ||
            !NativeIngressTargetEvidence.Equal(state.PinnedSource, start.PinnedSource) ||
            !NativeIngressTargetEvidence.Equal(state.Authority, start.Authority) || state.TenantId != start.TenantId ||
            state.Partition != start.Partition || state.RunKind != start.RunKind || state.CorrelationId != start.CorrelationId ||
            state.DispatchNestingDepth != start.DispatchNestingDepth ||
            !NativeIngressTargetEvidence.Equal(state.IngressOrigin, start.StartAuthority?.StandaloneTrigger) ||
            !NativeIngressTargetEvidence.Equal(state.ModelWorkOrigin, start.StartAuthority?.ModelWork) ||
            !NativeIngressTargetEvidence.Equal(state.StimulusDeliveryOrigin, start.StimulusDeliveryOrigin))
            throw Invalid();
    }

    internal static void RequireSameStart(RuntimeCheckpointCommit commit, WorkflowExecutionCommandEnvelope actual, WorkflowExecutable material)
    {
        var work = commit.ExpectedInitialStartFault?.Work ?? throw Invalid();
        var start = new RuntimeSchedulerWorkItem(actual.EnvelopeId, actual.WorkflowExecutionId, actual.Command.CommandId,
            actual.Command.Kind, actual.EnvelopeId, actual.IdempotencyKey, actual.EnqueuedAt, work.RecordedAt,
            actual.Sequence, actual.Command.Payload, actual.Command.Metadata, actual.Metadata);
        var expected = work.CommandKind == WorkflowExecutionCommandKind.Start ? start :
            NativeIngressStartLineage.Build(start, actual.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!, material.RootActivity.ExecutableNodeId);
        if (!NativeIngressTargetEvidence.Equal(work, expected)) throw Invalid();
    }

    private static PersistenceScope? ModelScope(WorkflowExecutionState state) => state.ModelWorkOrigin?.Scope;

    private static InvalidDataException Invalid() => new("The first fault does not match its physical accepted start and parked scheduler failure.");
}
