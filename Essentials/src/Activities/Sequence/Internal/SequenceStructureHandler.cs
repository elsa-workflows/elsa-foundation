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
        var executable = structure.Payload.Deserialize<SequenceExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException("Sequence executable structure payload is invalid.");
        var remapped = new SequenceExecutableStructure(
            executable.Activities.Select(nodeId => Remap(nodeId, authoredToExecutableNodeIds)).ToArray(),
            executable.Variables);
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(remapped, SerializerOptions));
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
