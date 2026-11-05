using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Records replay-stable parent resume responsibility in a waited child's terminal checkpoint.</summary>
public sealed class WorkflowDispatchCompletionEnricher : IRuntimeCheckpointCommitEnricher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IWorkflowOutputSource _workflowOutputSource;
    private readonly IPostCommitOutboxLookupStore _outboxLookupStore;
    private readonly IIncidentStateStore? _incidentStateStore;
    private readonly IRuntimeWorkflowControlBoundary? _controlBoundary;
    private readonly IServiceProvider? _historyServices;

    public WorkflowDispatchCompletionEnricher(
        IWorkflowOutputSource workflowOutputSource,
        IPostCommitOutboxLookupStore outboxLookupStore)
        : this(workflowOutputSource, outboxLookupStore, incidentStateStore: null)
    {
    }

    public WorkflowDispatchCompletionEnricher(
        IWorkflowOutputSource workflowOutputSource,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IIncidentStateStore? incidentStateStore)
        : this(workflowOutputSource, outboxLookupStore, incidentStateStore, controlBoundary: null)
    {
    }

    public WorkflowDispatchCompletionEnricher(
        IWorkflowOutputSource workflowOutputSource,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IIncidentStateStore? incidentStateStore,
        IRuntimeWorkflowControlBoundary? controlBoundary)
        : this(workflowOutputSource, outboxLookupStore, incidentStateStore, controlBoundary, null)
    {
    }

    public WorkflowDispatchCompletionEnricher(
        IWorkflowOutputSource workflowOutputSource,
        IPostCommitOutboxLookupStore outboxLookupStore,
        IIncidentStateStore? incidentStateStore,
        IRuntimeWorkflowControlBoundary? controlBoundary,
        IServiceProvider? historyServices)
    {
        ArgumentNullException.ThrowIfNull(workflowOutputSource);
        ArgumentNullException.ThrowIfNull(outboxLookupStore);
        _workflowOutputSource = workflowOutputSource;
        _outboxLookupStore = outboxLookupStore;
        _incidentStateStore = incidentStateStore;
        _controlBoundary = controlBoundary;
        _historyServices = historyServices;
    }

    public int Order => 100;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        if (commit.StateChanges.WorkflowExecution?.State is not { } child ||
            !TryGetDispatchStatus(child.Status, out var terminalStatus))
            return commit;

        var terminalDispatches = commit.StateChanges.WorkflowDispatches
            .Where(change => change.Operation != RuntimeStateChangeOperation.Delete)
            .Select(change => change.State)
            .Where(record => record.Status == terminalStatus &&
                             StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, child.WorkflowExecutionId))
            .OrderBy(record => record.DispatchId, StringComparer.Ordinal)
            .ToArray();
        if (terminalDispatches.Length == 0)
            return commit;

        var dispatches = terminalDispatches.Where(record => record.Mode == WorkflowDispatchMode.WaitForCompletion).ToArray();

        var intents = commit.PostCommitIntents.ToList();
        IReadOnlyCollection<RuntimeWorkflowOutput>? safeOutputs = null;
        IReadOnlyCollection<string>? incidentIds = null;
        foreach (var dispatch in dispatches)
        {
            var controlEnabled = WorkflowDispatchControlPolicy.ReadCapability(dispatch.Metadata) is not null;
            try
            {
                var identity = new WorkflowDispatchIdentity(
                    dispatch.ParentWorkflowExecutionId,
                    dispatch.ParentActivityExecutionId);
                var control = await CreateControlAsync(commit, dispatch, identity, cancellationToken);
                var outboxItemId = identity.ParentResumeOutboxItemId(commit.CommitId);
                var committed = await _outboxLookupStore.FindAsync(outboxItemId, cancellationToken);
                var existing = intents.SingleOrDefault(intent => StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId));
                if (committed is not null && existing is not null && !committed.Intent.IsEquivalentTo(existing))
                    throw TerminalConflict(controlEnabled, $"Parent-resume intent '{identity.ParentResumeIntentId}' conflicts with its committed outbox item.");

                RuntimePostCommitIntent intent;
                if (committed is not null)
                {
                    intent = committed.Intent;
                    if (existing is null)
                        intents.Add(intent);
                }
                else
                {
                    if (terminalStatus == WorkflowDispatchStatus.Completed)
                    {
                        safeOutputs ??= await _workflowOutputSource.ReadAsync(
                            child.WorkflowExecutionId,
                            commit.StateChanges.DurableValues,
                            cancellationToken);
                    }
                    else if (terminalStatus == WorkflowDispatchStatus.Faulted)
                    {
                        incidentIds ??= await ReadIncidentIdsAsync(
                            child.WorkflowExecutionId,
                            commit.StateChanges.Incidents,
                            cancellationToken);
                    }

                    var canonical = CreateIntent(
                        dispatch,
                        identity,
                        safeOutputs ?? [],
                        incidentIds ?? [],
                        commit.Checkpoint.OccurredAt,
                        control);
                    if (existing?.HistoryCausation is { } retainedCause)
                    {
                        if (retainedCause != ResumeCause(commit, dispatch, canonical))
                            throw TerminalConflict(controlEnabled, "Parent-resume source causation differs from its actual child terminal checkpoint.");
                        canonical = CopyCause(canonical, retainedCause);
                    }
                    if (existing is not null && !canonical.IsEquivalentTo(existing))
                        throw TerminalConflict(controlEnabled, $"Parent-resume intent '{identity.ParentResumeIntentId}' conflicts with the policy-safe terminal result.");

                    intent = existing ?? canonical;
                    if (existing is null)
                        intents.Add(intent);
                }

                var payload = ValidateIntent(intent, dispatch, identity, commit.Checkpoint.OccurredAt);
                if (intent.HistoryCausation is { } existingCause && existingCause != ResumeCause(commit, dispatch, intent))
                    throw TerminalConflict(controlEnabled, "Parent-resume source causation differs from its actual child terminal checkpoint.");
                if (!WorkflowControlTransport.Equivalent(payload.Result.Control, control))
                    throw WorkflowDispatchControlPolicy.Failure("terminal resume differs from its whole staged root control");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (controlEnabled)
            {
                throw WorkflowDispatchControlPolicy.CheckpointFailure(dispatch.ChildWorkflowExecutionId, null, exception);
            }
        }

        var enriched = commit with { PostCommitIntents = intents };
        if (_historyServices is null || commit.Checkpoint.Name != TerminalCheckpointName(child.Status) ||
            _historyServices.GetService<IRuntimeHistorySourceContributor>() is not { } contributor) return enriched;
        RuntimeHistoryCapturePolicy? pin = null;
        var captured = await contributor.AddFactsAsync(enriched, policy =>
        {
            pin = policy;
            if (!policy.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowSettled), StringComparer.Ordinal)) return [];
            return terminalDispatches.Select(dispatch => new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ChildWorkflowSettled,
                dispatch.DispatchId, commit.Checkpoint.OccurredAt,
                activityExecutionId: commit.StateChanges.ActivityExecutions.Where(change => change.Operation != RuntimeStateChangeOperation.Delete &&
                    change.State.Execution.WorkflowExecutionId == child.WorkflowExecutionId && change.State.ParentActivityExecutionId is null)
                    .Select(change => change.State.InvocationId).SingleOrDefault(),
                causation: dispatch.Mode == WorkflowDispatchMode.WaitForCompletion
                    ? new(WorkflowExecutionId: commit.WorkflowExecutionId, DeliveryId: RuntimePostCommitOutboxIdentity.CreateLogicalValue(
                        commit.CommitId, new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).ParentResumeIntentId))
                    : null,
                relatedWorkflowExecutionId: dispatch.ParentWorkflowExecutionId, relatedActivityExecutionId: dispatch.ParentActivityExecutionId,
                reasonCode: terminalStatus.ToString()));
        }, cancellationToken);
        if (pin is null || !pin.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowSettled), StringComparer.Ordinal) ||
            !pin.SupportedKinds.Contains("DeliveryReceived", StringComparer.Ordinal) || !pin.SupportedKinds.Contains("DeliveryDispatched", StringComparer.Ordinal) ||
            captured.HistorySourceFacts is null) return captured;
        var settled = captured.HistorySourceFacts.Where(fact => fact.Kind == RuntimeHistorySourceFactKind.ChildWorkflowSettled)
            .Select(fact => fact.SubjectId).ToHashSet(StringComparer.Ordinal);
        var resumeIds = dispatches.Where(item => settled.Contains(item.DispatchId))
            .Select(item => new WorkflowDispatchIdentity(item.ParentWorkflowExecutionId, item.ParentActivityExecutionId).ParentResumeIntentId)
            .ToHashSet(StringComparer.Ordinal);
        if (!captured.PostCommitIntents.Concat(captured.StateChanges.PostCommitOutbox
                .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert).Select(change => change.State.Intent))
                .Any(intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind && resumeIds.Contains(intent.IntentId))) return captured;
        if (_historyServices.GetService<IRuntimeHistoryObservationWriter>() is not { } writer ||
            _historyServices.GetService<IRuntimeHistoryStore>() is not { } reader ||
            _historyServices.GetService<IRuntimePostCommitIntentDispatcher>() is not { } dispatcher ||
            !dispatcher.CanRecordHistory(DispatchWorkflowConstants.ResumeParentIntentKind, writer, reader))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The originally captured parent-resume history owner is unavailable.");
        return RuntimeHistorySourceFacts.WithPostCommitCausation(captured, intent =>
        {
            var dispatch = dispatches.SingleOrDefault(item => settled.Contains(item.DispatchId) &&
                new WorkflowDispatchIdentity(item.ParentWorkflowExecutionId, item.ParentActivityExecutionId).ParentResumeIntentId == intent.IntentId);
            return dispatch is null ? null : ResumeCause(commit, dispatch, intent);
        }, _historyServices.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope());
    }

    private static RuntimeHistoryCausation ResumeCause(RuntimeCheckpointCommit commit, WorkflowDispatchRecord dispatch, RuntimePostCommitIntent intent) =>
        new(RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, nameof(RuntimeHistorySourceFactKind.ChildWorkflowSettled), dispatch.DispatchId),
            commit.WorkflowExecutionId, RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId));

    private static RuntimePostCommitIntent CopyCause(RuntimePostCommitIntent intent, RuntimeHistoryCausation cause) =>
        new(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey,
            intent.Payload, intent.Metadata, intent.DependsOnWaitRegistrationId, intent.WaitFailurePolicy, intent.MaterializedSchedulerWorkItem, intent.NotBefore)
        { HistoryCausation = cause };

    private static string? TerminalCheckpointName(WorkflowExecutionStatus status) => status switch
    {
        WorkflowExecutionStatus.Completed => RuntimeCheckpointNames.WorkflowCompleted,
        WorkflowExecutionStatus.Faulted => RuntimeCheckpointNames.WorkflowFaulted,
        WorkflowExecutionStatus.Cancelled => RuntimeCheckpointNames.WorkflowCancelled,
        _ => null
    };

    private static Exception TerminalConflict(bool controlEnabled, string message) => controlEnabled
        ? new WorkflowDispatchControlConflictException(message)
        : new InvalidOperationException(message);

    private static RuntimePostCommitIntent CreateIntent(
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity,
        IReadOnlyCollection<RuntimeWorkflowOutput> outputs,
        IReadOnlyCollection<string> incidentIds,
        DateTimeOffset recordedAt,
        RuntimeChildControlTransport? control)
    {
        var resultOutputs = dispatch.Status == WorkflowDispatchStatus.Completed
            ? outputs
                .OrderBy(output => output.Name, StringComparer.Ordinal)
                .Select(output => new DispatchWorkflowOutput(
                    output.Name,
                    output.Value,
                    output.Type.Id ?? output.Type.Kind,
                    output.IsRedacted))
                .ToArray()
            : [];
        var diagnostics = dispatch.Status switch
        {
            WorkflowDispatchStatus.Faulted => DispatchWorkflowDiagnostics.Faulted(incidentIds),
            WorkflowDispatchStatus.Cancelled => DispatchWorkflowDiagnostics.Cancelled(),
            _ => new Dictionary<string, string>()
        };
        var result = new DispatchWorkflowResult(
            dispatch.ChildWorkflowExecutionId,
            dispatch.Status,
            resultOutputs,
            diagnostics,
            control);
        var payload = new WorkflowDispatchParentResumePayload(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            result);

        return new RuntimePostCommitIntent(
            intentId: identity.ParentResumeIntentId,
            workflowExecutionId: dispatch.ParentWorkflowExecutionId,
            kind: DispatchWorkflowConstants.ResumeParentIntentKind,
            recordedAt: recordedAt,
            activityExecutionId: dispatch.ParentActivityExecutionId,
            idempotencyKey: identity.ParentResumeIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(payload, SerializerOptions),
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
    }

    private async ValueTask<RuntimeChildControlTransport?> CreateControlAsync(RuntimeCheckpointCommit commit,
        WorkflowDispatchRecord dispatch, WorkflowDispatchIdentity identity, CancellationToken cancellationToken)
    {
        if (WorkflowDispatchControlPolicy.ReadCapability(dispatch.Metadata) is null)
            return null;
        string? rootId = null;
        try
        {
            if (_controlBoundary is null)
                throw WorkflowDispatchControlPolicy.Failure("terminal control requires the boundary provider");
            var root = commit.StateChanges.ActivityExecutions.Where(change => change.Operation != RuntimeStateChangeOperation.Delete &&
                change.State.ParentActivityExecutionId is null && change.State.Execution.WorkflowExecutionId == dispatch.ChildWorkflowExecutionId)
                .Select(change => change.State).Single();
            if (root.ProducedControl is not { IsTerminal: true } batch)
                throw WorkflowDispatchControlPolicy.Failure("terminal control requires its whole staged root barrier");
            rootId = root.InvocationId;
            var target = await _controlBoundary.ResolveAsync(dispatch.ChildWorkflowExecutionId, root.InvocationId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("terminal control has no admitted boundary");
            WorkflowControlProducerEnricher.ValidateOwned(batch, target);
            if (target.DispatchId != dispatch.DispatchId)
                throw WorkflowDispatchControlPolicy.Failure("terminal control dispatch differs");
            return WorkflowControlTransport.Create(target, batch, commit.CommitId, identity.ParentResumeIntentId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw WorkflowDispatchControlPolicy.CheckpointFailure(dispatch.ChildWorkflowExecutionId, rootId, exception); }
    }

    internal static WorkflowDispatchParentResumePayload ValidateIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity,
        DateTimeOffset recordedAt)
    {
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, dispatch.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, dispatch.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.ResumeParentIntentKind) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ParentResumeIdempotencyKey) ||
            intent.RecordedAt != recordedAt ||
            intent.Metadata.Count != 2 ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
            !StringComparer.Ordinal.Equals(dispatchId, dispatch.DispatchId) ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.ChildWorkflowExecutionId, out var childWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(childWorkflowExecutionId, dispatch.ChildWorkflowExecutionId) ||
            intent.Payload is null)
        {
            throw new WorkflowDispatchControlConflictException($"Parent-resume intent '{intent.IntentId}' conflicts with dispatch '{dispatch.DispatchId}'.");
        }

        var payload = intent.Payload.Value.Deserialize<WorkflowDispatchParentResumePayload>(SerializerOptions)
            ?? throw new WorkflowDispatchControlConflictException($"Parent-resume intent '{intent.IntentId}' has no valid payload.");
        if (!StringComparer.Ordinal.Equals(payload.DispatchId, dispatch.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId) ||
            payload.Result.Status != dispatch.Status)
        {
            throw new WorkflowDispatchControlConflictException($"Parent-resume intent '{intent.IntentId}' payload conflicts with dispatch '{dispatch.DispatchId}'.");
        }

        return payload;
    }

    private async ValueTask<IReadOnlyCollection<string>> ReadIncidentIdsAsync(
        string childWorkflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> pendingChanges,
        CancellationToken cancellationToken)
    {
        if (_incidentStateStore is null)
            throw new InvalidOperationException("Faulted DispatchWorkflow completion requires an incident state store.");

        var incidents = new Dictionary<string, IncidentState>(StringComparer.Ordinal);
        foreach (var incident in await _incidentStateStore.ListBlockingAsync(childWorkflowExecutionId, cancellationToken))
        {
            if (incident.IsBlocking &&
                StringComparer.Ordinal.Equals(incident.WorkflowExecutionId, childWorkflowExecutionId))
            {
                incidents[incident.IncidentId] = incident;
            }
        }
        foreach (var change in pendingChanges.Where(change =>
                     StringComparer.Ordinal.Equals(change.State.WorkflowExecutionId, childWorkflowExecutionId)))
        {
            if (change.Operation == RuntimeStateChangeOperation.Delete || !change.State.IsBlocking)
                incidents.Remove(change.State.IncidentId);
            else
                incidents[change.State.IncidentId] = change.State;
        }

        return incidents.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryGetDispatchStatus(
        WorkflowExecutionStatus workflowStatus,
        out WorkflowDispatchStatus dispatchStatus)
    {
        dispatchStatus = workflowStatus switch
        {
            WorkflowExecutionStatus.Completed => WorkflowDispatchStatus.Completed,
            WorkflowExecutionStatus.Faulted => WorkflowDispatchStatus.Faulted,
            WorkflowExecutionStatus.Cancelled => WorkflowDispatchStatus.Cancelled,
            _ => default
        };
        return workflowStatus is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled;
    }
}
