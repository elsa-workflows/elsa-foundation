using System.Text.Json;
using Elsa.Activities.While.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.While.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>While</c> activity's single <c>Body</c> named
/// branch slot. Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the
/// runtime <c>While</c> activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class WhileStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Kind => WhileActivity.StructureKind;

    public string SchemaVersion => WhileActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return
        [
            new ActivityChildProjection(WhileActivity.BodySlotName, ToBranch(structure.Body))
        ];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var body = SingleBranch(childProjections, WhileActivity.BodySlotName);
        var structure = new WhileAuthoredStructure(body);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new WhileExecutableStructure(authoredStructure.Body?.NodeId);

        return new ActivityNodeStructure(
            WhileActivity.StructureKind,
            WhileActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBranch(ActivityNode? branch) =>
        branch is null ? [] : [branch];

    private static ActivityNode? SingleBranch(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static WhileAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new WhileAuthoredStructure();

        return activity.Structure.Payload.Deserialize<WhileAuthoredStructure>(SerializerOptions)
               ?? new WhileAuthoredStructure();
    }
}
