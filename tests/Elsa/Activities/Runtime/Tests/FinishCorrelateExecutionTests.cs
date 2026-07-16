using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// In-process coverage for the canonical workflow-effect intrinsics. These effects belong to the
/// engine checkpoint protocol and are deliberately not CLR activities.
/// </summary>
public sealed class FinishCorrelateExecutionTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public async Task FinishIntrinsic_EndsWorkflow_WithConfiguredOutcome()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("actexec-finish");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewEffectNode(
            "node-finish", WorkflowIntrinsicKind.Finish, WorkflowIntrinsicInputKeys.Outcome, "Aborted")));

        run.AssertOutcomes("node-finish", "Aborted");
        run.AssertWorkflowCompleted();
        Assert.NotNull(run.WorkflowState?.CompletedAt);
    }

    [Fact]
    public async Task FinishIntrinsic_EndsWorkflow_WithDoneOutcome()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("actexec-finish");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewEffectNode(
            "node-finish", WorkflowIntrinsicKind.Finish, WorkflowIntrinsicInputKeys.Outcome, ActivityOutcomes.Done)));

        run.AssertOutcomes("node-finish", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task SetCorrelationIdIntrinsic_PersistsCorrelationId()
    {
        await using var harness = WorkflowExecutionHarness.Create().Build("actexec-correlate");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewEffectNode(
            "node-correlate", WorkflowIntrinsicKind.SetCorrelationId, WorkflowIntrinsicInputKeys.Value, "order-42")));

        run.AssertCompleted("node-correlate");
        run.AssertWorkflowCompleted();
        Assert.Equal("order-42", run.WorkflowState?.CorrelationId);
    }

    [Fact]
    public async Task FinishInsideSequence_EndsWorkflow_AndSkipsLaterSiblings()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithProbeLeaf()
            .Build("actexec-sequence", "actexec-finish");

        var finish = NewEffectNode("node-finish", WorkflowIntrinsicKind.Finish, WorkflowIntrinsicInputKeys.Outcome, ActivityOutcomes.Done);
        var successor = WorkflowExecutionHarness.NewProbeNode("node-successor");
        var children = new[] { finish, successor };
        var root = new ExecutableNode(
            "node-sequence",
            "authored-sequence",
            typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!,
            "1.0.0",
            SequenceConstructor.DescriptorTypeKey,
            JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot(Elsa.Activities.Sequence.Activities.Sequence.ActivitiesSlotName, children)],
            new ExecutableActivityStructure(
                Elsa.Activities.Sequence.Activities.Sequence.StructureKind,
                Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertCompleted("node-finish");
        run.AssertDidNotRun("node-successor");
        run.AssertWorkflowCompleted();
    }

    private static ExecutableNode NewEffectNode(
        string nodeId,
        WorkflowIntrinsicKind kind,
        string inputKey,
        string value) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            $"elsa.intrinsic.{kind.ToString().ToLowerInvariant()}",
            "1.0.0",
            "intrinsic",
            JsonSerializer.SerializeToElement(new { kind = kind.ToString(), schemaVersion = "1.0.0" }),
            new Dictionary<string, RuntimeInputBinding>
            {
                [inputKey] = new(
                    inputKey,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            intrinsicKind: kind);

    private sealed record SequenceDescriptor;

    private sealed class SequenceConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string DescriptorTypeKey => typeof(SequenceDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) => new(new Elsa.Activities.Sequence.Activities.Sequence());

        public ValueTask<IActivity> Construct(
            SequenceDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) => new(new Elsa.Activities.Sequence.Activities.Sequence());
    }
}
