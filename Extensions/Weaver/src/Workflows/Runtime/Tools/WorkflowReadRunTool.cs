using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows.Runtime.Tools;

/// <summary>
/// Reads one exact workflow Run, read-only (spec 1193 FR-029): the Run an admitted <c>workflows.run</c> context names, and the
/// activity execution selected there, read again through Workflows Runtime Core. It returns the instance and its Executable,
/// status (a Run still executing is attributed to the read time), recorded incidents, one bounded page of activity
/// executions and the selected execution's bookmarks, incidents, value evidence (values only as the run endpoints' audited
/// payload read releases them; a withheld value stays withheld) and state view. What is unavailable is listed by reason.
/// Nothing is ever changed, and no run control is offered.
/// </summary>
/// <remarks>
/// The read is made twice. The first resolves no value and decides the bounds the result fits in, counting every value it
/// may release at its full bound; the second resolves exactly the values those bounds disclose, so the payload read audits
/// only what the result discloses. A result that does not fit even at its smallest bound is refused, never sent over budget.
/// </remarks>
internal sealed class WorkflowReadRunTool(
    IAgentToolAuthorizer authorizer,
    IServiceProvider services) : IAgentTool, IAgentToolAuthorization
{
    // Resolved only when the tool is used, so listing the installed tools never builds the Runtime read path.
    private IEnumerable<IWorkflowRunInspection> inspections => services.GetServices<IWorkflowRunInspection>();

    public const string ToolName = "workflow_read_run";
    internal const string ResultType = WorkflowRunContext.RunKind;
    /// <summary>The result's hard bound on its JSON text, well under the host's 32 KiB per-attachment budget (#3710).</summary>
    internal const int ResultBudget = 24 * 1024;
    internal const int TokenLimit = 1024;
    internal const int OutcomeLimit = 16;
    internal const string TruncationMarker = " … [truncated]";
    private static readonly JsonSerializerOptions JsonOptions = WorkflowRunContext.JsonOptions;

    /// <summary>
    /// The bounds tried in order until the result fits the budget: incidents, activity executions, values, bookmarks,
    /// characters per value or view, characters per incident message. The last discloses identities and counts only.
    /// </summary>
    internal static readonly Tier[] Tiers =
    [
        new(50, 50, 32, 50, 4096, 2048), new(25, 25, 16, 25, 2048, 1024), new(10, 10, 8, 10, 1024, 512), new(5, 5, 4, 5, 512, 256),
        new(2, 2, 2, 2, 256, 128), new(1, 1, 1, 1, 0, 64), new(0, 0, 0, 0, 0, 0)
    ];

    internal sealed record Tier(int Incidents, int Activities, int Values, int Bookmarks, int ValueChars, int MessageChars);

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
            return new(false, [new("weaver.workflow.resource_required", "One exact workflow Run is this tool's only target.")]);
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
        { return Failure("weaver.workflow.run_arguments_invalid", "The arguments must be JSON.", 400); }
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
            return Failure("weaver.workflow.run_context_required",
                "One admitted workflow Run context is required, and workflowExecutionId must name one of them; nothing was read.", 409);
        var inspection = WorkflowRunContext.Inspection(inspections);
        // First read: no value resolved. It decides the bounds, counting each value it may release at its full bound.
        var read = await WorkflowRunContext.ReadAsync(authorizer, inspection, invocation.ActorId, invocation.TenantId, bound, cancellationToken, pageSize, token);
        if (Refusal(read.Outcome) is { } refused) return refused;
        var tier = Tiers.FirstOrDefault(item => Result(read.Run!, bound.ActivityExecutionId, pageSize, item, estimate: true).GetRawText().Length <= ResultBudget);
        if (tier is null) return Failure(WorkflowRunContext.TooLargeCode, WorkflowRunContext.TooLargeMessage, 413);
        // Second read: exactly the values those bounds disclose are resolved, so the payload read audits only what is disclosed.
        var resolve = Disclosable(read.Run!, tier);
        if (resolve.Count > 0)
        {
            read = await WorkflowRunContext.ReadAsync(authorizer, inspection, invocation.ActorId, invocation.TenantId, bound, cancellationToken, pageSize, token, resolve);
            if (Refusal(read.Outcome) is { } changed) return changed;
        }
        var data = Result(read.Run!, bound.ActivityExecutionId, pageSize, tier, estimate: false);
        // The hard guard: never sent over budget (a Run that grew between the two reads is refused rather than cut).
        if (data.GetRawText().Length > ResultBudget) return Failure(WorkflowRunContext.TooLargeCode, WorkflowRunContext.TooLargeMessage, 413);
        return AgentResult<AgentToolPreparation>.Success(new(WorkflowRunContext.Target(read.Run!.Instance.WorkflowExecutionId, bound.ActivityExecutionId), data,
            AgentRisk.ReadOnly, new(ResultType, "1", data), RequiresApproval: false));
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the exact workflow Run through the workflow runtime; nothing was changed.", new(ResultType, "1", invocation.Arguments))));

    /// <summary>
    /// Whether a retained result discloses more than the actor's current run access covers: incident details read under value
    /// inspection, or values released under payload resolution. A result whose disclosure cannot be read is treated as exceeding.
    /// </summary>
    internal static bool Exceeds(AgentPayload? payload, WorkflowRunInspectionDetails current)
    {
        if (payload is null) return false;
        var data = payload.Data;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("canInspectValues", out var inspected)
            || inspected.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return true;
        if (inspected.GetBoolean() && !current.CanInspectValues) return true;
        if (!data.TryGetProperty("selectedActivity", out var selected) || selected.ValueKind != JsonValueKind.Object
            || !selected.TryGetProperty("values", out var values)) return false;
        if (values.ValueKind != JsonValueKind.Array) return true;
        // A value whose disclosure is not a readable string counts as released (fails closed), never as an exception.
        var released = values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("disclosure", out var disclosure) || disclosure.ValueKind != JsonValueKind.String
            || disclosure.GetString() != "withheld" && disclosure.GetString() != "unavailable" && disclosure.GetString() != "resolvable");
        return released && !(current.CanInspectValues && current.CanResolveValues);
    }

    private static AgentResult<AgentToolPreparation>? Refusal(WorkflowRunContext.Outcome outcome) => outcome switch
    {
        WorkflowRunContext.Outcome.Denied => Failure("weaver.workflow.permission_denied", WorkflowRunContext.DeniedMessage, 403),
        WorkflowRunContext.Outcome.NotFound => Failure(WorkflowRunContext.NotFoundCode, WorkflowRunContext.NotFoundMessage, 404),
        WorkflowRunContext.Outcome.ActivityNotFound => Failure(WorkflowRunContext.ActivityNotFoundCode, WorkflowRunContext.ActivityNotFoundMessage, 404),
        WorkflowRunContext.Outcome.Unavailable => Failure(WorkflowRunContext.UnavailableCode, WorkflowRunContext.UnavailableMessage, 503),
        _ => null
    };

    // The resolvable values the tier discloses with a value: within its value count, and only when values are shown at all.
    private static IReadOnlySet<string> Disclosable(WorkflowRunInspectionDetails run, Tier tier) =>
        tier.ValueChars == 0 || run.Selection?.Execution is not { } execution ? new HashSet<string>()
            : execution.Values.Take(tier.Values).Where(value => value.Disclosure == WorkflowRunValueDisclosure.Resolvable).Select(value => value.EvidenceId)
                .ToHashSet(StringComparer.Ordinal);

    private static JsonElement Result(WorkflowRunInspectionDetails run, string? activityExecutionId, int pageSize, Tier tier, bool estimate)
    {
        var instance = run.Instance;
        var unavailable = new List<object>();
        if (!run.CanInspectValues && run.Incidents.Count > 0)
            unavailable.Add(new { data = "incident messages", reason = "notPermitted", explanation = WorkflowRunContext.Explain(WorkflowRunUnavailableReason.NotPermitted) });
        if (run.CanInspectValues && run.Incidents.Any(incident => incident.SensitiveInputs))
            unavailable.Add(new { data = "incident messages that could echo sensitive or uninspectable data", reason = "withheld",
                explanation = "withheld because the text could echo a sensitive input, another Run's data or an arbitrary handler's exception text" });
        if (run.InspectionGranularity != "activity-level")
            unavailable.Add(new { data = "exact per-activity evidence", reason = "notCaptured",
                explanation = $"This Run executed under the {run.PersistenceMode} persistence mode, so its activity evidence is folded to commit boundaries ({run.InspectionGranularity})." });
        object? selected = null;
        if (activityExecutionId is not null)
        {
            if (run.Selection is { Execution: { } execution })
            {
                var values = execution.Values.Take(tier.Values).Select(value => Value(value, tier.ValueChars, estimate, unavailable)).ToArray();
                var state = execution.StateView;
                if (state.Withheld)
                    unavailable.Add(new { data = "selected activity execution state view", reason = "withheld",
                        explanation = "withheld because the activity may have a sensitive input that its state could echo" });
                else if (!state.Available && state.UnavailableReason is { } stateReason)
                    unavailable.Add(new { data = "selected activity execution state view", reason = WorkflowRunContext.Reason(stateReason), explanation = WorkflowRunContext.Explain(stateReason) });
                var view = state.View is { } projection && Serialized(projection) <= tier.ValueChars ? (JsonElement?)projection : null;
                selected = new
                {
                    activityExecutionId, summary = Summary(execution.Summary), parentActivityExecutionId = execution.ParentActivityExecutionId,
                    schedulingActivityExecutionId = execution.SchedulingActivityExecutionId, attemptNumber = execution.AttemptNumber,
                    bookmarks = execution.Bookmarks.Take(tier.Bookmarks).Select(bookmark => new
                    {
                        bookmarkId = bookmark.BookmarkId, stimulusType = bookmark.StimulusType, createdAt = bookmark.CreatedAt, expiresAt = bookmark.ExpiresAt
                    }).ToArray(),
                    bookmarkCount = execution.Bookmarks.Count, bookmarksTruncated = execution.Bookmarks.Count > tier.Bookmarks,
                    incidents = execution.Incidents.Take(tier.Incidents).Select(incident => Incident(incident, tier.MessageChars)).ToArray(),
                    incidentsTruncated = execution.Incidents.Count > tier.Incidents,
                    values, valueCount = execution.Values.Count, valuesTruncated = execution.Values.Count > values.Length,
                    stateView = new
                    {
                        available = state.Available, withheld = state.Withheld,
                        unavailableReason = state.UnavailableReason is { } reason ? WorkflowRunContext.Reason(reason) : null,
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
        var activities = Math.Min(tier.Activities, pageSize);
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
            canInspectValues = run.CanInspectValues, canResolveValues = run.CanResolveValues,
            persistenceMode = run.PersistenceMode, inspectionGranularity = run.InspectionGranularity,
            incidents = run.Incidents.Take(tier.Incidents).Select(incident => Incident(incident, tier.MessageChars)).ToArray(),
            incidentsTruncated = run.Incidents.Count > tier.Incidents,
            activities = new
            {
                items = run.Activities.Items.Take(activities).Select(Summary).ToArray(), pageSize,
                itemsTruncated = run.Activities.Items.Count > activities,
                totalCount = run.Activities.TotalCount, nextContinuationToken = run.Activities.NextContinuationToken
            },
            selectedActivity = selected,
            unavailable,
            runControls = WorkflowRunContext.RunControls
        }, JsonOptions);
    }

    private static object Incident(WorkflowRunInspectionIncident incident, int limit)
    {
        var (message, truncated) = Bounded(incident.Message, limit);
        return new
        {
            incidentId = incident.IncidentId, activityExecutionId = incident.ActivityExecutionId, executableNodeId = incident.ExecutableNodeId,
            severity = incident.Severity, status = incident.Status, failureType = Bounded(incident.FailureType, 256).Text, message, messageTruncated = truncated,
            messageRedacted = incident.MessageRedacted, sensitiveInputs = incident.SensitiveInputs, isBlocking = incident.IsBlocking,
            createdAt = incident.CreatedAt, resolvedAt = incident.ResolvedAt
        };
    }

    // A prefix whose JSON-escaped text fits the bound, marked as truncated; nothing at all when the bound is zero.
    private static (string? Text, bool Truncated) Bounded(string text, int limit)
    {
        if (JsonSerializer.Serialize(text, JsonOptions).Length - 2 <= limit) return (text, false);
        if (limit <= TruncationMarker.Length) return (null, true);
        var length = Math.Min(text.Length, limit - TruncationMarker.Length);
        string prefix;
        do
        {
            // Never split a surrogate pair.
            if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
            prefix = text[..length] + TruncationMarker;
            length -= Math.Max(1, length / 8);
        }
        while (length > 0 && JsonSerializer.Serialize(prefix, JsonOptions).Length - 2 > limit);
        return JsonSerializer.Serialize(prefix, JsonOptions).Length - 2 <= limit ? (prefix, true) : (null, true);
    }

    private static int Serialized(JsonElement value) => JsonSerializer.Serialize(value, JsonOptions).Length;

    private static object Summary(WorkflowRunInspectionActivitySummary activity) => new
    {
        activityExecutionId = activity.ActivityExecutionId, executableNodeId = activity.ExecutableNodeId, authoredActivityId = activity.AuthoredActivityId,
        activityType = activity.ActivityType, activityTypeVersion = activity.ActivityTypeVersion, status = activity.Status, subStatus = activity.SubStatus,
        executionSequence = activity.ExecutionSequence, scheduledAt = activity.ScheduledAt, startedAt = activity.StartedAt, completedAt = activity.CompletedAt,
        outcomes = activity.OutcomeNames.Take(OutcomeLimit).ToArray(), outcomesTruncated = activity.OutcomeNames.Count > OutcomeLimit,
        bookmarkCount = activity.BookmarkCount, incidentCount = activity.IncidentCount, valueCount = activity.ValueSnapshotCount
    };

    // A value as its evidence says: a released value (within the bound, otherwise a marked prefix of its JSON text), a
    // withheld one by its marker alone, an unavailable one by its reason. Estimating, a resolvable value counts at its full bound.
    private static object Value(WorkflowRunInspectionValue value, int limit, bool estimate, List<object> unavailable)
    {
        var disclosure = value.Disclosure switch
        {
            WorkflowRunValueDisclosure.Resolved => "resolved",
            WorkflowRunValueDisclosure.Resolvable => "resolvable",
            WorkflowRunValueDisclosure.Unavailable => "unavailable",
            _ => "withheld"
        };
        object? released = null;
        var truncated = false;
        // Measured as the result itself is serialized (escaping included), so the estimate is an exact upper bound: a value
        // shown whole is at most the bound when serialized, and a prefix is cut until its serialized text fits the bound.
        if (value.Disclosure == WorkflowRunValueDisclosure.Resolved && value.Value is { } payload && limit > 0)
        {
            if (Serialized(payload) <= limit) released = payload;
            else (released, truncated) = Bounded(payload.GetRawText(), limit);
        }
        else if (estimate && value.Disclosure == WorkflowRunValueDisclosure.Resolvable && limit > 0)
            released = new string('x', limit);
        var label = value.Subject + " '" + (value.InputKey ?? value.Name) + "'";
        if (value.Disclosure == WorkflowRunValueDisclosure.Withheld)
            unavailable.Add(new { data = label, reason = "withheld", explanation = "withheld by execution evidence (" + (value.WithheldKind ?? "Sensitive") + "); it is never shown" });
        else if (value.UnavailableReason is { } reason)
            unavailable.Add(new { data = label, reason = WorkflowRunContext.Reason(reason), explanation = WorkflowRunContext.Explain(reason) });
        return new
        {
            evidenceId = value.EvidenceId, name = value.Name, subject = value.Subject, inputKey = value.InputKey, captureMode = value.CaptureMode,
            captureState = value.CaptureState, accessState = value.AccessState, sensitive = value.IsSensitive, capturedAt = value.CapturedAt,
            captureReason = Bounded(value.CaptureReason, 256).Text, typeKind = value.TypeKind, typeId = value.TypeId, disclosure, withheldKind = value.WithheldKind,
            secretReferenceName = value.SecretReferenceName,
            unavailableReason = value.UnavailableReason is { } why ? WorkflowRunContext.Reason(why) : null,
            value = released, valueTruncated = truncated,
            valueOmitted = value.Disclosure == WorkflowRunValueDisclosure.Resolved && released is null
        };
    }

    private static AgentResult<AgentToolPreparation> Invalid() => Failure("weaver.workflow.run_arguments_invalid",
        "Only workflowExecutionId (a Run identity), activityPageSize (1 to 50) and activityContinuationToken are accepted.", 400);

    private static AgentResult<AgentToolPreparation> Failure(string code, string message, int status) =>
        AgentResult<AgentToolPreparation>.Failure(code, message, status);
}
