using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// In-process execution coverage for the <c>Finish</c>/<c>Complete</c> and <c>Correlate</c> control leaves
/// running through the real workflow agent on the shared <see cref="WorkflowExecutionHarness"/>. Asserts the
/// engine drains the leaf intents: <c>Finish</c> ends the run with its outcome, and <c>Correlate</c> persists
/// the new correlation id on the workflow instance.
/// </summary>
/// <remarks>
/// As with the <c>Fault</c> and <c>If</c> execution tests, the leaf is constructed by a focused test
/// <see cref="IActivityConstructor"/> rather than the production <c>ClrActivityConstructor</c> (whose
/// serialization stack is out of scope for a single-leaf execution probe). The activities themselves are
/// registered with the production <c>ClrActivityConstructor</c> via <c>ActivitiesPrimitivesFeature</c>.
/// </remarks>
public sealed class FinishCorrelateExecutionTests
{
    [Fact]
    public async Task FinishActivity_EndsWorkflow_WithConfiguredOutcome()
    {
        await using var harness = NewHarness<FinishConstructor>("actexec-finish");

        var run = await harness.RunAsync(NewExecutable("node-finish", typeof(Finish), "OutcomeName", "Aborted"));

        run.AssertOutcomes("node-finish", "Aborted");
        run.AssertWorkflowCompleted();
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.NotNull(run.WorkflowState?.CompletedAt);
    }

    [Fact]
    public async Task FinishActivity_EndsWorkflow_WithDoneOutcome_WhenOutcomeIsUnset()
    {
        await using var harness = NewHarness<FinishConstructor>("actexec-finish");

        var run = await harness.RunAsync(NewExecutable("node-finish", typeof(Finish), inputName: null, inputValue: null));

        run.AssertOutcomes("node-finish", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task CorrelateActivity_SetsCorrelationId_AndCompletesWorkflow()
    {
        await using var harness = NewHarness<CorrelateConstructor>("actexec-correlate");

        var run = await harness.RunAsync(NewExecutable("node-correlate", typeof(Correlate), "CorrelationId", "order-42"));

        run.AssertCompleted("node-correlate");
        run.AssertWorkflowCompleted();
        Assert.Equal("order-42", run.WorkflowState?.CorrelationId);
    }

    [Fact]
    public async Task FinishInsideSequence_EndsWorkflow_AndSkipsLaterSiblings()
    {
        // Sequence: [Finish, probe-successor]. Finish runs first and ends the whole run; because it skips
        // completion propagation, the sequence never schedules the successor and the run reaches Completed.
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithConstructor<FinishConstructor>()
            .WithProbeLeaf()
            .Build("actexec-sequence", "actexec-finish");

        var run = await harness.RunAsync(NewSequenceWithFinishFirst());

        run.AssertCompleted("node-finish");
        run.AssertDidNotRun("node-successor");
        run.AssertWorkflowCompleted();
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    private static WorkflowExecutionHarness NewHarness<TConstructor>(params string[] activityExecutionIds)
        where TConstructor : class, IActivityConstructor =>
        WorkflowExecutionHarness.Create()
            .WithConstructor<TConstructor>()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewSequenceWithFinishFirst()
    {
        var finishNode = new ExecutableNode(
            executableNodeId: "node-finish",
            authoredActivityId: "authored-finish",
            activityType: typeof(Finish).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(TestDescriptor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new TestDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var successorNode = WorkflowExecutionHarness.NewProbeNode("node-successor");
        var children = new[] { finishNode, successorNode };

        var sequenceType = typeof(Elsa.Activities.Sequence.Activities.Sequence);
        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: sequenceType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(SequenceConstructor.SequenceConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new SequenceDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(Elsa.Activities.Sequence.Activities.Sequence.ActivitiesSlotName, children)
            ],
            structure: new ExecutableActivityStructure(
                Elsa.Activities.Sequence.Activities.Sequence.StructureKind,
                Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static WorkflowExecutable NewExecutable(string nodeId, Type activityType, string? inputName, string? inputValue)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>();
        if (inputName is not null && inputValue is not null)
            inputBindings[inputName] = new RuntimeInputBinding(
                inputName: inputName,
                source: RuntimeInputBindingSource.Literal,
                literalValue: JsonSerializer.SerializeToElement(inputValue),
                metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.String" });

        var root = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(TestDescriptor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new TestDescriptor())),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed record TestDescriptor
    {
        public static string ConsumerKeyValue => typeof(TestDescriptor).FullName!;
    }

    private sealed class FinishConstructor : IActivityConstructor<TestDescriptor>
    {
        public string ConsumerKey => TestDescriptor.ConsumerKeyValue;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new TestDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(TestDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new Finish();
            if (inputs is not null && inputs.TryGetValue("OutcomeName", out var outcomeInput))
                activity.OutcomeName = (InputArgument<string>)outcomeInput;
            return new(activity);
        }
    }

    private sealed class CorrelateConstructor : IActivityConstructor<TestDescriptor>
    {
        public string ConsumerKey => TestDescriptor.ConsumerKeyValue;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new TestDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(TestDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new Correlate();
            if (inputs is not null && inputs.TryGetValue("CorrelationId", out var correlationInput))
                activity.CorrelationId = (InputArgument<string>)correlationInput;
            return new(activity);
        }
    }

    private sealed record SequenceDescriptor;

    private sealed class SequenceConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string SequenceConsumerKey => typeof(SequenceDescriptor).FullName!;
        public string ConsumerKey => SequenceConsumerKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new Elsa.Activities.Sequence.Activities.Sequence());

        public ValueTask<IActivity> Construct(SequenceDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new Elsa.Activities.Sequence.Activities.Sequence());
    }
}
