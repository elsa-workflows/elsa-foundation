using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows.Runtime.Tools;

/// <summary>
/// Reads one exact workflow Run, read-only (spec 1193 FR-029): the Run an admitted <c>workflows.run</c> context names, and the
/// activity execution selected there, read again through Workflows Runtime Core. It returns the instance and its Executable,
/// status (a Run still executing is attributed to the read time), recorded incidents, one bounded page of activity
/// executions and the selected execution's bookmarks, incidents, value evidence (values only as the run endpoints' audited
/// payload read releases them; a withheld value stays withheld) and state view. What is unavailable is listed by reason.
/// Nothing is ever changed, and no run control is offered.
/// </summary>
internal sealed class WorkflowReadRunTool(
    IAgentToolAuthorizer authorizer,
    IServiceProvider services) : IAgentTool, IAgentToolAuthorization
{
    // Resolved only when the tool is used, so listing the installed tools never builds the Runtime read path.
    private IEnumerable<IWorkflowRunInspection> inspections => services.GetServices<IWorkflowRunInspection>();

    public const string ToolName = "workflow_read_run";
    internal const string ResultType = WorkflowRunContext.RunKind;
    /// <summary>The result's bound on its JSON text, under the host's 32 KiB per-attachment budget (#3710).</summary>
    internal const int ResultBudget = 28 * 1024;
    internal const int TokenLimit = 1024;
    private static readonly JsonSerializerOptions JsonOptions = WorkflowRunContext.JsonOptions;

    /// <summary>The bounds tried in order until the result fits the budget: incidents, activity executions, values, characters per value or view.</summary>
    private static readonly (int Incidents, int Activities, int Values, int ValueChars)[] Tiers =
        [(50, 50, 64, 4096), (25, 25, 32, 2048), (10, 10, 16, 1024), (5, 5, 8, 256), (2, 2, 4, 0), (0, 0, 0, 0)];

    internal static readonly AgentToolDescriptor Contract = new(ToolName, "Read workflow Run",
        "Returns one exact workflow Run named by an admitted workflows.run context (its workflowExecutionId, or the only one), read-only: the Workflow " +
        "Instance and its Executable (artifact id and Artifact Hash), status (a Run still in progress is attributed to the read time), recorded incidents, " +
        "a page of activity executions (activityPageSize, activityContinuationToken), and the selected activity execution's bookmarks, incidents, value " +
        "evidence and state view. A withheld value stays withheld; unavailable data is listed by reason. It never retries, cancels, deletes or dispatches a Run.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly,
        """{"type":"object","properties":{"workflowExecutionId":{"type":"string","minLength":1,"maxLength":256},"activityPageSize":{"type":"integer","minimum":1,"maximum":50},"activityContinuationToken":{"type":"string","minLength":1,"maxLength":1024}},"additionalProperties":false}""",
        ["workflows/runtime:read"], ["/workflows"])
    {
        ModuleId = WorkflowRunContext.ModuleId, ContractVersion = "1", ExecutionLocation = AgentExecutionLocation.Server,
        ResultType = ResultType, ResultSchemaVersion = "1", DisclosureSourceId = WorkflowRunContext.ContextSourceId
    };

    public AgentToolDescriptor Descriptor => Contract;

    public async ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkflowRunContext.Inspection(inspections) is not { } inspection)
            return new(false, [new(WorkflowRunContext.UnavailableCode, WorkflowRunContext.UnavailableMessage) { OwnerUnavailable = true }]);
        if (target is null) return new(true, []);
        if (!WorkflowRunContext.IsTarget(target))
            return new(false, [new("agent.workflow.resource_required", "One exact workflow Run is this tool's only target.")]);
        // The host decided the runtime read permission; the owner decides that the Run (and its selection) is still visible.
        var selected = WorkflowRunContext.SelectedActivity(target);
        var run = await inspection.GetAsync(new(target.ResourceId!, selected, 1), cancellationToken);
        if (run is null || run.Instance.WorkflowExecutionId != target.ResourceId)
            return new(false, [new(WorkflowRunContext.NotFoundCode, WorkflowRunContext.NotFoundMessage)]);
        return selected is not null && run.Selection is not { Execution: not null } and not { UnavailableReason: not null }
            ? new(false, [new(WorkflowRunContext.ActivityNotFoundCode, WorkflowRunContext.ActivityNotFoundMessage)])
            : new(true, []);
    }

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("agent.workflow.run_arguments_invalid", "The arguments must be JSON.", 400); }
        string? workflowExecutionId = null, token = null;
        var pageSize = WorkflowRunInspectionRequest.DefaultActivityPageSize;
        if (arguments.ValueKind != JsonValueKind.Object) return Invalid();
        foreach (var property in arguments.EnumerateObject())
        {
            switch (property.Name)
            {
                case "workflowExecutionId" when WorkflowRunContext.Identity(property.Value) is { } value: workflowExecutionId = value; break;
                case "activityPageSize" when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var size)
                                              && size is >= 1 and <= WorkflowRunInspectionRequest.MaxActivityPageSize: pageSize = size; break;
                case "activityContinuationToken" when property.Value.ValueKind == JsonValueKind.String
                                                       && property.Value.GetString() is { Length: > 0 and <= TokenLimit } value: token = value; break;
                default: return Invalid();
            }
        }
        var contexts = invocation.Context.Where(item => item.ContentType == WorkflowRunContext.RunKind && item.Origin == AgentContextOrigin.AuthoritativeRead)
            .Select(item => WorkflowRunContext.TryRead(item, out var reference, out _) ? reference : null).OfType<WorkflowRunContext.Reference>().ToArray();
        var bound = workflowExecutionId is null ? contexts.Length == 1 ? contexts[0] : null : contexts.FirstOrDefault(item => item.WorkflowExecutionId == workflowExecutionId);
        if (bound is null)
            return Failure("agent.workflow.run_context_required",
                "One admitted workflow Run context is required, and workflowExecutionId must name one of them; nothing was read.", 409);
        var read = await WorkflowRunContext.ReadAsync(authorizer, WorkflowRunContext.Inspection(inspections), invocation.ActorId, invocation.TenantId, bound,
            cancellationToken, pageSize, token, resolveSelectedValues: bound.ActivityExecutionId is not null);
        switch (read.Outcome)
        {
            case WorkflowRunContext.Outcome.Denied: return Failure("agent.workflow.permission_denied", WorkflowRunContext.DeniedMessage, 403);
            case WorkflowRunContext.Outcome.NotFound: return Failure(WorkflowRunContext.NotFoundCode, WorkflowRunContext.NotFoundMessage, 404);
            case WorkflowRunContext.Outcome.ActivityNotFound: return Failure(WorkflowRunContext.ActivityNotFoundCode, WorkflowRunContext.ActivityNotFoundMessage, 404);
            case WorkflowRunContext.Outcome.Unavailable: return Failure(WorkflowRunContext.UnavailableCode, WorkflowRunContext.UnavailableMessage, 503);
        }
        var run = read.Run!;
        // The largest result whose text fits the budget; a smaller one says what it left out.
        JsonElement data = default;
        foreach (var tier in Tiers)
        {
            data = Result(run, bound.ActivityExecutionId, pageSize, tier);
            if (data.GetRawText().Length <= ResultBudget) break;
        }
        return AgentResult<AgentToolPreparation>.Success(new(WorkflowRunContext.Target(run.Instance.WorkflowExecutionId, bound.ActivityExecutionId), data,
            AgentRisk.ReadOnly, new(ResultType, "1", data), RequiresApproval: false));
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the exact workflow Run through the workflow runtime; nothing was changed.", new(ResultType, "1", invocation.Arguments))));

    private static JsonElement Result(WorkflowRunInspectionDetails run, string? activityExecutionId, int pageSize,
        (int Incidents, int Activities, int Values, int ValueChars) tier)
    {
        var instance = run.Instance;
        var unavailable = new List<object>();
        if (!run.CanInspectValues && run.Incidents.Count > 0)
            unavailable.Add(new { data = "incident messages", reason = "notPermitted", explanation = WorkflowRunContext.Explain(WorkflowRunUnavailableReason.NotPermitted) });
        if (run.InspectionGranularity != "activity-level")
            unavailable.Add(new { data = "exact per-activity evidence", reason = "notCaptured",
                explanation = $"This Run executed under the {run.PersistenceMode} persistence mode, so its activity evidence is folded to commit boundaries ({run.InspectionGranularity})." });
        object? selected = null;
        if (activityExecutionId is not null)
        {
            if (run.Selection is { Execution: { } execution })
            {
                var values = execution.Values.Take(tier.Values).Select(value => Value(value, tier.ValueChars, unavailable)).ToArray();
                var state = execution.StateView;
                if (!state.Available && state.UnavailableReason is { } stateReason)
                    unavailable.Add(new { data = "selected activity execution state view", reason = WorkflowRunContext.Reason(stateReason), explanation = WorkflowRunContext.Explain(stateReason) });
                var view = state.View is { } projection && projection.GetRawText().Length <= tier.ValueChars ? (JsonElement?)projection : null;
                selected = new
                {
                    activityExecutionId, summary = Summary(execution.Summary), parentActivityExecutionId = execution.ParentActivityExecutionId,
                    schedulingActivityExecutionId = execution.SchedulingActivityExecutionId, attemptNumber = execution.AttemptNumber,
                    bookmarks = execution.Bookmarks.Select(bookmark => new
                    {
                        bookmarkId = bookmark.BookmarkId, stimulusType = bookmark.StimulusType, createdAt = bookmark.CreatedAt, expiresAt = bookmark.ExpiresAt
                    }).ToArray(),
                    incidents = execution.Incidents.Take(Math.Max(tier.Incidents, 1)).Select(Incident).ToArray(),
                    incidentsTruncated = execution.Incidents.Count > Math.Max(tier.Incidents, 1),
                    values, valuesTruncated = execution.Values.Count > values.Length,
                    stateView = new
                    {
                        available = state.Available, unavailableReason = state.UnavailableReason is { } reason ? WorkflowRunContext.Reason(reason) : null,
                        viewKind = state.ViewKind, viewVersion = state.ViewVersion, view, viewOmitted = state.View is not null && view is null
                    }
                };
            }
            else if (run.Selection?.UnavailableReason is { } reason)
            {
                unavailable.Add(new { data = "selected activity execution " + activityExecutionId, reason = WorkflowRunContext.Reason(reason), explanation = WorkflowRunContext.Explain(reason) });
                selected = new { activityExecutionId, unavailable = WorkflowRunContext.Reason(reason), explanation = WorkflowRunContext.Explain(reason) };
            }
        }
        return JsonSerializer.SerializeToElement(new
        {
            readOnly = true, workflowExecutionId = instance.WorkflowExecutionId, activityExecutionId,
            readAt = run.ReadAt, inProgress = !instance.IsTerminal, attribution = WorkflowRunContext.Attribution(run),
            instance = new
            {
                workflowExecutionId = instance.WorkflowExecutionId, artifactId = instance.ArtifactId, artifactHash = instance.ArtifactHash,
                definitionId = instance.DefinitionId, definitionVersionId = instance.DefinitionVersionId, artifactVersion = instance.ArtifactVersion,
                status = instance.Status, subStatus = instance.SubStatus, runKind = instance.RunKind, createdAt = instance.CreatedAt, startedAt = instance.StartedAt,
                updatedAt = instance.UpdatedAt, completedAt = instance.CompletedAt, parentWorkflowExecutionId = instance.ParentWorkflowExecutionId,
                activityCount = instance.ActivityCount, incidentCount = instance.IncidentCount, activeIncidentCount = instance.ActiveIncidentCount,
                blockingIncidentCount = instance.BlockingIncidentCount, sourceReferenceId = instance.SourceReferenceId, publicationId = instance.PublicationId
            },
            canInspectValues = run.CanInspectValues, persistenceMode = run.PersistenceMode, inspectionGranularity = run.InspectionGranularity,
            incidents = run.Incidents.Take(tier.Incidents).Select(Incident).ToArray(), incidentsTruncated = run.Incidents.Count > tier.Incidents,
            activities = new
            {
                items = run.Activities.Items.Take(Math.Min(tier.Activities, pageSize)).Select(Summary).ToArray(), pageSize,
                itemsTruncated = run.Activities.Items.Count > Math.Min(tier.Activities, pageSize),
                totalCount = run.Activities.TotalCount, nextContinuationToken = run.Activities.NextContinuationToken
            },
            selectedActivity = selected,
            unavailable,
            runControls = WorkflowRunContext.RunControls
        }, JsonOptions);
    }

    private static object Incident(WorkflowRunInspectionIncident incident) => new
    {
        incidentId = incident.IncidentId, activityExecutionId = incident.ActivityExecutionId, executableNodeId = incident.ExecutableNodeId,
        severity = incident.Severity, status = incident.Status, failureType = incident.FailureType, message = incident.Message,
        messageRedacted = incident.MessageRedacted, isBlocking = incident.IsBlocking, createdAt = incident.CreatedAt, resolvedAt = incident.ResolvedAt
    };

    private static object Summary(WorkflowRunInspectionActivitySummary activity) => new
    {
        activityExecutionId = activity.ActivityExecutionId, executableNodeId = activity.ExecutableNodeId, authoredActivityId = activity.AuthoredActivityId,
        activityType = activity.ActivityType, activityTypeVersion = activity.ActivityTypeVersion, status = activity.Status, subStatus = activity.SubStatus,
        executionSequence = activity.ExecutionSequence, scheduledAt = activity.ScheduledAt, startedAt = activity.StartedAt, completedAt = activity.CompletedAt,
        outcomes = activity.OutcomeNames, bookmarkCount = activity.BookmarkCount, incidentCount = activity.IncidentCount, valueCount = activity.ValueSnapshotCount
    };

    // A value as its evidence says: resolved values only as the payload read released them (within the bound), a withheld
    // one by its marker alone, an unavailable one by its reason.
    private static object Value(WorkflowRunInspectionValue value, int limit, List<object> unavailable)
    {
        var disclosure = value.Disclosure switch
        {
            WorkflowRunValueDisclosure.Resolved => "resolved",
            WorkflowRunValueDisclosure.Resolvable => "resolvable",
            WorkflowRunValueDisclosure.Withheld => "withheld",
            _ => "unavailable"
        };
        var released = value.Disclosure == WorkflowRunValueDisclosure.Resolved && value.Value is { } payload && payload.GetRawText().Length <= limit ? value.Value : null;
        var label = value.Subject + " '" + (value.InputKey ?? value.Name) + "'";
        if (value.Disclosure == WorkflowRunValueDisclosure.Withheld)
            unavailable.Add(new { data = label, reason = "withheld", explanation = "withheld by execution evidence (" + (value.WithheldKind ?? "Sensitive") + "); it is never shown" });
        else if (value.UnavailableReason is { } reason)
            unavailable.Add(new { data = label, reason = WorkflowRunContext.Reason(reason), explanation = WorkflowRunContext.Explain(reason) });
        return new
        {
            evidenceId = value.EvidenceId, name = value.Name, subject = value.Subject, inputKey = value.InputKey, captureMode = value.CaptureMode,
            captureState = value.CaptureState, accessState = value.AccessState, sensitive = value.IsSensitive, capturedAt = value.CapturedAt,
            captureReason = value.CaptureReason, typeKind = value.TypeKind, typeId = value.TypeId, disclosure, withheldKind = value.WithheldKind,
            secretReferenceName = value.SecretReferenceName,
            unavailableReason = value.UnavailableReason is { } why ? WorkflowRunContext.Reason(why) : null,
            value = released, valueOmitted = value.Disclosure == WorkflowRunValueDisclosure.Resolved && released is null
        };
    }

    private static AgentResult<AgentToolPreparation> Invalid() => Failure("agent.workflow.run_arguments_invalid",
        "Only workflowExecutionId (a Run identity), activityPageSize (1 to 50) and activityContinuationToken are accepted.", 400);

    private static AgentResult<AgentToolPreparation> Failure(string code, string message, int status) =>
        AgentResult<AgentToolPreparation>.Failure(code, message, status);
}
