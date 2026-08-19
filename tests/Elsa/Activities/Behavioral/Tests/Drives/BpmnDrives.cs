using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// BpmnProcess: a minimal start → task → end process, driven to its Done outcome through the real BPMN engine.
/// The bound task child is a probe, so the drive proves the process actually walked the graph rather than
/// completing vacuously.
/// </summary>
public sealed class BpmnProcessDrive : IActivityDrive
{
    public const string ProcessNodeId = "node-bpmn";

    public Type ActivityType => typeof(BpmnProcess);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = BpmnHarness.New("actexec-bpmn", "actexec-task");
        var child = WorkflowExecutionHarness.NewProbeNode("node-task");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewProcessNode(
            [child],
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("task", BpmnElementTypes.Task, childNodeId: "node-task"),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            [
                new BpmnSequenceFlow("flow-1", "start", "task"),
                new BpmnSequenceFlow("flow-2", "task", "end")
            ])));

        recorder.Record(ActivityType, run, ProcessNodeId);
        run.AssertRan("node-task");
        run.AssertOutcomes(ProcessNodeId, ActivityOutcomes.Done);
    }

    internal static ExecutableNode NewProcessNode(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> sequenceFlows) =>
        new(
            executableNodeId: ProcessNodeId,
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcess).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcess.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                BpmnProcess.StructureKind,
                BpmnProcess.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new BpmnStructure(elements, sequenceFlows),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}

/// <summary>
/// BpmnDecision routes an exclusive gateway by evaluating to a named outcome. Its ports are authored data (the
/// gateway's outgoing flow names), so the node carries an explicit contract declaring them — exactly what the
/// publish compiler pins. The drive reaches both a named branch and the blank-input Done fallback.
/// </summary>
public sealed class BpmnDecisionDrive : IActivityDrive
{
    public Type ActivityType => typeof(BpmnDecision);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, authored: "Approved", expected: "Approved");
        // A blank or unbound Outcome resolves to Done — the documented fallback.
        await RunAsync(recorder, authored: "  ", expected: ActivityOutcomes.Done);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, string authored, string expected)
    {
        const string nodeId = "node-decision";
        await using var harness = BpmnHarness.New("actexec-bpmn", "actexec-decision", "actexec-approved", "actexec-rejected");

        var decision = NewDecisionNode(nodeId, authored, "Approved", "Rejected");
        var approved = WorkflowExecutionHarness.NewProbeNode("node-approved");
        var rejected = WorkflowExecutionHarness.NewProbeNode("node-rejected");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(BpmnProcessDrive.NewProcessNode(
            [decision, approved, rejected],
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("gateway", BpmnElementTypes.ExclusiveGateway, childNodeId: nodeId),
                new BpmnElement("approved", BpmnElementTypes.Task, childNodeId: "node-approved"),
                new BpmnElement("rejected", BpmnElementTypes.Task, childNodeId: "node-rejected"),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            [
                new BpmnSequenceFlow("flow-1", "start", "gateway"),
                new BpmnSequenceFlow("Approved", "gateway", "approved"),
                new BpmnSequenceFlow("Rejected", "gateway", "rejected"),
                new BpmnSequenceFlow("flow-4", "approved", "end"),
                new BpmnSequenceFlow("flow-5", "rejected", "end")
            ])));

        recorder.Record(ActivityType, run, nodeId);
        run.AssertOutcomes(nodeId, expected);
    }

    private static ExecutableNode NewDecisionNode(string nodeId, string outcome, params string[] declaredOutcomes)
    {
        var serializer = TestPayloadSerializers.NewPayloadSerializer();
        var stringType = new ValueTypeDescriptor("String");
        var payload = ClrConstruction.Payload(serializer, typeof(BpmnDecision));
        var contract = new ActivityContract(
            typeof(BpmnDecision).FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            payload,
            [new ActivityInputContract(nameof(BpmnDecision.Outcome), nameof(BpmnDecision.Outcome), stringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(
                new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(BpmnDecisionResult))),
                true,
                ActivityValuePolicy.Default,
                []),
            [.. declaredOutcomes, ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(typeof(BpmnDecision))));

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnDecision).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: payload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(BpmnDecision.Outcome)] = new(
                    nameof(BpmnDecision.Outcome),
                    stringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(stringType, JsonSerializer.SerializeToElement(outcome), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }
}

internal static class BpmnHarness
{
    public static WorkflowExecutionHarness New(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesBpmnRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .Build(activityExecutionIds);
}
