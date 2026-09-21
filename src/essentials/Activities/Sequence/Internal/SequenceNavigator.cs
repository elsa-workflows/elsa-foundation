using Elsa.Activities.Sequence.Exceptions;
using Elsa.Activities.Sequence.Models;
using Elsa.Workflows.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Internal;

internal sealed class SequenceNavigator
{
    private readonly IReadOnlyDictionary<string, int> _indexesByNodeId;

    private SequenceNavigator(IReadOnlyList<ExecutableNode> children)
    {
        Children = children;
        _indexesByNodeId = children
            .Select((node, index) => new { node.ExecutableNodeId, Index = index })
            .ToDictionary(item => item.ExecutableNodeId, item => item.Index, StringComparer.Ordinal);
    }

    public IReadOnlyList<ExecutableNode> Children { get; }

    public static SequenceNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, SequenceActivity.ActivitiesSlotName));
        var children = slot?.Activities.ToArray() ?? [];
        if (children.Length == 0 && executableNode.Structure is null)
            return new SequenceNavigator([]);

        var orderedIds = ExecutableStructureReader.ReadStructure<SequenceExecutableStructure>(
            executableNode, "Sequence", SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion, Fail).Activities.ToArray();
        var duplicateStructureId = orderedIds.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateStructureId is not null)
            throw new SequenceExecutionException($"Sequence executable node '{executableNode.ExecutableNodeId}' structure contains duplicate child '{duplicateStructureId}'.");

        var duplicateChildId = children.GroupBy(child => child.ExecutableNodeId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateChildId is not null)
            throw new SequenceExecutionException($"Sequence executable node '{executableNode.ExecutableNodeId}' child slot '{SequenceActivity.ActivitiesSlotName}' contains duplicate child '{duplicateChildId}'.");

        var childrenById = children.ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
        var orderedChildren = orderedIds
            .Select(id => childrenById.TryGetValue(id, out var child)
                ? child
                : throw new SequenceExecutionException($"Sequence executable node '{executableNode.ExecutableNodeId}' structure references missing child '{id}'."))
            .ToArray();

        if (orderedChildren.Length != children.Length)
            throw new SequenceExecutionException($"Sequence executable node '{executableNode.ExecutableNodeId}' structure does not reference every projected child in slot '{SequenceActivity.ActivitiesSlotName}'.");

        return new SequenceNavigator(orderedChildren);
    }

    public ExecutableNode? SelectFirst() =>
        Children.Count == 0 ? null : Children[0];

    public ExecutableNode? SelectAfter(string completedChildExecutableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildExecutableNodeId);

        if (!_indexesByNodeId.TryGetValue(completedChildExecutableNodeId, out var index))
            throw new SequenceExecutionException($"Completed child executable node '{completedChildExecutableNodeId}' does not exist in child slot '{SequenceActivity.ActivitiesSlotName}'.");

        var nextIndex = index + 1;
        return nextIndex >= Children.Count ? null : Children[nextIndex];
    }

    private static SequenceExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

