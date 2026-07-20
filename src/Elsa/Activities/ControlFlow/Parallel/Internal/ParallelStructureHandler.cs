using System.Text.Json;
using Elsa.Activities.Parallel.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Parallel.Internal;

/// <summary>
/// Design-side handler that projects and compiles the <c>Parallel</c> activity's per-branch child slots.
/// Lives in the activity module (which references <c>Elsa.Workflows.Design.Core</c>); the runtime
/// <c>Parallel</c> activity class references no Design types, preserving the Elsa §E2.2 split.
/// </summary>
internal sealed class ParallelStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => ParallelActivity.StructureKind;

    public string SchemaVersion => ParallelActivity.StructureSchemaVersion;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return structure.Branches
            .Select(branch => new ActivityChildProjection(ParallelActivity.BranchSlotName(branch.Name), ToBranch(branch.Activity)))
            .ToList();
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = ReadAuthoredStructure(activity);
        var branches = current.Branches
            .Select(branch => new ParallelAuthoredBranch(branch.Name, SingleBranch(childProjections, ParallelActivity.BranchSlotName(branch.Name))))
            .ToArray();
        var structure = new ParallelAuthoredStructure(branches, current.Threshold);

        return activity with
        {
            Structure = new ActivityNodeStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions))
        };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authoredStructure = ReadAuthoredStructure(activity);
        var executableStructure = new ParallelExecutableStructure(
            authoredStructure.Branches
                .Select(branch => new ParallelExecutableBranch(branch.Name, branch.Activity?.NodeId))
                .ToArray(),
            authoredStructure.Threshold);

        return new ActivityNodeStructure(
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(executableStructure, SerializerOptions));
    }

    private static IEnumerable<ActivityNode> ToBranch(ActivityNode? branch) =>
        branch is null ? [] : [branch];

    private static ActivityNode? SingleBranch(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName)
    {
        var slot = childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName));
        return slot?.Activities.FirstOrDefault();
    }

    private static ParallelAuthoredStructure ReadAuthoredStructure(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new ParallelAuthoredStructure();

        return activity.Structure.Payload.Deserialize<ParallelAuthoredStructure>(SerializerOptions)
               ?? new ParallelAuthoredStructure();
    }
}
