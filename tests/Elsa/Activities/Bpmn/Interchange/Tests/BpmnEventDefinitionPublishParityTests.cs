using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Spec 118 D6 — publish parity: a document imported with event-defined starts and a catch event must
/// satisfy the runtime seams specs 116/117 froze with no hand editing. The imported structure is bridged
/// to a runtime <see cref="ExecutableNode"/> and run through the real
/// <see cref="WorkflowTriggerBindingExtractor"/> (which itself validates the graph via
/// <see cref="BpmnGraph.From"/>), proving the interchange output publishes and binds correctly.
/// </summary>
public sealed class BpmnEventDefinitionPublishParityTests
{
    private const string Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private const string Document = $$"""
        <definitions xmlns="{{Bpmn}}" id="defs" targetNamespace="https://example.org">
          <message id="msg-order" name="order-placed" />
          <signal id="sig-cancel" name="cancel-requested" />
          <message id="msg-ship" name="order-shipped" />
          <process id="p" isExecutable="true">
            <startEvent id="start-msg"><messageEventDefinition messageRef="msg-order" /></startEvent>
            <startEvent id="start-sig"><signalEventDefinition signalRef="sig-cancel" /></startEvent>
            <startEvent id="start-timer"><timerEventDefinition><timeCycle>PT5M</timeCycle></timerEventDefinition></startEvent>
            <intermediateCatchEvent id="catch-msg"><messageEventDefinition messageRef="msg-ship" /></intermediateCatchEvent>
            <endEvent id="end" />
            <sequenceFlow id="f1" sourceRef="start-msg" targetRef="catch-msg" />
            <sequenceFlow id="f2" sourceRef="start-sig" targetRef="catch-msg" />
            <sequenceFlow id="f3" sourceRef="start-timer" targetRef="catch-msg" />
            <sequenceFlow id="f4" sourceRef="catch-msg" targetRef="end" />
          </process>
        </definitions>
        """;

    [Fact]
    public void ImportedEventDefinitions_PublishAndValidate_ThroughRealRuntimeSeams()
    {
        var structure = new BpmnDocumentImporter().Import(Document)
            .ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;

        var node = BridgeToExecutableNode(structure);

        // BpmnGraph.From enforces the frozen graph rules: the catch event must bind its synthesized child,
        // and the event-defined starts must be pure (no child). Both come straight from the import.
        var graph = BpmnGraph.From(node);
        Assert.Equal(3, graph.StartEvents.Count);
        var catchEvent = graph.Elements.Single(element => element.ElementId == "catch-msg");
        Assert.Equal("node-catch-msg", catchEvent.ChildNodeId);

        // The real extractor recognizes the trigger node and emits one binding per event-defined start.
        var executable = new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity("artifact-bpmn", "definition-1", "version-1", "1.0.0", "sha256:v1"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());
        var bindings = new WorkflowTriggerBindingExtractor([new BpmnProcessTriggerStimulusProvider()]).Extract(executable);
        Assert.Equal(3, bindings.Count);

        var byElement = bindings.ToDictionary(binding => binding.Metadata[BpmnStartTrigger.StartElementIdMetadataKey]);
        Assert.Equal(new[] { "start-msg", "start-sig", "start-timer" }, byElement.Keys.OrderBy(key => key));
        Assert.Equal(BpmnMessageStartStimulus.Hash("order-placed"), byElement["start-msg"].StimulusHash);
        Assert.Equal(BpmnMessageStartStimulus.Hash("cancel-requested"), byElement["start-sig"].StimulusHash);
        Assert.Equal(BpmnTimerStartStimulus.StimulusType, byElement["start-timer"].StimulusType);
    }

    /// <summary>Wraps the imported authored structure into the runtime executable shape: a trigger-marked BpmnProcess node whose child slot carries the synthesized catch children.</summary>
    private static ExecutableNode BridgeToExecutableNode(BpmnAuthoredStructure structure)
    {
        var children = structure.Activities
            .Select(activity => new ExecutableNode(
                executableNodeId: activity.NodeId,
                authoredActivityId: $"authored-{activity.NodeId}",
                activityType: activity.ActivityVersionId,
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()))
            .ToArray();

        return new ExecutableNode(
            executableNodeId: "node-p",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType },
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(structure.Elements, structure.SequenceFlows))));
    }
}
