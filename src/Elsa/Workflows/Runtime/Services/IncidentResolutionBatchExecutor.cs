using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Strategies;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Stages and atomically commits one deterministic batch of ordinary incident-resolution decisions.</summary>
public sealed class IncidentResolutionBatchExecutor(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IIncidentStateStore incidentStateStore,
    IWorkflowExecutableStore workflowExecutableStore,
    IIncidentStrategyResolver strategyResolver,
    RuntimeCheckpointCommitter checkpointCommitter,
    TimeProvider timeProvider,
    IEnumerable<IncidentStrategySafeIntentRegistration>? safeIntentRegistrations = null)
{
    private readonly HashSet<string> _safeIntentKinds = safeIntentRegistrations?
        .Select(registration => registration.Descriptor.Kind)
        .ToHashSet(StringComparer.Ordinal) ?? [];

    public async ValueTask ExecuteAsync(
        WorkflowExecutionCommandEnvelope envelope,
        IReadOnlyCollection<IncidentState> incidents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(incidents);
        cancellationToken.ThrowIfCancellationRequested();

        var workflow = await workflowExecutionStateStore.FindAsync(envelope.WorkflowExecutionId, cancellationToken);
        if (workflow is null || workflow.Status.IsTerminal())
            return;

        var executable = await workflowExecutableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Pinned executable '{workflow.PinnedExecutable.ArtifactId}' was not found for workflow execution '{workflow.WorkflowExecutionId}'.");
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, workflow.PinnedExecutable))
        {
            throw new InvalidOperationException(
                $"Executable '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}' does not match the pinned snapshot " +
                $"'{WorkflowExecutableIdentityComparer.Format(workflow.PinnedExecutable)}' for workflow execution '{workflow.WorkflowExecutionId}'.");
        }

        var appliedAt = timeProvider.GetUtcNow();
        var stagedIncidents = new List<IncidentState>();
        var stagedIntentRequests = new List<(IncidentState Incident, IncidentStrategySafePostCommitIntent Intent)>();
        var requestWorkflowFault = false;
        foreach (var candidate in incidents
                     .Where(candidate => candidate.Status == IncidentStatus.Blocking && candidate.ResolutionOutcome is null)
                     .OrderBy(candidate => candidate.IncidentId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var incident = await incidentStateStore.FindAsync(workflow.WorkflowExecutionId, candidate.IncidentId, cancellationToken);
            if (incident is null || incident.Status != IncidentStatus.Blocking || incident.ResolutionOutcome is not null)
                continue;
            var stage = new IncidentResolutionActionStagingContext(_safeIntentKinds);
            var action = await ResolveActionAsync(executable.IncidentStrategy, incident, workflow, executable, cancellationToken);
            var failurePhase = action.FailurePhase;
            string actionKind;
            if (action.Action is not null)
            {
                try
                {
                    actionKind = ValidateAction(action.Action);
                    await action.Action.ExecuteAsync(stage, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    stage = new IncidentResolutionActionStagingContext(_safeIntentKinds);
                    var fallbackAction = IncidentResolutionActions.FaultWorkflow();
                    action = new ResolvedAction(fallbackAction, "Execute");
                    actionKind = ValidateAction(fallbackAction);
                    await fallbackAction.ExecuteAsync(stage, cancellationToken);
                    failurePhase = action.FailurePhase;
                }
            }
            else
            {
                throw new InvalidOperationException("Incident strategy resolution produced no action or fallback.");
            }

            var systemSource = failurePhase is null ? null : IncidentResolutionSystemSources.IncidentStrategyFailure;
            var outcomeMetadata = stage.Metadata;
            if (failurePhase is not null)
                outcomeMetadata = outcomeMetadata.Append(new KeyValuePair<string, string>("phase", failurePhase)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            var outcome = new IncidentResolutionOutcome(
                actionKind,
                appliedAt,
                executable.IncidentStrategy,
                systemSource,
                outcomeMetadata);
            stagedIncidents.Add(new IncidentState(
                incident.IncidentId,
                incident.WorkflowExecutionId,
                incident.ActivityExecutionId,
                incident.ExecutableNodeId,
                incident.Severity,
                stage.Status,
                outcome,
                incident.FailureType,
                incident.Message,
                incident.CreatedAt,
                stage.Status == IncidentStatus.Resolved ? appliedAt : null,
                incident.Metadata));
            stagedIntentRequests.AddRange(stage.PostCommitIntents.Select(intent => (incident, intent)));
            requestWorkflowFault |= stage.RequestedWorkflowFault;
        }

        if (stagedIncidents.Count == 0)
            return;

        var batchFingerprint = RuntimeChainId.Fingerprint(envelope.EnvelopeId);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = RuntimeCheckpointNames.IncidentResolutionBatchApplied,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        var faultedWorkflow = requestWorkflowFault
            ? RuntimeContainerScopeService.CloseRootFrame(workflow with
            {
                Status = WorkflowExecutionStatus.Faulted,
                UpdatedAt = appliedAt,
                CompletedAt = appliedAt
            })
            : null;
        var postCommitIntents = stagedIntentRequests.Select((request, index) =>
        {
            var intentId = $"intent:{workflow.WorkflowExecutionId}:incident-resolution:{batchFingerprint}:{RuntimeChainId.Fingerprint(request.Incident.IncidentId)}:{index:D4}";
            return new RuntimePostCommitIntent(
                intentId,
                workflow.WorkflowExecutionId,
                request.Intent.Kind,
                appliedAt,
                request.Incident.ActivityExecutionId,
                intentId,
                request.Intent.Payload,
                request.Intent.Metadata);
        }).ToArray();
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflow.WorkflowExecutionId}:incident-strategy-resolution:{batchFingerprint}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workflow.WorkflowExecutionId}:incident-strategy-resolution:{batchFingerprint}",
                Name: RuntimeCheckpointNames.IncidentResolutionBatchApplied,
                WorkflowExecutionId: workflow.WorkflowExecutionId,
                OccurredAt: appliedAt,
                ActivityExecutionIds: [],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: faultedWorkflow is null
                    ? null
                    : new RuntimeStateChange<WorkflowExecutionState>(workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, faultedWorkflow, metadata),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: stagedIncidents.Select(incident => new RuntimeStateChange<IncidentState>(
                    incident.IncidentId,
                    RuntimeStateChangeOperation.Upsert,
                    incident,
                    metadata)).ToArray(),
                operational: [],
                activityExecutionInspections: []),
            PostCommitIntents: postCommitIntents,
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask<ResolvedAction> ResolveActionAsync(
        Elsa.Workflows.Primitives.Models.IncidentStrategyReference strategy,
        IncidentState incident,
        WorkflowExecutionState workflow,
        WorkflowExecutable executable,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolutionStrategy = strategyResolver.Resolve(strategy);
            var activity = incident.ActivityExecutionId is null
                ? null
                : await activityExecutionStateStore.FindAsync(workflow.WorkflowExecutionId, incident.ActivityExecutionId, cancellationToken);
            var context = new IncidentStrategyContext(
                new IncidentStrategyIncidentSnapshot(
                    incident.IncidentId,
                    incident.WorkflowExecutionId,
                    incident.ActivityExecutionId,
                    incident.ExecutableNodeId,
                    incident.Status.ToString(),
                    incident.Severity.ToString(),
                    incident.FailureType,
                    incident.CreatedAt,
                    IncidentStrategyMetadata.ProjectIncident(incident.Metadata)),
                activity is null
                    ? null
                    : new IncidentStrategyActivitySnapshot(
                        activity.Execution.ActivityExecutionId,
                        activity.Execution.ActivityType,
                        activity.Status.ToString()),
                new IncidentStrategyWorkflowSnapshot(workflow.WorkflowExecutionId, workflow.Status.ToString()),
                new IncidentStrategyExecutableSnapshot(
                    executable.Identity.DefinitionId,
                    executable.Identity.DefinitionVersionId,
                    executable.Identity.ArtifactHash,
                    executable.IncidentStrategy));
            var action = await resolutionStrategy.ResolveAsync(context, cancellationToken);
            return action is null
                ? new ResolvedAction(IncidentResolutionActions.FaultWorkflow(), "Resolve")
                : new ResolvedAction(action, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ResolvedAction(IncidentResolutionActions.FaultWorkflow(), "Resolve");
        }
    }

    private static string ValidateAction(IIncidentResolutionAction action)
    {
        var kind = action.Kind;
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var validBuiltIn = kind switch
        {
            IncidentResolutionActionKinds.FaultWorkflow => ReferenceEquals(action, IncidentResolutionActions.FaultWorkflow()),
            IncidentResolutionActionKinds.ContinueWithIncidents => ReferenceEquals(action, IncidentResolutionActions.ContinueWithIncidents()),
            IncidentResolutionActionKinds.WaitForIntervention => ReferenceEquals(action, IncidentResolutionActions.WaitForIntervention()),
            _ => false
        };
        if ((kind is IncidentResolutionActionKinds.FaultWorkflow
                 or IncidentResolutionActionKinds.ContinueWithIncidents
                 or IncidentResolutionActionKinds.WaitForIntervention) &&
            !validBuiltIn)
            throw new InvalidOperationException($"Incident-resolution action kind '{kind}' is reserved for a runtime-owned action.");
        if (!validBuiltIn && (!kind.Contains('.', StringComparison.Ordinal) || kind.Split('.').Any(String.IsNullOrWhiteSpace)))
            throw new InvalidOperationException($"Custom incident-resolution action kind '{kind}' must be dotted and namespaced.");
        return kind;
    }

    private sealed record ResolvedAction(IIncidentResolutionAction? Action, string? FailurePhase);
}

