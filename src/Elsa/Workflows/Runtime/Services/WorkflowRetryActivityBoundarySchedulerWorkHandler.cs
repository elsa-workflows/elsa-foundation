using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Creates a fresh activity execution attempt from one failed boundary's immutable input snapshot.</summary>
public sealed class WorkflowRetryActivityBoundarySchedulerWorkHandler(
    IActivityExecutionStateStore activityExecutionStateStore,
    IDurableValueStateStore durableValueStateStore,
    IWorkflowExecutableStore workflowExecutableStore,
    RuntimeCheckpointCommitter checkpointCommitter) : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowRetryActivityBoundarySchedulerWorkHandler);
    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandKind == WorkflowExecutionCommandKind.RetryActivityBoundary;

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var commit = await BuildCommitAsync(workItem, cancellationToken);
        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    public async ValueTask HandleAsync(
        RuntimeSchedulerWorkItem workItem,
        IRuntimePipelineContext pipelineContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        pipelineContext.Workspace.StageCheckpointCommit(await BuildCommitAsync(workItem, cancellationToken));
    }

    private async ValueTask<RuntimeCheckpointCommit> BuildCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();
        var command = Deserialize(workItem);
        var prior = await activityExecutionStateStore.FindAsync(
            workItem.WorkflowExecutionId, command.ActivityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Boundary retry references missing activity execution '{command.ActivityExecutionId}'.");
        if (prior.Status != ActivityExecutionStatus.Faulted)
            throw new InvalidOperationException($"Activity execution '{command.ActivityExecutionId}' is '{prior.Status}' and cannot be retried as a failed boundary.");
        if (!StringComparer.Ordinal.Equals(prior.ExecutionScopeId, command.ActivityExecutionId))
            throw new InvalidOperationException($"Activity execution '{command.ActivityExecutionId}' is not an activity execution scope boundary.");
        ValidatePinnedExecutable(prior, command.PinnedExecutable);

        var executable = await workflowExecutableStore.FindAsync(command.PinnedExecutable.ArtifactId, cancellationToken)
                         ?? throw new WorkflowExecutableNotFoundException(command.PinnedExecutable.ArtifactId);
        if (!SameIdentity(executable.Identity, command.PinnedExecutable))
            throw new InvalidOperationException("Boundary retry pinned executable does not match the retained workflow artifact.");
        if (!executable.NodesById.ContainsKey(prior.Execution.ExecutableNodeId))
            throw new InvalidOperationException($"Boundary retry executable node '{prior.Execution.ExecutableNodeId}' is absent from the pinned workflow artifact.");

        var previousAttempt = prior.Attempt ?? prior.Provenance.Attempt
            ?? new ActivityExecutionAttemptLineage(1, prior.Execution.ActivityExecutionId, null);
        var newActivityExecutionId = command.RetryActivityExecutionId;
        var attempt = new ActivityExecutionAttemptLineage(
            checked(previousAttempt.AttemptNumber + 1),
            previousAttempt.FirstAttemptActivityExecutionId,
            prior.Execution.ActivityExecutionId);
        var existingRetry = await activityExecutionStateStore.FindAsync(
            workItem.WorkflowExecutionId, newActivityExecutionId, cancellationToken);
        if (existingRetry is not null)
            ValidateExistingRetry(existingRetry, prior, attempt, command.PinnedExecutable);

        var allValues = await durableValueStateStore.ListAllDurableValueStatesAsync(workItem.WorkflowExecutionId, cancellationToken);
        var originalInputs = allValues
            .Where(value => StringComparer.Ordinal.Equals(value.SourceActivityExecutionId, prior.Execution.ActivityExecutionId))
            .Where(value => value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) && StringComparer.Ordinal.Equals(role, "input"))
            .OrderBy(value => value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.BoundaryReferenceKey), StringComparer.Ordinal)
            .ToArray();
        var clonedInputs = originalInputs.Select(value => CloneInput(value, newActivityExecutionId, prior.Execution.ActivityExecutionId)).ToArray();
        // A scheduler work item may be delivered more than once. Anchor the derived checkpoint and
        // post-commit work to its immutable recorded time so the same commit ID has the same fingerprint.
        var occurredAt = workItem.RecordedAt;
        var provenanceMetadata = prior.Provenance.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        provenanceMetadata[RuntimeMetadataKeys.RetrySourceActivityExecutionId] = prior.Execution.ActivityExecutionId;
        provenanceMetadata[RuntimeMetadataKeys.RetryReason] = command.Reason;
        var provenance = ActivitySchedulingProvenance.From(
            workItem.WorkflowExecutionId,
            prior.ParentActivityExecutionId,
            prior.SchedulingActivityExecutionId,
            prior.BranchId,
            prior.IterationId,
            prior.Provenance.ExecutionPathId,
            newActivityExecutionId,
            "activity-boundary-retry",
            provenanceMetadata,
            attempt);
        var schedulePayload = new RuntimeScheduleActivityCommandPayload(
            command.PinnedExecutable,
            prior.Execution.ExecutableNodeId,
            newActivityExecutionId,
            "ActivityBoundaryRetry",
            prior.SchedulingActivityExecutionId,
            prior.ParentActivityExecutionId,
            provenance);
        var scheduleWorkItem = new RuntimeSchedulerWorkItem(
            $"{workItem.WorkItemId}:schedule-retry:{newActivityExecutionId}",
            workItem.WorkflowExecutionId,
            $"{workItem.CommandId}:schedule-retry:{newActivityExecutionId}",
            WorkflowExecutionCommandKind.ScheduleActivity,
            workItem.EnvelopeId,
            $"{workItem.IdempotencyKey}:schedule-retry:{newActivityExecutionId}",
            occurredAt,
            occurredAt,
            workItem.Sequence is { } sequence ? sequence + 1 : null,
            JsonSerializer.SerializeToElement(schedulePayload),
            provenanceMetadata,
            workItem.EnvelopeMetadata,
            newActivityExecutionId,
            attempt);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = "ActivityBoundaryRetryPrepared",
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = newActivityExecutionId,
            [RuntimeMetadataKeys.RetrySourceActivityExecutionId] = prior.Execution.ActivityExecutionId,
            [RuntimeMetadataKeys.RetryReason] = command.Reason,
            [RuntimeMetadataKeys.PinnedArtifactId] = command.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.PinnedArtifactVersion] = command.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.PinnedArtifactHash] = command.PinnedExecutable.ArtifactHash
        };
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-boundary-retry:{newActivityExecutionId}";
        return new RuntimeCheckpointCommit(
            $"commit:{workItem.WorkItemId}:activity-boundary-retry:{newActivityExecutionId}",
            new RuntimeCheckpoint(
                checkpointId,
                "ActivityBoundaryRetryPrepared",
                workItem.WorkflowExecutionId,
                occurredAt,
                [prior.Execution.ActivityExecutionId, newActivityExecutionId],
                metadata),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: clonedInputs.Select(value => new RuntimeStateChange<DurableValueState>(
                    value.DurableValueId, RuntimeStateChangeOperation.Upsert, value, metadata)).ToArray(),
                incidents: [],
                operational: []),
            [SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(workItem, newActivityExecutionId, scheduleWorkItem, occurredAt)],
            metadata);
    }

    private static DurableValueState CloneInput(
        DurableValueState source,
        string newActivityExecutionId,
        string previousActivityExecutionId)
    {
        if (!source.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryReferenceKey, out var referenceKey) || string.IsNullOrWhiteSpace(referenceKey))
            throw new InvalidOperationException($"Boundary input '{source.DurableValueId}' has no stable reference key.");
        if (!source.Metadata.ContainsKey(RuntimeMetadataKeys.BoundaryInputName))
            throw new InvalidOperationException($"Boundary input '{source.DurableValueId}' has no runtime input name.");

        var durableValueId = ActivityExecutionScopeKeys.Input(newActivityExecutionId, referenceKey);
        var metadata = source.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.BoundaryExecutionScopeId] = newActivityExecutionId;
        metadata[RuntimeMetadataKeys.RetrySourceActivityExecutionId] = previousActivityExecutionId;
        return new DurableValueState(
            durableValueId,
            source.WorkflowExecutionId,
            durableValueId,
            source.Type,
            source.Lifecycle,
            source.Storage,
            source.InlineValue?.Clone(),
            source.ExternalReference,
            newActivityExecutionId,
            source.CapturedAt,
            metadata);
    }

    private static void ValidatePinnedExecutable(ActivityExecutionState state, WorkflowExecutableIdentity pinned)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.PinnedArtifactId] = pinned.ArtifactId,
            [RuntimeMetadataKeys.PinnedArtifactVersion] = pinned.ArtifactVersion,
            [RuntimeMetadataKeys.PinnedArtifactHash] = pinned.ArtifactHash
        };
        foreach (var item in expected)
        {
            if (!state.Metadata.TryGetValue(item.Key, out var actual) || !StringComparer.Ordinal.Equals(actual, item.Value))
                throw new InvalidOperationException($"Boundary retry exact artifact mismatch for '{item.Key}'.");
        }
    }

    private static void ValidateExistingRetry(
        ActivityExecutionState existing,
        ActivityExecutionState prior,
        ActivityExecutionAttemptLineage expectedAttempt,
        WorkflowExecutableIdentity pinned)
    {
        var actualAttempt = existing.Attempt ?? existing.Provenance.Attempt;
        var isSameAttempt =
            StringComparer.Ordinal.Equals(existing.Execution.ExecutableNodeId, prior.Execution.ExecutableNodeId) &&
            StringComparer.Ordinal.Equals(existing.ParentActivityExecutionId, prior.ParentActivityExecutionId) &&
            StringComparer.Ordinal.Equals(existing.ExecutionScopeId, existing.Execution.ActivityExecutionId) &&
            actualAttempt is not null &&
            actualAttempt.AttemptNumber == expectedAttempt.AttemptNumber &&
            StringComparer.Ordinal.Equals(actualAttempt.FirstAttemptActivityExecutionId, expectedAttempt.FirstAttemptActivityExecutionId) &&
            StringComparer.Ordinal.Equals(actualAttempt.PreviousAttemptActivityExecutionId, expectedAttempt.PreviousAttemptActivityExecutionId);
        if (!isSameAttempt)
            throw new InvalidOperationException($"Boundary retry activity execution ID '{existing.Execution.ActivityExecutionId}' is already used by a different execution attempt.");

        ValidatePinnedExecutable(existing, pinned);
    }

    private static bool SameIdentity(WorkflowExecutableIdentity left, WorkflowExecutableIdentity right) =>
        StringComparer.Ordinal.Equals(left.ArtifactId, right.ArtifactId) &&
        StringComparer.Ordinal.Equals(left.DefinitionId, right.DefinitionId) &&
        StringComparer.Ordinal.Equals(left.DefinitionVersionId, right.DefinitionVersionId) &&
        StringComparer.Ordinal.Equals(left.ArtifactVersion, right.ArtifactVersion) &&
        StringComparer.Ordinal.Equals(left.ArtifactHash, right.ArtifactHash);

    private static RetryActivityBoundaryCommand Deserialize(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("RetryActivityBoundary scheduler work requires a payload.");
        try
        {
            return payload.Deserialize<RetryActivityBoundaryCommand>()
                   ?? throw new InvalidOperationException("RetryActivityBoundary scheduler work payload resolved to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("RetryActivityBoundary scheduler work payload is invalid.", exception);
        }
    }
}
