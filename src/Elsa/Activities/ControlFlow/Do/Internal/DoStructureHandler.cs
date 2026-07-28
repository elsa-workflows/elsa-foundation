using System.Text.Json;
using Elsa.Activities.Do.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using DoActivity = Elsa.Activities.Do.Activities.Do;

namespace Elsa.Activities.Do.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>Do</c> activity's single <c>Body</c> named
/// branch slot. Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the
/// runtime <c>Do</c> activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class DoStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => DoActivity.StructureKind;

    public string SchemaVersion => DoActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return
        [
            new ActivityChildProjection(DoActivity.BodySlotName, ToBranch(structure.Body))
        ];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var body = SingleBranch(childProjections, DoActivity.BodySlotName);
        var structure = new DoAuthoredStructure(body);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                DoActivity.StructureKind,
                DoActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new DoExecutableStructure(authoredStructure.Body?.NodeId);

        return new ActivityNodeStructure(
            DoActivity.StructureKind,
            DoActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<DoExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException("Do executable structure payload is invalid.");
        var remapped = new DoExecutableStructure(Remap(executable.Body, authoredToExecutableNodeIds));
        return new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(remapped, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBranch(ActivityNode? branch) =>
        branch is null ? [] : [branch];

    private static ActivityNode? SingleBranch(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static DoAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new DoAuthoredStructure();

        return activity.Structure.Payload.Deserialize<DoAuthoredStructure>(SerializerOptions)
               ?? new DoAuthoredStructure();
    }

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
