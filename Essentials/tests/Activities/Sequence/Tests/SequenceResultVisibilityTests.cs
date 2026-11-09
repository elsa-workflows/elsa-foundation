using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class SequenceResultVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_later_sibling_reads_the_producer_result_before_and_after_extraction(bool extracted)
    {
        await using var harness = NewHarness();
        var producer = Echo("producer", Literal(42));
        var consumer = Echo("consumer", Result("producer"));
        var root = Sequence("root", extracted ? Sequence("block", producer) : producer, consumer);

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(root),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        run.AssertWorkflowCompleted();
        Assert.Equal(42, ReadInput(run, "consumer"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_child_reads_a_prior_sibling_result_before_and_after_extraction(bool extracted)
    {
        await using var harness = NewHarness();
        var producer = Echo("producer", Literal(42));
        var consumer = Echo("consumer", Result("producer"));
        var root = Sequence("root", producer, extracted ? Sequence("block", consumer) : consumer);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        Assert.Equal(42, ReadInput(run, "consumer"));
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .Build(Enumerable.Range(1, 32).Select(index => $"actexec-{index}"));

    private static ExecutableNode Sequence(string nodeId, params ExecutableNode[] children) => new(
        executableNodeId: nodeId,
        authoredActivityId: nodeId,
        activityType: typeof(SequenceActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding>(),
        metadata: new Dictionary<string, string>(),
        childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
        structure: new ExecutableActivityStructure(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

    private static ExecutableNode Echo(string nodeId, RuntimeInputBinding input) => new(
        executableNodeId: nodeId,
        authoredActivityId: nodeId,
        activityType: typeof(EchoActivity).FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding> { [input.InputName] = input },
        metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding Literal(int value) => new(
        nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding Result(string producerNodeId) => new(
        nameof(EchoActivity.Value), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResult,
        activityResult: new RuntimeActivityResultReference(producerNodeId, nameof(EchoResult.Value), "scope:root"));

    private static int ReadInput(WorkflowExecutionRun run, string nodeId) =>
        run.State(nodeId).InputSnapshot!.Values[nameof(EchoActivity.Value)].InlineValue!.Value.GetInt32();

    public sealed record EchoResult([property: Output(Key = "Value", Path = "value")] object? Value);

    public sealed class EchoActivity : Activity<EchoResult>
    {
        [ActivityInput] public object? Value { get; set; }

        protected override ValueTask<ActivityTransition<EchoResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new EchoResult(Value)));
    }
}
