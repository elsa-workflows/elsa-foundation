using System.Text.Json;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Internal;

internal sealed class SequenceStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global web JSON options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => SequenceActivity.StructureKind;

    public string SchemaVersion => SequenceActivity.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(SequenceAuthoredStructure);

    public bool SupportsScopedVariables => true;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(SequenceActivity.ActivitiesSlotName, structure.Activities)];
    }

    public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId)
    {
        var children = ReadAuthoredStructure(activity).Activities.ToArray();
        var index = Array.FindIndex(children, child => StringComparer.Ordinal.Equals(child.NodeId, childNodeId));
        return index < 0 ? null : children.Take(index).Reverse().Select(child => child.NodeId).ToArray();
    }

    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId)
    {
        var children = ReadAuthoredStructure(activity).Activities.ToArray();
        var producer = Array.FindIndex(children, child => StringComparer.Ordinal.Equals(child.NodeId, producerChildNodeId));
        var consumer = Array.FindIndex(children, child => StringComparer.Ordinal.Equals(child.NodeId, consumerChildNodeId));
        if (producer < 0 || consumer < 0)
            return new(ActivityChildResultAvailability.Unavailable);

        // A Sequence runs each child once per run, in order.
        return new(producer < consumer ? ActivityChildResultAvailability.Definite : ActivityChildResultAvailability.Unavailable,
            IsCyclicBackEdge: false, MayRunConcurrently: false, AreMutuallyExclusive: false)
        { EarlierProducerRunCanReachConsumer = false };
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, SequenceActivity.ActivitiesSlotName));
        var activities = slot?.Activities.ToArray() ?? [];
        var structure = new SequenceAuthoredStructure(activities, current.Variables);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new SequenceExecutableStructure(
            authoredStructure.Activities.Select(child => child.NodeId).ToArray(),
            authoredStructure.Variables);

        return new ActivityNodeStructure(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        if (structure.Payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Sequence executable structure payload is invalid.");
        // Placement owns child identities only. Variables may already be canonical runtime
        // declarations, and neither those declarations nor opaque fields may be retyped here.
        var fields = structure.Payload.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var activitiesKey = Key("activities");
        if (activitiesKey is not null && fields[activitiesKey].ValueKind != JsonValueKind.Null)
        {
            var activities = fields[activitiesKey];
            if (activities.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Sequence executable activities must be an array.");
            fields[activitiesKey] = JsonSerializer.SerializeToElement(activities.EnumerateArray().Select(activity =>
                activity.ValueKind == JsonValueKind.String
                    ? Remap(activity.GetString()!, authoredToExecutableNodeIds)
                    : throw new InvalidOperationException("Sequence executable child identities must be strings.")).ToArray(), SerializerOptions);
        }
        var variablesKey = Key("variables");
        if (variablesKey is not null && fields[variablesKey].ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
            throw new InvalidOperationException("Sequence executable variables must be an array.");
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(fields, SerializerOptions));

        string? Key(string name) => fields.Keys.SingleOrDefault(key => StringComparer.OrdinalIgnoreCase.Equals(key, name));
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
        ReadAuthoredStructure(activity).Variables;

    private static SequenceAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new SequenceAuthoredStructure();

        return activity.Structure.Payload.Deserialize<SequenceAuthoredStructure>(SerializerOptions)
               ?? new SequenceAuthoredStructure();
    }

    private static string Remap(string nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
