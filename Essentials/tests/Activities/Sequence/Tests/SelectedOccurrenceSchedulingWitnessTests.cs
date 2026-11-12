using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

// Lower-level scheduling witnesses, not an authorized selected-occurrence start API.
public sealed class SelectedOccurrenceSchedulingWitnessTests
{
    [Fact]
    public async Task Ordinary_trigger_identity_does_not_select_a_subtree()
    {
        await using var harness = Harness();
        var executable = Candidate(Echo());
        var run = await harness.RunAsync(executable, JsonSerializer.SerializeToElement(new { }), "selected");
        run.AssertWorkflowCompleted();
        Assert.Equal(new[] { "after", "before", "container", "selected" }, run.RanNodeIds.Order().ToArray());
        Assert.Equal(42, run.State("selected").InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public async Task Parentless_leaf_uses_normal_pipeline_without_changing_the_full_artifact()
    {
        await using var harness = Harness();
        var run = await ScheduleSelected(harness, Candidate(Echo()));
        run.AssertWorkflowCompleted();
        var state = Assert.Single(run.ActivityStates);
        Assert.Equal("selected", state.Execution.ExecutableNodeId);
        Assert.Null(state.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(42, state.InputSnapshot!.Values["value"].InlineValue!.Value.GetInt32());
        Assert.Equal(42, state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Parentless_composite_runs_its_owned_children_and_no_containing_siblings()
    {
        await using var harness = Harness();
        var selected = Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("owned-a"), WorkflowExecutionHarness.NewProbeNode("owned-b")]);
        var run = await ScheduleSelected(harness, Candidate(selected));
        run.AssertWorkflowCompleted();
        Assert.Equal(new[] { "owned-a", "owned-b", "selected" }, run.RanNodeIds.Order().ToArray());
        var parent = run.State("selected");
        Assert.Null(parent.ParentActivityExecutionId);
        Assert.All(run.ActivityStates.Where(x => x != parent), child => Assert.Equal(parent.Execution.ActivityExecutionId, child.ParentActivityExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_capture_needs_a_real_enclosing_invocation(bool selectedOnly)
    {
        await using var harness = Harness();
        var candidate = Candidate(Echo(capture: true), closed: true);
        var run = selectedOnly ? await ScheduleSelected(harness, candidate) : await harness.RunAsync(candidate);
        if (!selectedOnly)
        {
            run.AssertWorkflowCompleted();
            Assert.Equal(4, run.ActivityStates.Count);
            var captured = await harness.Services.GetRequiredService<IDurableValueStateStore>().FindAsync(harness.ExecutionId,
                ActivityExecutionScopeKeys.Variable(run.State("container").InvocationId, "counter"));
            Assert.NotNull(captured);
            Assert.Equal(42, captured.InlineValue!.Value.GetInt32());
            return;
        }
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
        var selected = Assert.Single(run.ActivityStates);
        Assert.Equal("selected", selected.Execution.ExecutableNodeId);
        Assert.Null(selected.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Faulted, selected.Status);
        Assert.Contains("not the nearest closed activity execution scope", selected.Fault!.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
        .WithProbeLeaf().Build(Enumerable.Range(1, 20).Select(index => "activity-" + index));

    private static WorkflowExecutable Candidate(ExecutableNode selected, bool closed = false) =>
        WorkflowExecutionHarness.NewExecutable(Sequence("container",
            [WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after")], closed));

    private static ExecutableNode Sequence(string id, ExecutableNode[] children, bool closed = false) => new(
        id, id, typeof(SequenceActivity).FullName!, "1", "test.sequence", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding>(), closed
            ? new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityExecutionScope] = RuntimeMetadataKeys.ActivityExecutionScopeClosed }
            : new Dictionary<string, string>(),
        childSlots: [new(SequenceActivity.ActivitiesSlotName, children)],
        structure: new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId),
                variables = new[] { new RuntimeVariableDeclaration("counter", "counter", new("Object"), ValueProtectionPolicy.InstanceInline) } },
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static ExecutableNode Echo(bool capture = false) => new("selected", "selected", typeof(EchoActivity).FullName!, "1", "test.echo",
        JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new("value", new("Object"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(new("Object"), JsonSerializer.SerializeToElement(42), ValueProtectionPolicy.InstanceInline))
        }, new Dictionary<string, string>(), outputCaptures: capture ? new Dictionary<string, RuntimeOutputCapture>
        {
            ["value"] = new("value", "captured", new("Object", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
                DurableValueLifecycle.Instance, DurableValueStorage.Inline, true, new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.CaptureTargetScopeId] = "container",
                    [RuntimeMetadataKeys.TargetVariableReferenceKey] = "counter"
                })
        } : null);

    private static async Task<WorkflowExecutionRun> ScheduleSelected(WorkflowExecutionHarness harness, WorkflowExecutable executable)
    {
        // Publish pins CLR contracts using the normal harness owner. Keep the resulting complete artifact.
        await harness.PublishAsync(executable, "witness-source");
        var store = harness.Services.GetRequiredService<IWorkflowExecutableStore>();
        var pinned = (await store.FindAsync(executable.Identity.ArtifactId))!;
        var before = JsonSerializer.Serialize(pinned);
        var agent = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new(harness.ExecutionId, WorkflowExecutionActorActivationReason.Start, WorkflowExecutionHarness.Timestamp,
                "scheduling-witness", WorkflowExecutionActorCapabilities.InProcessMailbox));
        await Send(WorkflowExecutionCommandKind.Checkpoint, new RuntimeCheckpointCommandPayload(pinned.Identity,
            RuntimeCheckpointNames.WorkflowStarted, [], RuntimeCheckpointCommandPayload.WorkflowStartReason, runKind: WorkflowRunKind.TestRun), 1);
        await Send(WorkflowExecutionCommandKind.ScheduleActivity,
            new RuntimeScheduleActivityCommandPayload(pinned.Identity, "selected", "selected-execution", "scheduling-witness"), 2);
        Assert.Equal(before, JsonSerializer.Serialize(await store.FindAsync(pinned.Identity.ArtifactId)));
        Assert.Empty(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        var run = await harness.ReadRunAsync(harness.ExecutionId);
        Assert.Equal(WorkflowRunKind.TestRun, run.WorkflowState!.RunKind);
        return run;

        async Task Send(WorkflowExecutionCommandKind kind, object payload, long sequence)
        {
            var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = pinned.Identity.ArtifactId };
            var command = new WorkflowExecutionCommand("command-" + sequence, harness.ExecutionId, kind,
                WorkflowExecutionHarness.Timestamp, JsonSerializer.SerializeToElement(payload), metadata);
            var result = await agent.EnqueueAsync(new("envelope-" + sequence, harness.ExecutionId, command,
                "witness-" + sequence, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, WorkflowExecutionHarness.Timestamp, sequence, metadata));
            Assert.Contains(result.Status, new[] { WorkflowExecutionCommandDispatchStatus.Accepted, WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted });
        }
    }

    public sealed record EchoResult([property: Output(Key = "value", Path = "value")] object? Value);
    public sealed class EchoActivity : Activity<EchoResult>
    {
        [ActivityInput(Key = "value")] public object? Value { get; set; }
        protected override ValueTask<ActivityTransition<EchoResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new EchoResult(Value)));
    }
}
