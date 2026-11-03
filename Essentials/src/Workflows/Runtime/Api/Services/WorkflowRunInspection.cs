using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// The default <see cref="IWorkflowRunInspection"/> (spec 1193 slice G, D4): exactly the reads the run endpoints make, through
/// the same services, mapped to the Core models. <c>GET runtime/workflows/instances/{id}</c> gives the instance, its incidents
/// and one page of its activity executions; <c>.../activity-executions/{activityExecutionId}</c> the selected execution; its
/// <c>state-view</c> the module projection; and, only when asked, the separately authorized and audited value-payload read
/// each resolvable value. Each service applies the run endpoints' own per-run authority for the current request, so this
/// read is never broader than the run page's. It decides no endpoint permission; the consumer does.
/// </summary>
/// <remarks>
/// A missing activity execution inspection, state view or payload read is reported as unsupported by this backend; an
/// activity execution this Run recorded (on its activity page or an incident) but whose inspection record is gone, or a value
/// whose evidence vanished, as pruned.
/// </remarks>
internal sealed class WorkflowRunInspection(
    IWorkflowInstanceDetailsService details,
    IActivityExecutionInspectionService? activities = null,
    IActivityExecutionStateViewService? stateViews = null,
    IActivityExecutionValuePayloadReader? payloads = null,
    TimeProvider? timeProvider = null) : IWorkflowRunInspection
{
    /// <summary>At most this many values of the selected activity execution are resolved in one read; the rest stay resolvable.</summary>
    internal const int ResolveLimit = 32;

    public async ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var id = request.WorkflowExecutionId;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var pageSize = Math.Clamp(request.ActivityPageSize, 1, WorkflowRunInspectionRequest.MaxActivityPageSize);
        var view = await details.GetAsync(new GetWorkflowInstance(id, pageSize, request.ActivityContinuationToken), cancellationToken);
        if (view is null || view.Instance.WorkflowExecutionId != id) return null;
        var readAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var canInspectValues = view.Permissions?.CanInspectValues == true;
        var selection = string.IsNullOrWhiteSpace(request.ActivityExecutionId) ? null
            : await SelectAsync(view, request.ActivityExecutionId, canInspectValues, request.ResolveSelectedValues, cancellationToken);
        return new(
            Instance(view.Instance),
            view.Incidents.Select(incident => Incident(incident, canInspectValues)).ToArray(),
            new(view.Activities.Select(Summary).ToArray(), view.Instance.ActivityCount, view.ActivityNextContinuationToken),
            selection,
            canInspectValues,
            view.PersistenceMode,
            view.InspectionGranularity,
            readAt);
    }

    private async Task<WorkflowRunInspectionSelection> SelectAsync(WorkflowInstanceDetailsView run, string activityExecutionId, bool canInspectValues,
        bool resolve, CancellationToken cancellationToken)
    {
        if (activities is null) return new(activityExecutionId, null, WorkflowRunUnavailableReason.Unsupported);
        var workflowExecutionId = run.Instance.WorkflowExecutionId;
        var execution = await activities.GetAsync(new GetActivityExecution(workflowExecutionId, activityExecutionId), cancellationToken);
        if (execution is null || execution.WorkflowExecutionId != workflowExecutionId || execution.ActivityExecutionId != activityExecutionId)
        {
            // Recorded for this Run (on its activity page or by an incident) but no longer inspectable: pruned. Otherwise the Run has no such execution.
            var recorded = run.Activities.Any(item => item.ActivityExecutionId == activityExecutionId)
                || run.Incidents.Any(item => item.ActivityExecutionId == activityExecutionId);
            return new(activityExecutionId, null, recorded ? WorkflowRunUnavailableReason.Pruned : null);
        }
        var values = new List<WorkflowRunInspectionValue>();
        var reads = 0;
        foreach (var snapshot in execution.ValueSnapshots)
        {
            var (value, read) = await ValueAsync(execution, snapshot, resolve && reads < ResolveLimit, cancellationToken);
            if (read) reads++;
            values.Add(value);
        }
        var summary = new WorkflowRunInspectionActivitySummary(execution.ActivityExecutionId, execution.ExecutableNodeId, execution.AuthoredActivityId,
            execution.ActivityType, execution.ActivityTypeVersion, execution.Status, execution.SubStatus, execution.ExecutionSequence, execution.ScheduledAt,
            execution.StartedAt, execution.CompletedAt, execution.OutcomeNames.ToArray(), execution.Bookmarks.Count, execution.Incidents.Count,
            execution.ValueSnapshots.Count);
        return new(activityExecutionId, new(summary, execution.Provenance.ParentActivityExecutionId, execution.Provenance.SchedulingActivityExecutionId,
            execution.Attempt?.AttemptNumber,
            execution.Bookmarks.Select(bookmark => new WorkflowRunInspectionBookmark(bookmark.Identity.BookmarkId, bookmark.StimulusType, bookmark.CreatedAt,
                bookmark.ExpiresAt)).ToArray(),
            execution.Incidents.Select(incident => new WorkflowRunInspectionIncident(incident.IncidentId, execution.ActivityExecutionId,
                incident.Causation?.ExecutableNodeId ?? execution.ExecutableNodeId, incident.Severity, incident.Status, incident.FailureType, incident.Message,
                !canInspectValues, incident.IsBlocking, incident.CreatedAt, incident.ResolvedAt)).ToArray(),
            values,
            await StateViewAsync(workflowExecutionId, activityExecutionId, cancellationToken)), null);
    }

    // The evidence record as the activity execution read discloses it; the value itself only through the payload read. Returns
    // whether the payload read was made.
    private async Task<(WorkflowRunInspectionValue Value, bool Read)> ValueAsync(ActivityExecutionInspectionView execution, ActivityExecutionInspectionValueSnapshotView snapshot,
        bool resolve, CancellationToken cancellationToken)
    {
        (WorkflowRunInspectionValue, bool) Value(WorkflowRunValueDisclosure disclosure, WorkflowRunUnavailableReason? reason = null, System.Text.Json.JsonElement? value = null,
            bool read = false) =>
            (new(snapshot.EvidenceId, snapshot.Name, snapshot.Subject, snapshot.InputKey, snapshot.CaptureMode, snapshot.CaptureState, snapshot.AccessState ?? "unavailable",
                snapshot.IsSensitive, snapshot.CapturedAt, snapshot.CaptureReason, snapshot.Type?.Kind, snapshot.Type?.Id, snapshot.WithheldKind,
                snapshot.SecretReferenceName, disclosure, reason, value), read);

        // A withheld record has no value, whatever it carries. A sensitive value is withheld too: this read never releases one,
        // even when a capture policy captured it; the run page's own payload read stays the only way to see it.
        if (snapshot.WithheldKind is not null || snapshot.IsSensitive) return Value(WorkflowRunValueDisclosure.Withheld);
        if (snapshot.CaptureState is "notCaptured" or "metadataOnly" or "captureFailed" or "unavailable")
            return Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured);
        switch (snapshot.AccessState)
        {
            case "redacted" or "resolutionPermissionRequired":
                return Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted);
            case "resolutionAvailable" when !resolve:
                return Value(WorkflowRunValueDisclosure.Resolvable);
            case "resolutionAvailable" when payloads is null:
                return Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.Unsupported);
            case "resolutionAvailable":
                var read = await payloads!.ReadAsync(execution.WorkflowExecutionId, execution.ActivityExecutionId, snapshot.EvidenceId, cancellationToken);
                return read.Outcome switch
                {
                    ActivityExecutionValuePayloadReadOutcome.Resolved when read.Value is not null =>
                        Value(WorkflowRunValueDisclosure.Resolved, null, read.Value.Payload.Clone(), read: true),
                    ActivityExecutionValuePayloadReadOutcome.Denied => Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted, read: true),
                    ActivityExecutionValuePayloadReadOutcome.NotFound => Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.Pruned, read: true),
                    _ => Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured, read: true)
                };
            default:
                return Value(WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured);
        }
    }

    private async Task<WorkflowRunInspectionStateView> StateViewAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken)
    {
        if (stateViews is null) return new(false, WorkflowRunUnavailableReason.Unsupported, null, null, null);
        var view = await stateViews.GetAsync(new GetActivityExecutionStateView(workflowExecutionId, activityExecutionId), cancellationToken);
        if (view is null) return new(false, WorkflowRunUnavailableReason.NotCaptured, null, null, null);
        if (!view.Available)
            return new(false, view.UnavailableReason == ActivityStateView.NoProjector ? WorkflowRunUnavailableReason.Unsupported : WorkflowRunUnavailableReason.NotCaptured,
                null, null, null);
        return new(true, null, view.ViewKind, view.ViewVersion, view.View?.Clone());
    }

    private static WorkflowRunInspectionInstance Instance(WorkflowInstanceSummaryView instance) =>
        new(instance.WorkflowExecutionId, instance.ArtifactId, instance.ArtifactHash, instance.DefinitionId, instance.DefinitionVersionId, instance.ArtifactVersion,
            instance.Status, instance.SubStatus, instance.RunKind,
            Enum.TryParse<WorkflowExecutionStatus>(instance.Status, ignoreCase: false, out var status) && status.IsTerminal(),
            instance.CreatedAt, instance.StartedAt, instance.UpdatedAt, instance.CompletedAt, instance.ParentWorkflowExecutionId, instance.ActivityCount,
            instance.IncidentCount, instance.ActiveIncidentCount, instance.BlockingIncidentCount, instance.SourceReferenceId, instance.PublicationId);

    private static WorkflowRunInspectionIncident Incident(IncidentStateView incident, bool canInspectValues) =>
        new(incident.IncidentId, incident.ActivityExecutionId, incident.ExecutableNodeId, incident.Severity, incident.Status, incident.FailureType, incident.Message,
            !canInspectValues, incident.IsBlocking, incident.CreatedAt, incident.ResolvedAt);

    private static WorkflowRunInspectionActivitySummary Summary(ActivityExecutionInspectionSummaryView activity) =>
        new(activity.ActivityExecutionId, activity.ExecutableNodeId, activity.AuthoredActivityId, activity.ActivityType, activity.ActivityTypeVersion, activity.Status,
            activity.SubStatus, activity.ExecutionSequence, activity.ScheduledAt, activity.StartedAt, activity.CompletedAt, activity.OutcomeNames.ToArray(),
            activity.BookmarkCount, activity.IncidentCount, activity.ValueSnapshotCount);
}

/// <summary>Rejects a second <see cref="IWorkflowRunInspection"/> registration at startup (constitution-framework §2.6.2).</summary>
internal sealed class WorkflowRunInspectionStartupTask(IServiceCollection registrations) : IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    /// <summary>Throws when the collection holds more than one Run inspection registration.</summary>
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IWorkflowRunInspection)) > 1)
            throw new InvalidOperationException("IWorkflowRunInspection has multiple registrations; select one explicit workflow Run inspection.");
    }
}