internal sealed class IncidentResolutionActionStagingContext(IReadOnlySet<string> safeIntentKinds) : IncidentResolutionActionContext
{
    private IncidentStatus? _status;
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);

    public IncidentStatus Status => _status ?? IncidentStatus.Blocking;
    public bool RequestedWorkflowFault { get; private set; }
    public IReadOnlyDictionary<string, string> Metadata => _metadata;
    public IReadOnlyCollection<IncidentStrategySafePostCommitIntent> PostCommitIntents => _postCommitIntents;
    private readonly List<IncidentStrategySafePostCommitIntent> _postCommitIntents = [];

    public override void KeepIncidentBlocking() => SetStatus(IncidentStatus.Blocking);
    public override void OpenIncident() => SetStatus(IncidentStatus.Open);
    public override void ResolveIncident() => SetStatus(IncidentStatus.Resolved);
    public override void RequestWorkflowFault() => RequestedWorkflowFault = true;

    public override void AddMetadata(string key, string value)
    {
        IncidentStrategyMetadata.ValidateEntry(key, value);
        if (_metadata.Count >= IncidentStrategyMetadata.MaxEntries)
            throw new InvalidOperationException($"An incident-resolution action may add at most {IncidentStrategyMetadata.MaxEntries} metadata entries.");
        if (!_metadata.TryAdd(key, value))
            throw new InvalidOperationException($"Incident-resolution metadata key '{key}' was added more than once.");
    }

    public override void AddPostCommitIntent(IncidentStrategySafePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!safeIntentKinds.Contains(intent.Kind))
            throw new InvalidOperationException($"Post-commit intent kind '{intent.Kind}' is not registered as strategy-safe.");
        if (_postCommitIntents.Count >= 32)
            throw new InvalidOperationException("An incident-resolution action may request at most 32 post-commit intents.");
        _postCommitIntents.Add(intent);
    }

    private void SetStatus(IncidentStatus status)
    {
        if (_status is not null)
            throw new InvalidOperationException("An incident-resolution action may stage only one target incident transition.");
        _status = status;
    }
}
