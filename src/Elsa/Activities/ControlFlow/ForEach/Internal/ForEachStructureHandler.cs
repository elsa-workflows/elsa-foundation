using System.Text.Json;
using Elsa.Activities.ForEach.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>ForEach</c> activity's single body slot. Lives
/// in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the runtime <c>ForEach</c>
/// activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class ForEachStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => ForEachActivity.StructureKind;

    public string SchemaVersion => ForEachActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(ForEachActivity.BodySlotName, ToBody(structure.Body))];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var structure = new ForEachAuthoredStructure(SingleBody(childProjections, ForEachActivity.BodySlotName));

        return activity with
        {
            Structure = new ActivityNodeStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new ForEachExecutableStructure(authoredStructure.Body?.NodeId);

        return new ActivityNodeStructure(
            ForEachActivity.StructureKind,
            ForEachActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBody(ActivityNode? body) =>
        body is null ? [] : [body];

    private static ActivityNode? SingleBody(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static ForEachAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new ForEachAuthoredStructure();

        return activity.Structure.Payload.Deserialize<ForEachAuthoredStructure>(SerializerOptions)
               ?? new ForEachAuthoredStructure();
    }
}
