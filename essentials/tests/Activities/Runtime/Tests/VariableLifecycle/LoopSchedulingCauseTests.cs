using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 T004 (FR-007): a loop schedules each next iteration with the loop as scheduler, so the run time records the
/// iteration that completed as its cause. Inspection reads that to order one iteration's writes after the previous one's.
/// </summary>
public sealed class LoopSchedulingCauseTests
{
    private static readonly IPayloadSerializer Serializer = SerializationServices.PayloadSerializer();

    [Fact]
    public async Task Each_next_iteration_of_a_For_loop_names_the_iteration_that_completed_before_it()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build("actexec-for", "actexec-i0", "actexec-i1", "actexec-i2");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            NewForNode("node-for", start: 0, end: 3, body: WorkflowExecutionHarness.NewProbeNode("node-body"))));

        run.AssertWorkflowCompleted();
        var iterations = run.States("node-body").OrderBy(state => state.ExecutionSequence).ToArray();
        Assert.Equal(3, iterations.Length);
        var loop = run.State("node-for").Execution.ActivityExecutionId;
        Assert.All(iterations, iteration => Assert.Equal(loop, iteration.Provenance.SchedulingActivityExecutionId));
        Assert.Null(iterations[0].Provenance.CausedBy);
        for (var index = 1; index < iterations.Length; index++)
            Assert.Equal([iterations[index - 1].Execution.ActivityExecutionId], iterations[index].Provenance.CausedBy);
    }

    private static ExecutableNode NewForNode(string nodeId, int start, int end, ExecutableNode body) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(ForActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(Serializer, typeof(ForActivity))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = Literal("Start", start),
                ["End"] = Literal("End", end)
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    private static RuntimeInputBinding Literal(string inputName, int value)
    {
        var type = new ValueTypeDescriptor("Int32");
        return new RuntimeInputBinding(inputName, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }
}
