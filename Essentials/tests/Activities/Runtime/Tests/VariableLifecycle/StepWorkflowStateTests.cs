using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed class StepWorkflowStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_writes_merge_root_frame_and_identity_and_keep_capture_namespaces_distinct(bool capture)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var node = Node("complete", capture);
        var run = await harness.RunAsync(StepVariableTestGraph.Single(node));
        run.AssertWorkflowCompleted();
        Assert.Equal(capture ? 7 : 42, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal("new", run.WorkflowState!.CorrelationId);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.Equal(33, Assert.Single(values, value => value.ValueId == "output:count").InlineValue!.Value.GetInt32());
        Assert.DoesNotContain(run.State("node-root").Warnings ?? [], warning => warning.Code == "VF-STATE-004");
    }

    [Fact]
    public async Task Actual_same_output_capture_wins_and_discloses_value_free_overlap()
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var original = Node("complete");
        var projection = original.ActivityContract!.Result.Projections.Keys.Single(key => key.EndsWith(nameof(Incremented.Value), StringComparison.Ordinal));
        var node = new ExecutableNode(original.ExecutableNodeId, original.AuthoredActivityId, original.ActivityType, original.ActivityTypeVersion,
            original.DescriptorType, original.DescriptorPayload, inputBindings: original.InputBindings, metadata: original.Metadata,
            activityContract: original.ActivityContract, outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                [projection] = new(projection, "output:count", new RuntimeValueTypeDescriptor("Int32", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
                    DurableValueLifecycle.Instance, DurableValueStorage.Custom, true, new Dictionary<string, string> { [RuntimeMetadataKeys.OutputName] = "count" })
            });
        var run = await harness.RunAsync(StepVariableTestGraph.Single(node));
        run.AssertWorkflowCompleted();
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.Equal(7, Assert.Single(values, value => value.ValueId == "output:count").InlineValue!.Value.GetInt32());
        Assert.Contains(run.State("node-root").Warnings!, warning => warning.Code == "VF-STATE-004");
    }

    [Theory]
    [InlineData("throw", ActivityExecutionStatus.Faulted)]
    [InlineData("fault", ActivityExecutionStatus.Faulted)]
    [InlineData("cancel", ActivityExecutionStatus.Cancelled)]
    [InlineData("invalid", ActivityExecutionStatus.Faulted)]
    [InlineData("caught", ActivityExecutionStatus.Faulted)]
    [InlineData("dispose", ActivityExecutionStatus.Faulted)]
    [InlineData("dispose-caught-access", ActivityExecutionStatus.Faulted)]
    public async Task Native_failure_paths_discard_every_pending_effect(string mode, ActivityExecutionStatus expected)
    {
        await using var harness = VariableLifecycleTestGraph.NewHarness("actexec-root");
        var run = await harness.RunAsync(StepVariableTestGraph.Single(Node(mode)));
        Assert.Equal(expected, run.State("node-root").Status);
        Assert.Equal(0, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Null(run.WorkflowState!.CorrelationId);
        var values = await harness.Services.GetRequiredService<IDurableValueStateStore>().ListAllDurableValueStatesAsync(harness.ExecutionId);
        Assert.DoesNotContain(values, value => value.ValueId == "output:count");
    }

    private static ExecutableNode Node(string mode, bool capture = false) => StepVariableTestGraph.Leaf(
        "node-root", typeof(NativeStateWriter), new Dictionary<string, RuntimeInputBinding>
        {
            ["Count"] = StepVariableTestGraph.Reference("Count", "count", VariableLifecycleTestGraph.CountKey),
            ["Mode"] = new("Mode", new Elsa.Primitives.Models.ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new Elsa.Primitives.Models.ValueTypeDescriptor("String"),
                    JsonSerializer.SerializeToElement(mode), ValueProtectionPolicy.InstanceInline))
        }, captureInto: capture ? VariableLifecycleTestGraph.CountKey : null);
}

[AccessesWorkflowState]
public sealed class NativeStateWriter : Activity<Incremented>, IDisposable
{
    [ActivityInput] public VariableReference<int> Count { get; set; } = default!;
    [ActivityInput] public string Mode { get; set; } = "complete";
    private ActivityExecutionContext? _context;

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        _context = context;
        context.Variables.Set(Count, 42);
        context.WorkflowState.SetOutput("count", JsonSerializer.SerializeToElement(33));
        context.WorkflowState.CorrelationId = "new";
        context.WorkflowState.InstanceName = "Ready";
        Assert.Equal("new", context.WorkflowState.CorrelationId);
        Assert.True(context.WorkflowState.TryGetOutput("count", out var pending));
        Assert.Equal(33, pending.GetInt32());
        if (Mode == "throw") throw new InvalidOperationException("body failed");
        if (Mode == "caught")
            try { context.WorkflowState.SetOutput("", JsonSerializer.SerializeToElement(1)); }
            catch (InvalidOperationException) { }
        return ValueTask.FromResult(Mode switch
        {
            "fault" => ActivityTransition.Fault<Incremented>(new ActivityFault("test", "body fault")),
            "cancel" => ActivityTransition.Cancel<Incremented>("body cancellation"),
            "invalid" => ActivityTransition.Complete(new Incremented(7), OutcomeSelection.Named("Unknown")),
            _ => ActivityTransition.Complete(new Incremented(7))
        });
    }

    public void Dispose()
    {
        if (Mode == "dispose") throw new InvalidOperationException("disposal failed");
        if (Mode == "dispose-caught-access")
            try { _context!.WorkflowState.CorrelationId = "late"; }
            catch (InvalidOperationException) { }
    }
}
