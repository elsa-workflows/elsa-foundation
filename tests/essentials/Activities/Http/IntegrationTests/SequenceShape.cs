using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>The child slot and ordered structure a <see cref="SequenceActivity"/> node carries over its children.</summary>
internal static class SequenceShape
{
    public static ExecutableChildSlot[] ChildSlots(IReadOnlyList<ExecutableNode> children) =>
        [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)];

    public static ExecutableActivityStructure Structure(IReadOnlyList<ExecutableNode> children) =>
        new(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() }));
}
