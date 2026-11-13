using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Nested authored nodes carry the same string enums as the workflow API (intrinsics and conversions).
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => BpmnProcessActivity.StructureKind;

    public string SchemaVersion => BpmnProcessActivity.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(BpmnAuthoredStructure);

    public bool SupportsScopedVariables => true;

    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        BpmnLinearChildResultAnalysis.TryCreate(ReadAuthoredStructure(activity))?.Assess(producerChildNodeId, consumerChildNodeId)
        ?? new(ActivityChildResultAvailability.Unknown);

    public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId) =>
        BpmnLinearChildResultAnalysis.TryCreate(ReadAuthoredStructure(activity))?.CompletedBefore(childNodeId);

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        var outcomes = structure.Elements.Where(element => element.ChildNodeId is not null)
            .GroupBy(element => element.ChildNodeId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var elements = group.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
                var names = structure.SequenceFlows
                    .Where(flow => elements.TryGetValue(flow.SourceRef, out var element) &&
                        !flow.IsDefault && element.DefaultFlowId != flow.FlowId)
                    .Select(flow => flow.ConditionOutcome)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new ActivityChildOutcomeProjection(group.Key, BpmnActivityIdentities.DecisionType, names, BpmnActivityIdentities.DecisionOutcomeInput);
            }).ToArray();
        return [new ActivityChildProjection(BpmnProcessActivity.ActivitiesSlotName, structure.Activities)
        {
            OutcomeProjections = outcomes
        }];
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
        if (structure.Kind != BpmnProcessActivity.StructureKind || structure.SchemaVersion != BpmnProcessActivity.StructureSchemaVersion)
            throw new InvalidOperationException("Legacy BPMN executable remapping requires the exact kind and schema 1.0.0 before typed deserialization.");
        RejectCompleteSourceAuthority(structure.Payload, $"executable remapping of '{structure.Kind}@{structure.SchemaVersion}'");
        // Validate existing semantic contracts without interpreting executable variables as authored declarations.
        var semantic = structure.Payload.EnumerateObject().Where(property => !StringComparer.OrdinalIgnoreCase.Equals(property.Name, "variables"))
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        _ = JsonSerializer.SerializeToElement(semantic, SerializerOptions).Deserialize<BpmnStructure>(SerializerOptions)
            ?? throw new InvalidOperationException("BPMN executable structure payload is invalid.");
        foreach (var field in structure.Payload.EnumerateObject().Where(property => StringComparer.OrdinalIgnoreCase.Equals(property.Name, "variables")))
            if (field.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                throw new InvalidOperationException("BPMN executable variables must be an array.");
        var payload = JsonNode.Parse(structure.Payload.GetRawText(), new JsonNodeOptions { PropertyNameCaseInsensitive = true })?.AsObject()
                      ?? throw new InvalidOperationException("BPMN executable structure payload is invalid.");
        // Placement has already normalized variables and rebased their typed initial bindings.
        // This owner rewrites only semantic activity references, retaining every other JSON field.
        foreach (var element in payload["elements"]?.AsArray() ?? [])
        {
            var item = element?.AsObject() ?? throw new InvalidOperationException("BPMN executable element is invalid.");
            foreach (var name in new[] { "childNodeId", "listenerNodeId" })
                if (item[name] is { } reference)
                    item[name] = Remap(reference.GetValue<string>(), authoredToExecutableNodeIds);
        }
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(payload, SerializerOptions));
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Variables;

    private static BpmnAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new BpmnAuthoredStructure();
        if (activity.Structure.Kind != BpmnProcessActivity.StructureKind || activity.Structure.SchemaVersion != BpmnProcessActivity.StructureSchemaVersion)
            throw new InvalidOperationException("Legacy BPMN authoring requires the exact kind and schema 1.0.0 before typed deserialization.");

        RejectCompleteSourceAuthority(activity.Structure.Payload, $"activity '{activity.NodeId}'");
        return activity.Structure.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)
               ?? new BpmnAuthoredStructure();
    }

    private static void RejectCompleteSourceAuthority(JsonElement payload, string context)
    {
        try { BpmnLegacyStructureGuard.RejectCompleteSourceFields(payload); }
        catch (JsonException exception)
        { throw new BpmnDocumentSourceException($"Invalid legacy BPMN {context}: {exception.Message}", exception); }
    }

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds)
    {
        // Preserve the BpmnElement constructor's normalization of these owned references only.
        nodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId.Trim();
        var result = nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }
}
