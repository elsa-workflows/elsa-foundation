using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BpmnProcessActivity.StructureKind;

    public string SchemaVersion => BpmnProcessActivity.StructureSchemaVersion;

    public bool SupportsScopedVariables => true;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(BpmnProcessActivity.ActivitiesSlotName, structure.Activities)];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, BpmnProcessActivity.ActivitiesSlotName));
        var activities = slot?.Activities.ToArray() ?? [];
        var updated = new BpmnAuthoredStructure(
            activities,
            current.Elements,
            current.SequenceFlows,
            current.Pools,
            current.Lanes,
            current.Variables,
            current.Diagram,
            current.IsTransaction,
            current.MessageFlows);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(updated, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        // Pools, lanes and the opaque diagram document are authored-side/designer concerns; the
        // executable structure carries only the semantic process graph.
        var executableStructure = new BpmnStructure(
            authoredStructure.Elements,
            authoredStructure.SequenceFlows,
            authoredStructure.Variables,
            authoredStructure.IsTransaction);

        return new ActivityNodeStructure(
            BpmnProcessActivity.StructureKind,
            BpmnProcessActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<BpmnStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException("BPMN executable structure payload is invalid.");
        var remapped = new BpmnStructure(
            executable.Elements.Select(element => new BpmnElement(
                element.ElementId,
                element.ElementType,
                element.Name,
                Remap(element.ChildNodeId, authoredToExecutableNodeIds),
                element.LaneId,
                element.DefaultFlowId,
                element.EventDefinitions,
                element.Properties,
                element.AttachedToRef,
                element.CancelActivity,
                element.LoopCharacteristics,
                element.IsForCompensation,
                element.CompensationHandlerElementId,
                element.IsTransaction,
                element.TriggeredByEvent,
                Remap(element.ListenerNodeId, authoredToExecutableNodeIds))).ToArray(),
            executable.SequenceFlows,
            executable.Variables,
            executable.IsTransaction);
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(remapped, SerializerOptions));
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Variables;

    private static BpmnAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new BpmnAuthoredStructure();

        return activity.Structure.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)
               ?? new BpmnAuthoredStructure();
    }

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
