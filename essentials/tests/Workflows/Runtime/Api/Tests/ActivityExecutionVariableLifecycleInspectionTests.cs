using System.Text.Json;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// The activity-execution read carries the variable-lifecycle records of spec 1221 (Studio #4735): write history,
/// a refused completion and superseded input evaluations. Values are value states only (spec 1229 FR-006): never
/// inline, a captured null stays captured, a sensitive value reads redacted.
/// </summary>
public sealed class ActivityExecutionVariableLifecycleInspectionTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string ActivityExecutionId = "actexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspections = new();
    private readonly InMemoryActivityExecutionStateStore _states = new();

    [Fact]
    public async Task A_lost_update_warning_names_both_writers_and_the_overwritten_value_as_a_state()
    {
        await SaveAsync(State() with
        {
            VariableWrites =
            [
                new VariableWriteRecord(
                    "scope-1", "count",
                    new VariableWriter(VariableWriteSource.Capture, "node-b", "inv-b", "att-b"),
                    VariableCommitOutcome.LostUpdateWarning,
                    baseVersion: 0, currentVersion: 1, newVersion: 2, commitOrder: 7, Now,
                    overwrittenWriter: new VariableWriter(VariableWriteSource.Capture, "node-a", "inv-a"),
                    overwrittenValue: Inline(Sentinel)) { CommitSequence = 12, FrameId = "frame-1", Digest = "sha256:abc" }
            ]
        });

        var view = await ViewAsync();
        var write = Assert.Single(view.VariableWrites!);

        Assert.Equal("LostUpdateWarning", write.Outcome);
        Assert.Equal("node-b", write.Writer.ExecutableNodeId);
        Assert.Equal("node-a", write.OverwrittenWriter!.ExecutableNodeId);
        Assert.Equal((0L, 1L, 2L), (write.BaseVersion!.Value, write.CurrentVersion, write.NewVersion));
        Assert.Equal(12, write.CommitSequence);
        Assert.Equal("frame-1", write.FrameId);
        Assert.Equal(VariableLifecycleValueStateView.Captured, write.OverwrittenValue!.State);
        Assert.True(write.OverwrittenValue.IsOperational);
        // No VariableWrite evidence was captured (the subject is off by default): the written value is omitted.
        Assert.Equal(VariableLifecycleValueStateView.Omitted, write.WrittenValue!.State);
        Assert.Equal("sha256:abc", write.WrittenValue.Digest);
        Assert.Null(write.RejectedValue);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(view), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fault_refusal_and_its_recorded_completion_carry_value_states_only()
    {
        await SaveAsync(State() with
        {
            Status = ActivityExecutionStatus.Faulted,
            SubStatus = "VariableLostUpdate",
            VariableWrites =
            [
                new VariableWriteRecord(
                    "scope-1", "count",
                    new VariableWriter(VariableWriteSource.Capture, "node-b", "inv-b"),
                    VariableCommitOutcome.LostUpdateFault,
                    baseVersion: 0, currentVersion: 1, newVersion: 1, commitOrder: 3, Now,
                    overwrittenWriter: new VariableWriter(VariableWriteSource.Capture, "node-a", "inv-a")) { RejectedValue = Inline(Sentinel) }
            ],
            RecordedCompletion = new RecordedActivityCompletion(
                new ActivityCompletion("inv-b", "att-b", Inline(Sentinel), OutcomeSelection.Named("Done"), Now, "test:1.0.0"),
                attempt: null,
                [
                    new RecordedVariableWrite("count", ValueEnvelope.Null(new ValueTypeDescriptor("Number"), ValueProtectionPolicy.InstanceInline)),
                    new RecordedVariableWrite("secret", Inline(Sentinel, sensitive: true))
                ])
        });

        var view = await ViewAsync();
        var refusal = Assert.Single(view.VariableWrites!);

        Assert.Equal("LostUpdateFault", refusal.Outcome);
        Assert.Null(refusal.WrittenValue);
        Assert.Equal(VariableLifecycleValueStateView.Captured, refusal.RejectedValue!.State);
        Assert.Equal(["Done"], view.RecordedCompletion!.OutcomeNames);
        Assert.Equal("att-b", view.RecordedCompletion.AttemptId);
        // A captured null is captured, never missing; a sensitive value is redacted.
        Assert.Equal(VariableLifecycleValueStateView.Captured, view.RecordedCompletion.VariableWrites.Single(w => w.VariableKey == "count").Value.State);
        Assert.Equal(VariableLifecycleValueStateView.Redacted, view.RecordedCompletion.VariableWrites.Single(w => w.VariableKey == "secret").Value.State);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(view), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_background_input_refresh_shows_the_superseded_evaluation_and_the_changed_variables()
    {
        var superseded = new ActivityInputSnapshot("inv-1", "contract", "bindings",
            new Dictionary<string, ValueEnvelope> { ["count"] = Inline(Sentinel), ["missing"] = ValueEnvelope.Absent(new ValueTypeDescriptor("Number"), ValueProtectionPolicy.InstanceInline) },
            Now.AddSeconds(-5));
        await SaveAsync(State() with
        {
            InputRefreshes = [new ActivityInputRefresh(superseded, 2, [new VariableInputChange("scope-1", "count", 0, 1)], Now)]
        });

        var refresh = Assert.Single((await ViewAsync()).InputRefreshes!);

        Assert.Equal((1, 2), (refresh.SupersededEvaluation, refresh.ReplacedByEvaluation));
        var change = Assert.Single(refresh.Changes);
        Assert.Equal(("count", 0L, 1L), (change.VariableKey, change.ReadVersion, change.CurrentVersion));
        Assert.Equal(VariableLifecycleValueStateView.Captured, refresh.SupersededInputs.Single(i => i.InputKey == "count").Value.State);
        Assert.Equal(VariableLifecycleValueStateView.Omitted, refresh.SupersededInputs.Single(i => i.InputKey == "missing").Value.State);
    }

    [Fact]
    public async Task A_written_value_links_its_captured_variable_write_evidence()
    {
        var evidence = ActivityExecutionInspectionValueSnapshot.FromDecision(
            "count",
            ActivityExecutionInspectionValueSubject.VariableWrite,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Full payload captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "Number", null),
            Now,
            JsonSerializer.SerializeToElement(2),
            isSensitive: false,
            phase: "scope-1",
            sequence: 2);
        await SaveAsync(State() with
        {
            VariableWrites =
            [
                new VariableWriteRecord("scope-1", "count", new VariableWriter(VariableWriteSource.Capture, "node-1", "inv-1"),
                    VariableCommitOutcome.Current, baseVersion: 1, currentVersion: 1, newVersion: 2, commitOrder: 1, Now)
            ]
        }, evidence);

        var view = await ViewAsync();
        var written = Assert.Single(view.VariableWrites!).WrittenValue!;

        Assert.Equal(VariableLifecycleValueStateView.Captured, written.State);
        Assert.Equal(Assert.Single(view.ValueSnapshots).EvidenceId, written.EvidenceId);
    }

    [Fact]
    public async Task An_accepted_override_keeps_the_state_of_the_value_it_overwrote()
    {
        await SaveAsync(State() with
        {
            VariableWrites =
            [
                new VariableWriteRecord("scope-1", "count", new VariableWriter(VariableWriteSource.AcceptedCompletion, "node-1", "inv-2"),
                    VariableCommitOutcome.AcceptedOverride, baseVersion: null, currentVersion: 1, newVersion: 2, commitOrder: 5, Now,
                    overwrittenWriter: new VariableWriter(VariableWriteSource.Capture, "node-a", "inv-a"),
                    overwrittenValue: Inline(Sentinel))
            ]
        });

        var view = await ViewAsync();
        var write = Assert.Single(view.VariableWrites!);

        Assert.Equal("AcceptedOverride", write.Outcome);
        Assert.Equal(VariableLifecycleValueStateView.Captured, write.OverwrittenValue!.State);
        Assert.True(write.OverwrittenValue.IsOperational);
        Assert.Null(write.RejectedValue);
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(view), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_value_digest_is_withheld_from_a_caller_who_may_not_inspect_sensitive_values_and_beside_a_redacted_value()
    {
        // The digest is an unsalted hash of the value: from the digest of a Boolean or a small number the value follows.
        const string digest = "sha256:b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b";
        var evidence = ActivityExecutionInspectionValueSnapshot.FromDecision(
            "approved",
            ActivityExecutionInspectionValueSubject.VariableWrite,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Full payload captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "Boolean", null),
            Now,
            JsonSerializer.SerializeToElement(true),
            isSensitive: false,
            phase: "scope-1",
            sequence: 1);
        await SaveAsync(State() with
        {
            VariableWrites =
            [
                new VariableWriteRecord("scope-1", "approved", new VariableWriter(VariableWriteSource.Capture, "node-1", "inv-1"),
                    VariableCommitOutcome.Blind, baseVersion: null, currentVersion: 0, newVersion: 1, commitOrder: 1, Now) { Digest = digest }
            ]
        }, evidence);

        var withoutPermission = Assert.Single((await ViewAsync(canInspectSensitiveValues: false)).VariableWrites!).WrittenValue!;
        // Inspecting without the payload permission keeps the value hidden, so its digest is withheld too.
        var withoutPayloadPermission = Assert.Single((await ViewAsync(canResolve: false)).VariableWrites!).WrittenValue!;
        var withPermission = Assert.Single((await ViewAsync()).VariableWrites!).WrittenValue!;

        Assert.Equal(VariableLifecycleValueStateView.Redacted, withoutPermission.State);
        Assert.Null(withoutPermission.Digest);
        Assert.Null(withoutPayloadPermission.Digest);
        Assert.Equal(VariableLifecycleValueStateView.Captured, withPermission.State);
        Assert.Equal(digest, withPermission.Digest);
        // A redacted value never carries its digest, whoever asks.
        var redacted = VariableLifecycleValueStateView.FromEvidence(
            ActivityExecutionInspectionValueSnapshotView.From(evidence, ActivityExecutionId, 0, canInspectSensitiveValues: false, canResolveValuePayloads: false),
            digest,
            canDiscloseDigest: true);
        Assert.Equal(VariableLifecycleValueStateView.Redacted, redacted.State);
        Assert.Null(redacted.Digest);
    }

    [Fact]
    public async Task An_execution_with_no_lifecycle_records_carries_none()
    {
        await SaveAsync(State());

        var view = await ViewAsync();

        Assert.Null(view.VariableWrites);
        Assert.Null(view.RecordedCompletion);
        Assert.Null(view.InputRefreshes);
    }

    private static ValueEnvelope Inline(string value, bool sensitive = false) =>
        ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement(value),
            sensitive ? new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true) : ValueProtectionPolicy.InstanceInline);

    [Fact]
    public async Task Warnings_of_the_input_expressions_and_of_the_step_are_shown_in_order_without_values()
    {
        // Spec 1228: the shared run-time warning channel. A warning carries paths, never values (spec 1229 FR-006).
        var inputWarning = new Elsa.Expressions.Core.Models.ExecutionWarning(
            "VF-PREC-002", "The script read numbers a JavaScript number cannot hold exactly.",
            Details: new Dictionary<string, string> { ["input"] = "arguments", ["paths"] = "variables.order.id" });
        var stepWarning = new Elsa.Expressions.Core.Models.ExecutionWarning(
            "VF-PREC-002", "The script read numbers a JavaScript number cannot hold exactly.",
            Details: new Dictionary<string, string> { ["paths"] = "args.order.id" });
        var state = State();
        await SaveAsync(state with
        {
            InputSnapshot = new ActivityInputSnapshot("inv-1", "schema", "binding", new Dictionary<string, ValueEnvelope>(), Now) { Warnings = [inputWarning] },
            Warnings = [stepWarning]
        });

        var view = await ViewAsync(canInspectSensitiveValues: false, canResolve: false);

        Assert.Collection(
            view.Warnings!,
            warning =>
            {
                Assert.Equal(("VF-PREC-002", "Warning", "input", "arguments"), (warning.Code, warning.Severity, warning.Source, warning.InputKey));
                Assert.Equal(("node-1", ActivityExecutionId), (warning.ExecutableNodeId, warning.ActivityExecutionId));
                Assert.Equal(new Dictionary<string, string> { ["paths"] = "variables.order.id" }, warning.Details);
            },
            warning =>
            {
                Assert.Equal(("step", (string?)null), (warning.Source, warning.InputKey));
                Assert.Equal("args.order.id", warning.Details["paths"]);
            });
    }

    [Fact]
    public async Task An_execution_without_warnings_has_none()
    {
        await SaveAsync(State());

        Assert.Null((await ViewAsync()).Warnings);
    }

    private static ActivityExecutionState State() =>
        new(
            new ActivityExecution(ActivityExecutionId, WorkflowExecutionId, "node-1", "authored-1", "Test.Activity", "1.0.0"),
            ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: Now,
            StartedAt: Now,
            CompletedAt: Now,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private async Task SaveAsync(ActivityExecutionState state, params ActivityExecutionInspectionValueSnapshot[] evidence)
    {
        await _workflows.SaveAsync(new WorkflowExecutionState(
            WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            WorkflowExecutionStatus.Running,
            null, Now, Now, Now, null, null, null, null,
            new Dictionary<string, string>()));
        await _states.SaveAsync(state);
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(
            ActivityExecutionId, WorkflowExecutionId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            state.Status, state.SubStatus, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now,
            ActivitySchedulingProvenance.Empty, [], [], [], evidence, new Dictionary<string, string>()));
    }

    private async Task<ActivityExecutionInspectionView> ViewAsync(bool canInspectSensitiveValues = true, bool canResolve = true) =>
        (await new ActivityExecutionInspectionService(_workflows, _inspections, new InspectionAccess(canInspectSensitiveValues, canResolve), activityExecutionStateStore: _states)
            .GetAsync(new GetActivityExecution(WorkflowExecutionId, ActivityExecutionId), CancellationToken.None))!;

    private sealed class InspectionAccess(bool canInspectSensitiveValues = true, bool canResolve = true) : IActivityInspectionContextAsync
    {
        public string TenantScope => "tenant-a";
        public string AuditSubject => "subject-1";
        public string RequestCorrelationId => "request-1";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("structure");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectSensitiveValues);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canResolve);
    }
}
