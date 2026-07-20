using System.Text.Json;
using Elsa.Activities.For.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>For</c> activity's single body slot. Lives in
/// the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the runtime <c>For</c>
/// activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class ForStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => ForActivity.StructureKind;

    public string SchemaVersion => ForActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return [new ActivityChildProjection(ForActivity.BodySlotName, ToBody(structure.Body))];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var body = SingleBody(childProjections, ForActivity.BodySlotName);
        var structure = new ForAuthoredStructure(body);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new ForExecutableStructure(authoredStructure.Body?.NodeId);

        return new ActivityNodeStructure(
            ForActivity.StructureKind,
            ForActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBody(ActivityNode? body) =>
        body is null ? [] : [body];

    private static ActivityNode? SingleBody(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static ForAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new ForAuthoredStructure();

        return activity.Structure.Payload.Deserialize<ForAuthoredStructure>(SerializerOptions)
               ?? new ForAuthoredStructure();
    }
}
