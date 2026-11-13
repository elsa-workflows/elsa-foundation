using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Flowchart.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

internal sealed class FlowchartStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global web JSON options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => FlowchartActivity.StructureKind;

    public string SchemaVersion => FlowchartActivity.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(FlowchartAuthoredStructure);

    public bool SupportsScopedVariables => true;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(FlowchartActivity.ActivitiesSlotName, structure.Activities)];
    }

    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId)
    {
        var structure = ReadAuthoredStructure(activity);
        var children = structure.Activities
            .Select(child => child.NodeId)
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .ToArray();
        if (!children.Contains(producerChildNodeId, StringComparer.Ordinal) ||
            !children.Contains(consumerChildNodeId, StringComparer.Ordinal))
            return new(ActivityChildResultAvailability.Unavailable);

        return FlowchartChildResultAnalysis.TryCreate(structure)
                   ?.Assess(producerChildNodeId, consumerChildNodeId)
               ?? new ActivityChildResultFacts(ActivityChildResultAvailability.Unknown);
    }

    public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Connections
            .GroupBy(connection => connection.Source.NodeId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ActivityChildContractMemberUsage(
                group.Key,
                group.Select(connection => new ActivityContractMemberUsage(
                        "Outcome",
                        connection.Source.Port,
                        "Connected"))
                    .Distinct()
                    .OrderBy(usage => usage.ReferenceKey, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, FlowchartActivity.ActivitiesSlotName));
        var activities = slot?.Activities.ToArray() ?? [];
        var updated = new FlowchartAuthoredStructure(
            activities,
            current.Connections,
            current.StartNodeId,
            current.NodeMetadata,
            current.ConnectionMetadata,
            current.Variables);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(updated, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new FlowchartStructure(
            authoredStructure.Connections,
            authoredStructure.StartNodeId,
            authoredStructure.NodeMetadata,
            authoredStructure.ConnectionMetadata,
            authoredStructure.Variables);

        return new ActivityNodeStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var fields = Object(structure.Payload);
        // Structural placement changes only child references. Retain canonical/legacy variable
        // JSON and opaque metadata instead of deserializing the complete executable payload.
        var connectionsKey = Key(fields, "connections");
        if (connectionsKey is not null && fields[connectionsKey].ValueKind != JsonValueKind.Null)
        {
            if (fields[connectionsKey].ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Flowchart executable connections must be an array.");
            fields[connectionsKey] = Write(fields[connectionsKey].EnumerateArray().Select(value =>
            {
                // Keep the existing validation of the declared connection/endpoint contract,
                // but update its original JSON so unrelated endpoint and connection data survive.
                var connection = value.Deserialize<FlowchartConnection>(SerializerOptions)
                    ?? throw new InvalidOperationException("Flowchart executable connection is invalid.");
                var entry = Object(value);
                foreach (var (name, endpoint) in new[] { ("source", connection.Source), ("target", connection.Target) })
                {
                    var endpointKey = Key(entry, name)!;
                    var endpointFields = Object(entry[endpointKey]);
                    var nodeId = Remap(endpoint.NodeId, authoredToExecutableNodeIds);
                    ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
                    endpointFields[Key(endpointFields, "nodeId")!] = Write(nodeId);
                    entry[endpointKey] = Write(endpointFields);
                }
                return Write(entry);
            }).ToArray());
        }
        var startKey = Key(fields, "startNodeId");
        if (startKey is not null && fields[startKey].ValueKind != JsonValueKind.Null)
        {
            if (fields[startKey].ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Flowchart executable start identity must be a string.");
            fields[startKey] = Write(Remap(fields[startKey].GetString(), authoredToExecutableNodeIds));
        }
        var metadataKey = Key(fields, "nodeMetadata");
        if (metadataKey is not null && fields[metadataKey].ValueKind != JsonValueKind.Null)
            fields[metadataKey] = Write(Object(fields[metadataKey]).ToDictionary(
                entry => Remap(entry.Key, authoredToExecutableNodeIds)!, entry =>
                {
                    _ = entry.Value.Deserialize<FlowchartNodeMetadata>(SerializerOptions);
                    return entry.Value;
                }, StringComparer.Ordinal));
        foreach (var name in new[] { "variables", "connectionMetadata" })
        {
            var key = Key(fields, name);
            var kind = name == "variables" ? JsonValueKind.Array : JsonValueKind.Object;
            if (key is not null && fields[key].ValueKind != JsonValueKind.Null && fields[key].ValueKind != kind)
                throw new InvalidOperationException($"Flowchart executable {name} has an invalid shape.");
            if (name == "connectionMetadata" && key is not null && fields[key].ValueKind == JsonValueKind.Object)
                foreach (var entry in fields[key].EnumerateObject()) _ = entry.Value.Deserialize<FlowchartConnectionMetadata>(SerializerOptions);
        }
        return new ActivityNodeStructure(Kind, SchemaVersion, Write(fields));

        static Dictionary<string, JsonElement> Object(JsonElement value) => value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : throw new InvalidOperationException("Flowchart executable structure contains an invalid object.");
        static string? Key(Dictionary<string, JsonElement> values, string name) => values.Keys.SingleOrDefault(key => StringComparer.OrdinalIgnoreCase.Equals(key, name));
        static JsonElement Write<T>(T value) => JsonSerializer.SerializeToElement(value, SerializerOptions);
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Variables;

    private static FlowchartAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new FlowchartAuthoredStructure();

        return activity.Structure.Payload.Deserialize<FlowchartAuthoredStructure>(SerializerOptions)
               ?? new FlowchartAuthoredStructure();
    }

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
