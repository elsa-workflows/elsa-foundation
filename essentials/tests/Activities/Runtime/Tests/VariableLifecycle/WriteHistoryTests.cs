using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// US7 (spec 1221 T036 to T039; FR-024 to FR-026): the write history keeps, for every applied write, its value as an
/// inspection value under the capture policy (captured, or not captured, never a silent <c>null</c>), and keeps the
/// operational data a recovery needs whatever that policy says: the value a lost-update warning overwrote, which can be
/// restored.
/// </summary>
public sealed class WriteHistoryTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    // Two parallel leaves both read 0; the left writes 1, the right writes 2 over it (a lost-update warning).
    private static WorkflowExecutable Executable() => VariableLifecycleTestGraph.ParallelIncrements(typeof(DoublingIncrementActivity));

    [Fact]
    public async Task With_values_captured_each_write_keeps_the_value_it_wrote()
    {
        await using var harness = Harness(new CapturePolicy(RuntimePayloadCaptureMode.Payload));

        var run = await harness.RunAsync(Executable());

        foreach (var (nodeId, written) in new[] { ("node-left", 1), ("node-right", 2) })
        {
            var record = Assert.Single(run.State(nodeId).VariableWrites!);
            var snapshot = Assert.Single(await WriteSnapshotsAsync(harness, run, nodeId));
            Assert.Equal(VariableLifecycleTestGraph.CountKey, snapshot.Name);
            Assert.Equal(record.ScopeId, snapshot.Phase);
            Assert.Equal(record.NewVersion, snapshot.Sequence);
            Assert.Equal(RuntimePayloadCaptureMode.Payload, snapshot.CaptureMode);
            Assert.Equal(written, snapshot.Payload!.Value.GetInt32());
        }
    }

    // FR-025/FR-026: with values not captured, the history says so (no payload, capture mode None) rather than showing
    // null, and the operational data stays: the overwritten value is on the record.
    [Fact]
    public async Task With_values_off_the_history_says_so_and_the_operational_data_stays()
    {
        await using var harness = Harness(new CapturePolicy(RuntimePayloadCaptureMode.None));

        var run = await harness.RunAsync(Executable());

        var snapshot = Assert.Single(await WriteSnapshotsAsync(harness, run, "node-right"));
        Assert.Equal(RuntimePayloadCaptureMode.None, snapshot.CaptureMode);
        Assert.Null(snapshot.Payload);
        var warning = Assert.Single(run.State("node-right").VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, warning.Outcome);
        Assert.Equal(1, warning.OverwrittenValue!.InlineValue!.Value.GetInt32());

        // Restoring it is a ModifyVariable with this value: OperatorActionsTests.A_value_a_lost_update_overwrote_can_be_restored.
    }

    // T039: an intrinsic Set leaves an Intrinsic record, numbered, with its value in the history.
    [Fact]
    public async Task An_intrinsic_write_is_recorded_with_its_value()
    {
        await using var harness = Harness(new CapturePolicy(RuntimePayloadCaptureMode.Payload));
        var type = VariableLifecycleTestGraph.Int32Type;
        var set = new ExecutableNode(
            executableNodeId: "node-set",
            authoredActivityId: "authored-node-set",
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = new(WorkflowIntrinsicInputKeys.Value, type, ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, System.Text.Json.JsonSerializer.SerializeToElement(42), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(VariableLifecycleTestGraph.CountKey, Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(set, VariableLifecycleTestGraph.CountDeclaration()));

        var record = Assert.Single(run.State("node-set").VariableWrites!);
        Assert.Equal(VariableWriteSource.Intrinsic, record.Writer.Source);
        Assert.NotNull(record.CommitSequence);
        var snapshot = Assert.Single(await WriteSnapshotsAsync(harness, run, "node-set"));
        Assert.Equal(42, snapshot.Payload!.Value.GetInt32());
        Assert.Equal(record.NewVersion, snapshot.Sequence);
    }

    // T039: the required metadata is present on a capture record (source, writer, versions, commit order).
    [Fact]
    public async Task A_capture_record_carries_the_required_metadata()
    {
        await using var harness = Harness(new CapturePolicy(RuntimePayloadCaptureMode.None));

        var run = await harness.RunAsync(Executable());

        var record = Assert.Single(run.State("node-left").VariableWrites!);
        Assert.Equal(VariableWriteSource.Capture, record.Writer.Source);
        Assert.Equal("node-left", record.Writer.ExecutableNodeId);
        Assert.Equal("actexec-left", record.Writer.InvocationId);
        Assert.NotNull(record.Writer.AttemptId);
        Assert.Equal(0, record.BaseVersion);
        Assert.Equal(1, record.NewVersion);
        Assert.NotNull(record.CommitSequence);
    }

    private static WorkflowExecutionHarness Harness(IRuntimePayloadCapturePolicy policy) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false });
                services.RemoveAll<IRuntimePayloadCapturePolicy>();
                services.AddSingleton(policy);
            })
            .Build(Ids);

    private static async Task<IReadOnlyList<ActivityExecutionInspectionValueSnapshot>> WriteSnapshotsAsync(
        WorkflowExecutionHarness harness, WorkflowExecutionRun run, string nodeId)
    {
        var state = run.State(nodeId);
        var projection = await harness.Services.GetRequiredService<IActivityExecutionInspectionStore>()
            .FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
        return projection!.ValueSnapshots.Where(snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.VariableWrite).ToArray();
    }

    private sealed class CapturePolicy(RuntimePayloadCaptureMode variableWrites) : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.Subject == RuntimePayloadCaptureSubject.VariableWrite
                ? new RuntimePayloadCaptureDecision(variableWrites, "Test policy for variable writes.")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Test policy captures nothing else.");
    }
}
