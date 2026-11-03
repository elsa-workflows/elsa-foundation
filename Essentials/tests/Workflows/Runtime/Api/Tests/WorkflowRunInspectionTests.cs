using System.Text.Json;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Spec 1193 slice G (D4, FR-029): the exact Run read for other domains through Workflows Runtime Core, over the services the
// run endpoints use, with their per-run authority and execution-evidence redaction, and unavailable data by reason.
public sealed class WorkflowRunInspectionTests
{
    private const string RunId = "wfexec-1";
    private const string ActivityId = "actexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReadAt = Now.AddMinutes(5);
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspections = new();
    private readonly AuditSink _audit = new();

    [Fact]
    public async Task Reads_the_instance_incidents_and_activity_page_the_run_endpoint_returns()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var run = await Create(details).GetAsync(new(RunId, ActivityPageSize: 500));

        Assert.NotNull(run);
        // The page size is clamped to the read's bound before the endpoint's service is asked.
        Assert.Equal(new GetWorkflowInstance(RunId, WorkflowRunInspectionRequest.MaxActivityPageSize, null), details.Requests.Single());
        Assert.Equal((RunId, "artifact-1", "sha256:test", "Faulted", true), (run.Instance.WorkflowExecutionId, run.Instance.ArtifactId, run.Instance.ArtifactHash,
            run.Instance.Status, run.Instance.IsTerminal));
        var incident = Assert.Single(run.Incidents);
        Assert.Equal(("incident-1", ActivityId, "The workflow faulted.", false, true), (incident.IncidentId, incident.ActivityExecutionId, incident.Message,
            incident.MessageRedacted, incident.IsBlocking));
        Assert.Equal([ActivityId], run.Activities.Items.Select(item => item.ActivityExecutionId));
        Assert.Equal((1L, "next-page"), (run.Activities.TotalCount, run.Activities.NextContinuationToken));
        Assert.Equal(("Durable", "activity-level", true, ReadAt), (run.PersistenceMode, run.InspectionGranularity, run.CanInspectValues, run.ReadAt));
        Assert.Null(run.Selection);
    }

    [Fact]
    public async Task A_run_still_executing_reads_as_not_terminal_at_its_read_time()
    {
        var run = await Create(await SeedAsync(WorkflowExecutionStatus.Running)).GetAsync(new(RunId));
        Assert.Equal(("Running", false, ReadAt), (run!.Instance.Status, run.Instance.IsTerminal, run.ReadAt));
    }

    [Fact]
    public async Task An_unknown_or_hidden_run_reads_as_absent()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        Assert.Null(await Create(details).GetAsync(new("other-run")));
        Assert.Null(await Create(details).GetAsync(new(" ")));
        Assert.Null(await Create(new Details(null)).GetAsync(new(RunId)));
    }

    [Fact]
    public async Task Withheld_and_sensitive_values_stay_withheld_and_a_captured_value_is_released_only_by_the_audited_payload_read()
    {
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted,
            Withheld("token"), Sensitive("password"), Captured("text", "hello"), Captured("pin", Sentinel, sensitive: true));
        var inspection = Create(details);

        var described = (await inspection.GetAsync(new(RunId, ActivityId)))!.Selection!.Execution!;
        Assert.Empty(_audit.Records);
        Assert.Equal(WorkflowRunValueDisclosure.Resolvable, Value(described, "text").Disclosure);

        var read = (await inspection.GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!;
        var token = Value(read, "token");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, "SecretReference", "payments.api-key", true, (JsonElement?)null),
            (token.Disclosure, token.WithheldKind, token.SecretReferenceName, token.IsSensitive, token.Value));
        var password = Value(read, "password");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, (WorkflowRunUnavailableReason?)null, (JsonElement?)null), (password.Disclosure, password.UnavailableReason, password.Value));
        // A sensitive value a capture policy did capture is still never released by this read, nor resolved.
        var pin = Value(read, "pin");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, true, (JsonElement?)null), (pin.Disclosure, pin.IsSensitive, pin.Value));
        var text = Value(read, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Resolved, "hello"), (text.Disclosure, text.Value!.Value.GetString()));
        // Only the resolvable value was read through the payload boundary, and that read was audited.
        Assert.Equal([("resolved", text.EvidenceId)], _audit.Records.Select(record => (record.Outcome, record.EvidenceId)));
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(read), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, "redacted")]
    [InlineData(true, false, "resolutionPermissionRequired")]
    public async Task A_value_the_caller_may_not_inspect_or_resolve_is_not_permitted(bool canInspect, bool canResolve, string accessState)
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, canInspect: canInspect, canResolve: canResolve, snapshots: [Captured("text", "hello")]);
        var text = Value((await Create(details).GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted, accessState, (JsonElement?)null),
            (text.Disclosure, text.UnavailableReason, text.AccessState, text.Value));
    }

    [Fact]
    public async Task Without_value_inspection_incident_messages_are_the_endpoints_redaction()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, canInspect: false);
        var run = (await Create(details).GetAsync(new(RunId)))!;
        Assert.False(run.CanInspectValues);
        var incident = Assert.Single(run.Incidents);
        Assert.Equal(("Incident details are redacted.", true), (incident.Message, incident.MessageRedacted));
    }

    [Fact]
    public async Task A_value_the_runtime_did_not_capture_or_a_backend_without_payload_reads_is_reported_by_reason()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, snapshots: [NotCaptured("skipped"), Captured("text", "hello")]);
        var read = (await Create(details, payloads: false).GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!;
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured), (Value(read, "skipped").Disclosure, Value(read, "skipped").UnavailableReason));
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.Unsupported), (Value(read, "text").Disclosure, Value(read, "text").UnavailableReason));
    }

    [Fact]
    public async Task A_selection_is_unsupported_without_activity_inspection_pruned_when_recorded_but_gone_and_absent_when_unknown()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        Assert.Equal(new WorkflowRunInspectionSelection(ActivityId, null, WorkflowRunUnavailableReason.Unsupported),
            (await Create(details, activities: false).GetAsync(new(RunId, ActivityId)))!.Selection);

        // The Run's page and incident still name the activity execution after its inspection record was pruned.
        Assert.Equal(new WorkflowRunInspectionSelection(ActivityId, null, WorkflowRunUnavailableReason.Pruned),
            (await Create(details, pruned: true).GetAsync(new(RunId, ActivityId)))!.Selection);
        Assert.Equal(new WorkflowRunInspectionSelection("never-recorded", null, null), (await Create(details).GetAsync(new(RunId, "never-recorded")))!.Selection);
    }

    [Theory]
    [InlineData("none", false, WorkflowRunUnavailableReason.Unsupported)]
    [InlineData("missing", false, WorkflowRunUnavailableReason.Unsupported)]
    [InlineData(ActivityStateView.StateCleared, false, WorkflowRunUnavailableReason.NotCaptured)]
    [InlineData(ActivityStateView.NoProjector, false, WorkflowRunUnavailableReason.Unsupported)]
    [InlineData("available", true, null)]
    public async Task The_state_view_is_the_endpoints_projection_or_unavailable_by_reason(string state, bool available, WorkflowRunUnavailableReason? reason)
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var view = state switch
        {
            "missing" => null,
            "available" => new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", true, null, "test.view", 1, true, JsonSerializer.SerializeToElement(new { step = 2 })),
            _ => new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", false, state, null, null, true, null)
        };
        var stateView = (await Create(details, stateViews: new StateViews(view), stateViewsSupported: state != "none").GetAsync(new(RunId, ActivityId)))!.Selection!.Execution!.StateView;
        Assert.Equal((available, reason), (stateView.Available, stateView.UnavailableReason));
        if (available) Assert.Equal(("test.view", 2), (stateView.ViewKind, stateView.View!.Value.GetProperty("step").GetInt32()));
    }

    [Fact]
    public async Task Only_the_named_evidence_is_resolved_and_audited_up_to_the_read_bound()
    {
        var snapshots = Enumerable.Range(0, 40).Select(index => Captured("value-" + index, "v" + index)).ToArray();
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted, snapshots);
        var inspection = Create(details);
        var described = (await inspection.GetAsync(new(RunId, ActivityId)))!.Selection!.Execution!;
        var named = described.Values.Take(3).Select(value => value.EvidenceId).ToHashSet();

        var read = (await inspection.GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: named)))!.Selection!.Execution!;
        Assert.Equal(named, read.Values.Where(value => value.Disclosure == WorkflowRunValueDisclosure.Resolved).Select(value => value.EvidenceId).ToHashSet());
        Assert.Equal(named, _audit.Records.Select(record => record.EvidenceId).ToHashSet());

        _audit.Records.Clear();
        var all = (await inspection.GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!;
        Assert.Equal(32, all.Values.Count(value => value.Disclosure == WorkflowRunValueDisclosure.Resolved));
        Assert.Equal(8, all.Values.Count(value => value.Disclosure == WorkflowRunValueDisclosure.Resolvable));
        Assert.Equal(32, _audit.Records.Count);
    }

    [Fact]
    public async Task A_denied_payload_read_is_not_permitted_and_a_vanished_one_is_pruned()
    {
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted, Captured("text", "hello"));
        // The payload read denies a caller it cannot attribute, whatever the activity read said.
        var denied = Value((await Create(details, payloadAccess: new(true, true, auditSubject: "")).GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!
            .Selection!.Execution!, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted), (denied.Disclosure, denied.UnavailableReason));
        var vanished = Value((await Create(details, payloadPruned: true).GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.Pruned), (vanished.Disclosure, vanished.UnavailableReason));
    }

    [Theory]
    [InlineData("metadataOnly")]
    [InlineData("captureFailed")]
    [InlineData("unavailable")]
    public async Task A_value_without_a_captured_payload_is_not_captured(string state)
    {
        var snapshot = state switch
        {
            "metadataOnly" => ActivityExecutionInspectionValueSnapshot.FromDecision("text", ActivityExecutionInspectionValueSubject.ActivityInput,
                new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.MetadataOnly, "Runtime diagnostics policy captured metadata only."),
                new RuntimeValueTypeDescriptor("alias", "String", null), Now, null, false, inputKey: "text"),
            "captureFailed" => ActivityExecutionInspectionValueSnapshot.FromDecision("text", ActivityExecutionInspectionValueSubject.ActivityInput,
                new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.DiagnosticSnapshot, "Diagnostic snapshot captured by runtime diagnostics policy."),
                new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement("x"), false, inputKey: "text",
                failure: new RuntimeInputEvaluationFailure("expression.failed", "The expression failed.")),
            _ => ActivityExecutionInspectionValueSnapshot.FromDecision("text", ActivityExecutionInspectionValueSubject.ActivityInput,
                new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.DiagnosticSnapshot, "Diagnostic snapshot captured by runtime diagnostics policy."),
                new RuntimeValueTypeDescriptor("alias", "String", null), Now, null, false, inputKey: "text")
        };
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted, snapshot);
        var text = Value((await Create(details).GetAsync(new(RunId, ActivityId, ResolveEvidenceIds: All)))!.Selection!.Execution!, "text");
        Assert.Equal((state, WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured), (text.CaptureState, text.Disclosure, text.UnavailableReason));
        Assert.Empty(_audit.Records);
    }

    [Fact]
    public async Task The_payload_grant_is_reported_and_an_unset_disclosure_fails_closed()
    {
        Assert.True((await Create(await SeedAsync(WorkflowExecutionStatus.Faulted)).GetAsync(new(RunId)))!.CanResolveValues);
        Assert.False((await Create(await SeedAsync(WorkflowExecutionStatus.Faulted, canResolve: false)).GetAsync(new(RunId)))!.CanResolveValues);
        Assert.False((await Create(await SeedAsync(WorkflowExecutionStatus.Faulted, canInspect: false)).GetAsync(new(RunId)))!.CanResolveValues);
        Assert.Equal(WorkflowRunValueDisclosure.Withheld, default(WorkflowRunValueDisclosure));
    }

    [Fact]
    public async Task An_activity_with_a_sensitive_input_has_its_incident_messages_and_state_view_withheld_even_with_value_inspection()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var run = (await Create(details, sensitiveNode: true, stateViews: new StateViews(new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", true, null,
            "test.view", 1, true, JsonSerializer.SerializeToElement(new { echoed = Sentinel })))).GetAsync(new(RunId, ActivityId)))!;
        var incident = Assert.Single(run.Incidents);
        Assert.Equal((true, true), (incident.SensitiveInputs, incident.MessageRedacted));
        Assert.NotEqual("The workflow faulted.", incident.Message);
        var state = run.Selection!.Execution!.StateView;
        Assert.Equal((false, true, (JsonElement?)null), (state.Available, state.Withheld, state.View));
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run), StringComparison.Ordinal);
        // Without the Executable the read cannot tell which activities are sensitive, so it withholds every activity's message.
        Assert.True(Assert.Single((await Create(details, executable: false).GetAsync(new(RunId)))!.Incidents).MessageRedacted);
        // An activity without a sensitive input keeps its message for a caller with value inspection.
        Assert.Equal("The workflow faulted.", Assert.Single((await Create(details).GetAsync(new(RunId)))!.Incidents).Message);
    }

    [Fact]
    public async Task A_run_level_incident_of_an_activity_with_a_sensitive_input_is_withheld_wherever_it_is_listed()
    {
        // The Executable says node-1 has no sensitive binding, but the execution's own evidence holds a sensitive input.
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted, Sensitive("password"));
        foreach (var selected in new[] { ActivityId, null })
        {
            var run = (await Create(details).GetAsync(new(RunId, selected)))!;
            var incident = Assert.Single(run.Incidents);
            Assert.Equal((true, true, "Incident details are withheld because their text could echo a sensitive input or data this read cannot inspect."), (incident.SensitiveInputs, incident.MessageRedacted, incident.Message));
            if (selected is not null) Assert.True(run.Selection!.Execution!.StateView.Withheld);
        }
    }

    [Fact]
    public async Task An_incident_whose_non_sensitivity_cannot_be_established_is_withheld()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        IncidentStateView Incident(string? activity, string? node) => new("incident-x", RunId, activity, node, "Error", "Active", null, "workflow.fault",
            "Fault summary: " + Sentinel, Now, null, true, new Dictionary<string, string>());
        async Task<WorkflowRunInspectionIncident> Read(IncidentStateView incident, bool activities = true) =>
            Assert.Single((await Create(new Details(details.View! with { Incidents = [incident] }), activities: activities).GetAsync(new(RunId)))!.Incidents);

        // A workflow-level incident (no activity execution, no node), as the poisoned-work observer records one.
        Assert.True((await Read(Incident(null, null))).MessageRedacted);
        // A node it names but no activity execution whose evidence could be read.
        Assert.True((await Read(Incident(null, "node-1"))).MessageRedacted);
        // An activity execution but no node: the node is the execution's own, read with its evidence, so it is established...
        Assert.False((await Read(Incident(ActivityId, null))).MessageRedacted);
        // ...unless that execution cannot be read.
        Assert.True((await Read(Incident(ActivityId, null), activities: false)).MessageRedacted);
        // An activity execution that cannot be read on this backend.
        Assert.True((await Read(Incident(ActivityId, "node-1"), activities: false)).MessageRedacted);
        // Established from both sources: kept.
        Assert.Equal("Fault summary: " + Sentinel, (await Read(Incident(ActivityId, "node-1"))).Message);
    }

    [Theory]
    [InlineData("sensitive", true)]
    [InlineData("plain", false)]
    [InlineData("unreadable", true)]
    [InlineData("partial", true)]
    [InlineData("blank", true)]
    [InlineData("completed-sensitive", true)]
    [InlineData("completed-plain", false)]
    [InlineData("completed-blank", true)]
    [InlineData("notifying-sensitive", true)]
    [InlineData("notifying-plain", false)]
    [InlineData("notifying-blank", true)]
    [InlineData("control-sensitive", true)]
    [InlineData("control-plain", true)]
    [InlineData("control-thrown-before-key", true)]
    [InlineData("poisoned-work", true)]
    [InlineData("control-blank", true)]
    public async Task An_incident_message_is_withheld_unless_its_causal_sources_are_established_non_sensitive(string child, bool withheld)
    {
        const string ChildId = "actexec-child";
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        if (child is "sensitive" or "plain" or "completed-sensitive" or "completed-plain" or "notifying-sensitive" or "notifying-plain" or "control-sensitive" or "control-plain")
            await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ChildId, RunId, "node-child", "authored-child", "Test.Child", "1.0.0",
                ActivityExecutionStatus.Faulted, null, 2, Now, Now, null, "checkpoint-2", "checkpoint-2", Now, ActivitySchedulingProvenance.Empty, [], [], [],
                child.EndsWith("sensitive", StringComparison.Ordinal) ? [Sensitive("password")] : [Captured("text", "hello")], new Dictionary<string, string>()));
        var causal = child switch
        {
            "partial" => new Dictionary<string, string> { [RuntimeMetadataKeys.CausalIncidentId] = "incident-0", [RuntimeMetadataKeys.CausalExecutableNodeId] = "node-child" },
            // A present but blank causal key, with no incident id or node beside it, is not "no causation".
            "blank" => new Dictionary<string, string> { [RuntimeMetadataKeys.CausalActivityExecutionId] = " " },
            // A structural parent (a Flowchart) records the child it completed from, as the packaged runtime does.
            "completed-sensitive" or "completed-plain" => new Dictionary<string, string> { [RuntimeMetadataKeys.CompletedChildActivityExecutionId] = ChildId },
            "completed-blank" => new Dictionary<string, string> { [RuntimeMetadataKeys.CompletedChildActivityExecutionId] = "" },
            // A parent faulting while handling a child's notification records that child, whose payload reached the handler.
            "notifying-sensitive" or "notifying-plain" => new Dictionary<string, string> { [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = ChildId },
            "notifying-blank" => new Dictionary<string, string> { [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = "" },
            // A child-control consumer's fault, on every arm, records the calling child; the batches came from another Run.
            "control-sensitive" or "control-plain" => new Dictionary<string, string> { [RuntimeMetadataKeys.ChildControlActivityExecutionId] = ChildId },
            "control-blank" => new Dictionary<string, string> { [RuntimeMetadataKeys.ChildControlActivityExecutionId] = "" },
            // A thrown consumer fault recorded before the key existed carries only its sub-status.
            "control-thrown-before-key" => new Dictionary<string, string> { [RuntimeMetadataKeys.FaultSubStatus] = "ChildControlFaulted" },
            "poisoned-work" => new Dictionary<string, string> { [RuntimeMetadataKeys.CheckpointReason] = "SchedulerWorkPoisoned" },
            _ => new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CausalIncidentId] = "incident-0", [RuntimeMetadataKeys.CausalActivityExecutionId] = ChildId,
                [RuntimeMetadataKeys.CausalExecutableNodeId] = "node-child", [RuntimeMetadataKeys.CausationKind] = "child-fault"
            }
        };
        // The parent's own projection records the propagated incident, as the fault recorder leaves it.
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [],
            [new ActivityExecutionIncidentSummary("incident-1", IncidentSeverity.Error, IncidentStatus.Blocking, null, "workflow.fault", "Child failed: " + Sentinel, Now,
                null, true, causal, ActivityExecutionIncidentCausation.From(causal))],
            [], new Dictionary<string, string>()));
        var view = details.View! with
        {
            Incidents = [details.View!.Incidents.Single() with { Message = "Child failed: " + Sentinel, Metadata = causal }]
        };

        var run = (await Create(new Details(view)).GetAsync(new(RunId, ActivityId)))!;

        var top = Assert.Single(run.Incidents);
        var selected = Assert.Single(run.Selection!.Execution!.Incidents);
        Assert.Equal((withheld, withheld), (top.MessageRedacted, selected.MessageRedacted));
        if (withheld) Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run), StringComparison.Ordinal);
        else Assert.Equal(("Child failed: " + Sentinel, "Child failed: " + Sentinel), (top.Message, selected.Message));
    }

    [Theory]
    [InlineData(true, true, RuntimeMetadataKeys.CompletedChildActivityExecutionId)]
    [InlineData(false, false, RuntimeMetadataKeys.CompletedChildActivityExecutionId)]
    [InlineData(true, true, RuntimeMetadataKeys.CausalActivityExecutionId)]
    [InlineData(true, true, RuntimeMetadataKeys.NotifyingChildActivityExecutionId)]
    public async Task A_propagated_chain_is_followed_through_the_childs_own_incident(bool grandchildSensitive, bool withheld, string key)
    {
        // A nested BPMN process copies its child's fault message into its own, which its parent copies in turn.
        const string ChildId = "actexec-child", GrandchildId = "actexec-grandchild";
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ChildId, RunId, "node-child", "authored-child", "Test.Child", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 2, Now, Now, null, "checkpoint-2", "checkpoint-2", Now, ActivitySchedulingProvenance.Empty, [], [], [],
            [Captured("text", "hello")], new Dictionary<string, string>()));
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(GrandchildId, RunId, "node-grandchild", "authored-grandchild", "Test.Leaf", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 3, Now, Now, null, "checkpoint-3", "checkpoint-3", Now, ActivitySchedulingProvenance.Empty, [], [], [],
            grandchildSensitive ? [Sensitive("password")] : [Captured("text", "hello")], new Dictionary<string, string>()));
        var parentCausal = new Dictionary<string, string> { [key] = ChildId };
        var childCausal = new Dictionary<string, string> { [key] = GrandchildId };
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [],
            [new ActivityExecutionIncidentSummary("incident-1", IncidentSeverity.Error, IncidentStatus.Blocking, null, "workflow.fault", "Child failed: " + Sentinel, Now,
                null, true, parentCausal, ActivityExecutionIncidentCausation.From(parentCausal))],
            [], new Dictionary<string, string>()));
        var parent = details.View!.Incidents.Single() with { Message = "Child failed: " + Sentinel, Metadata = parentCausal };
        var child = parent with { IncidentId = "incident-child", ActivityExecutionId = ChildId, ExecutableNodeId = "node-child", Metadata = childCausal };
        var view = details.View! with { Incidents = [parent, child] };

        var run = (await Create(new Details(view)).GetAsync(new(RunId, ActivityId)))!;

        Assert.Equal(withheld, run.Incidents.Single(item => item.IncidentId == parent.IncidentId).MessageRedacted);
        Assert.Equal(withheld, Assert.Single(run.Selection!.Execution!.Incidents).MessageRedacted);
        if (withheld) Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cyclic_or_overlong_causal_chain_counts_as_sensitive(bool cyclic)
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        // Every activity in the chain is itself established non-sensitive: only the chain's shape withholds the message.
        var length = cyclic ? 2 : 9; // one past the default implementation's depth bound of eight
        var ids = Enumerable.Range(0, length).Select(index => index == 0 ? ActivityId : $"actexec-chain-{index}").ToArray();
        foreach (var (id, index) in ids.Select((id, index) => (id, index)).Skip(1))
            await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(id, RunId, $"node-chain-{index}", $"authored-chain-{index}", "Test.Child", "1.0.0",
                ActivityExecutionStatus.Faulted, null, index + 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [], [],
                [Captured("text", "hello")], new Dictionary<string, string>()));
        // The last link names no child, so an overlong chain ends cleanly and only the depth bound withholds it.
        Dictionary<string, string> Next(int index) => index + 1 < ids.Length || cyclic
            ? new() { [RuntimeMetadataKeys.CompletedChildActivityExecutionId] = index + 1 < ids.Length ? ids[index + 1] : ids[0] }
            : new();
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [],
            [new ActivityExecutionIncidentSummary("incident-1", IncidentSeverity.Error, IncidentStatus.Blocking, null, "workflow.fault", "Child failed: " + Sentinel, Now,
                null, true, Next(0), ActivityExecutionIncidentCausation.From(Next(0)))],
            [], new Dictionary<string, string>()));
        var template = details.View!.Incidents.Single() with { Message = "Child failed: " + Sentinel };
        var incidents = ids.Select((id, index) => template with
        {
            IncidentId = index == 0 ? template.IncidentId : $"incident-chain-{index}", ActivityExecutionId = id,
            ExecutableNodeId = index == 0 ? template.ExecutableNodeId : $"node-chain-{index}", Metadata = Next(index),
            Message = index == 0 ? template.Message : $"Link {index} failed."
        }).ToArray();

        var run = (await Create(new Details(details.View! with { Incidents = incidents })).GetAsync(new(RunId, ActivityId)))!;

        Assert.True(run.Incidents.Single(item => item.IncidentId == template.IncidentId).MessageRedacted);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(run), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thousands_of_incidents_cost_a_bounded_number_of_activity_reads()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var template = details.View!.Incidents.Single();
        // A retry loop: 2 000 node-less incidents naming the one readable execution, then 2 000 naming distinct executions.
        var incidents = Enumerable.Range(0, 2000).Select(index => template with { IncidentId = "same-" + index, ExecutableNodeId = null })
            .Concat(Enumerable.Range(0, 2000).Select(index => template with { IncidentId = "distinct-" + index, ActivityExecutionId = "retry-" + index, ExecutableNodeId = null }))
            .ToArray();
        var counting = new CountingActivities(new ActivityExecutionInspectionService(_workflows, _inspections, _access));

        var run = (await Create(new Details(details.View! with { Incidents = incidents }), activityService: counting).GetAsync(new(RunId)))!;

        Assert.InRange(counting.Reads, 1, 32);
        Assert.All(run.Incidents.Where(item => item.IncidentId.StartsWith("distinct-", StringComparison.Ordinal)), item => Assert.True(item.MessageRedacted));
        // The readable, non-sensitive execution is read once and established non-sensitive for all 2 000 of its incidents.
        Assert.All(run.Incidents.Where(item => item.IncidentId.StartsWith("same-", StringComparison.Ordinal)), item => Assert.False(item.MessageRedacted));
    }

    [Fact]
    public async Task Pruned_does_not_depend_on_the_callers_activity_page()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var narrowed = new Details(details.View! with { Activities = [] });
        Assert.Equal(WorkflowRunUnavailableReason.Pruned, (await Create(narrowed, pruned: true).GetAsync(new(RunId, ActivityId, ActivityPageSize: 1)))!.Selection!.UnavailableReason);
        var unrecorded = new Details(details.View! with { Incidents = [] });
        Assert.Null((await Create(unrecorded, pruned: true).GetAsync(new(RunId, ActivityId)))!.Selection!.UnavailableReason);
    }

    [Fact]
    public async Task The_read_is_attributed_to_its_start_before_any_store_read()
    {
        var clock = new FakeTimeProvider(ReadAt);
        var details = await SeedAsync(WorkflowExecutionStatus.Running);
        var run = await Create(new Details(details.View, () => clock.Advance(TimeSpan.FromMinutes(1))), clock: clock).GetAsync(new(RunId));
        Assert.Equal(ReadAt, run!.ReadAt);
    }

    [Fact]
    public void The_feature_registers_one_internal_sealed_run_inspection()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IWorkflowRunInspection));
        Assert.Equal("WorkflowRunInspection", descriptor.ImplementationType!.Name);
        Assert.True(descriptor.ImplementationType.IsSealed);
        Assert.False(descriptor.ImplementationType.IsPublic);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void A_second_run_inspection_registration_fails_configuration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        var error = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeApiFeature().ConfigureServices(services));
        Assert.Contains("IWorkflowRunInspection has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_second_run_inspection_registration_fails_shell_startup()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        var task = services.Where(item => item.ServiceType == typeof(IStartupTask)).Select(item => item.ImplementationInstance)
            .OfType<IStartupTask>().Single(item => item.GetType().Name == "WorkflowRunInspectionStartupTask");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IWorkflowRunInspection has multiple registrations", error.Message);
    }

    private static WorkflowRunInspectionValue Value(WorkflowRunInspectionActivityExecution execution, string name) => execution.Values.Single(item => item.Name == name);

    private Access _access = new(true, true);

    private static readonly IReadOnlySet<string> All = new AllEvidence();

    private IWorkflowRunInspection Create(Details details, bool activities = true, StateViews? stateViews = null, bool payloads = true, bool pruned = false,
        bool stateViewsSupported = true, Access? payloadAccess = null, bool payloadPruned = false, bool sensitiveNode = false, bool executable = true,
        TimeProvider? clock = null, IActivityExecutionInspectionService? activityService = null) =>
        (IWorkflowRunInspection)Activator.CreateInstance(RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowRunInspection")!,
            details,
            activityService ?? (activities ? new ActivityExecutionInspectionService(_workflows, pruned ? new InMemoryActivityExecutionInspectionStore() : _inspections, _access) : null),
            stateViewsSupported
                ? stateViews ?? new StateViews(new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", false, ActivityStateView.StateCleared, null, null, true, null))
                : null,
            payloads ? new ActivityExecutionValuePayloadReader(_workflows, payloadPruned ? new InMemoryActivityExecutionInspectionStore() : _inspections,
                payloadAccess ?? _access, _audit, new FakeTimeProvider(Now)) : null,
            clock ?? new FakeTimeProvider(ReadAt),
            _workflows, _access, null, executable ? new Executables(sensitiveNode) : null)!;

    private async Task<Details> SeedValuesAsync(WorkflowExecutionStatus status, params ActivityExecutionInspectionValueSnapshot[] values) =>
        await SeedAsync(status, true, true, values);

    private async Task<Details> SeedAsync(WorkflowExecutionStatus status, bool canInspect = true, bool canResolve = true,
        ActivityExecutionInspectionValueSnapshot[]? snapshots = null)
    {
        _access = new(canInspect, canResolve);
        var state = new WorkflowExecutionState(RunId, new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            status, null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>());
        await _workflows.SaveAsync(state);
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [], [],
            snapshots ?? [], new Dictionary<string, string>()));
        var summary = new ActivityExecutionInspectionSummaryView(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0", "Faulted", null, 1, Now, Now, null,
            "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenanceView.From(ActivitySchedulingProvenance.Empty), [], 0, 1, snapshots?.Length ?? 0,
            new Dictionary<string, string>());
        var incident = new IncidentStateView("incident-1", RunId, ActivityId, "node-1", "Error", "Active", null, "workflow.fault",
            canInspect ? "The workflow faulted." : "Incident details are redacted.", Now, null, true, new Dictionary<string, string>());
        return new(new WorkflowInstanceDetailsView(WorkflowInstanceSummaryView.From(state, 1, 1, canInspect), [summary], [incident],
            new Dictionary<string, WorkflowOutputView>(), "Durable", "activity-level", "next-page", new(false, canInspect)));
    }

    private static ActivityExecutionInspectionValueSnapshot Withheld(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Full payload captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(Sentinel), true,
            ActivityExecutionInspectionValueSnapshot.MarkWithheld(new Dictionary<string, string>(),
                WithheldValue.SecretReference(new RuntimeSecretReference("payments.api-key"), null)), inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot Sensitive(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded by default."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(Sentinel), true, inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot NotCaptured(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Runtime diagnostics policy did not capture value evidence."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, null, false, inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot Captured(string name, string value, bool sensitive = false) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.DiagnosticSnapshot, "Diagnostic snapshot captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(value), sensitive, inputKey: name);

    /// <summary>Every evidence id: the caller asks to resolve whatever the selected execution holds.</summary>
    private sealed class AllEvidence : HashSet<string>, IReadOnlySet<string>
    {
        bool IReadOnlySet<string>.Contains(string item) => true;
    }

    /// <summary>The Run's Executable: node-1, with or without a sensitive input binding.</summary>
    private sealed class Executables(bool sensitive) : IWorkflowExecutableInspection
    {
        public ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableInspectionDetails?>(new(artifactId, "sha256:test", Now, "Test.Root", "1.0.0", 2, 1,
                new("root", "root", "Test.Root", "1.0.0", null, [], [new("activities", [new("node-1", "authored-1", "Test.Activity", "1.0.0", null,
                    [new("token", "Literal", sensitive)], [], [])])], []), [], []));
    }

    private sealed class Details(WorkflowInstanceDetailsView? view, Action? onRead = null) : IWorkflowInstanceDetailsService
    {
        public List<GetWorkflowInstance> Requests { get; } = [];
        public WorkflowInstanceDetailsView? View => view;

        public Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
        {
            onRead?.Invoke();
            Requests.Add(request);
            return Task.FromResult(view?.Instance.WorkflowExecutionId == request.WorkflowExecutionId ? view : null);
        }
    }

    private sealed class CountingActivities(IActivityExecutionInspectionService inner) : IActivityExecutionInspectionService
    {
        public int Reads { get; private set; }

        public Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken)
        {
            Reads++;
            return inner.GetAsync(request, cancellationToken);
        }
    }

    private sealed class StateViews(ActivityStateView? view) : IActivityExecutionStateViewService
    {
        public Task<ActivityStateView?> GetAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken) => Task.FromResult(view);
    }

    private sealed class Access(bool canInspectSensitiveValues, bool canResolve, string auditSubject = "subject-1") : IActivityInspectionContextAsync
    {
        public string TenantScope => "tenant-a";
        public string AuditSubject => auditSubject;
        public string RequestCorrelationId => "request-1";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("structure");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectSensitiveValues);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canResolve);
    }

    private sealed class AuditSink : IActivityExecutionValuePayloadAuditSink
    {
        public List<ActivityExecutionValuePayloadAuditRecord> Records { get; } = [];

        public ValueTask RecordAsync(ActivityExecutionValuePayloadAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Duplicate : IWorkflowRunInspection
    {
        public ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowRunInspectionDetails?>(null);
    }
}
