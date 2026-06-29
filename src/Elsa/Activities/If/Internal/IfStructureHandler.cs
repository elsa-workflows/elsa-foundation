using System.Text.Json;
using Elsa.Activities.If.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>If</c> activity's <c>Then</c> / <c>Else</c>
/// named branch slots. Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>);
/// the runtime <c>If</c> activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class IfStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Kind => IfActivity.StructureKind;

    public string SchemaVersion => IfActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return
        [
            new ActivityChildProjection(IfActivity.ThenSlotName, ToBranch(structure.Then)),
            new ActivityChildProjection(IfActivity.ElseSlotName, ToBranch(structure.Else))
        ];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var then = SingleBranch(childProjections, IfActivity.ThenSlotName);
        var @else = SingleBranch(childProjections, IfActivity.ElseSlotName);
        var structure = new IfAuthoredStructure(then, @else);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                IfActivity.StructureKind,
                IfActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new IfExecutableStructure(
            authoredStructure.Then?.NodeId,
            authoredStructure.Else?.NodeId);

        return new ActivityNodeStructure(
            IfActivity.StructureKind,
            IfActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBranch(ActivityNode? branch) =>
        branch is null ? [] : [branch];

    private static ActivityNode? SingleBranch(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static IfAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new IfAuthoredStructure();

        return activity.Structure.Payload.Deserialize<IfAuthoredStructure>(SerializerOptions)
               ?? new IfAuthoredStructure();
    }
}
