using Elsa.Activities.Sequence.Exceptions;
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
        return new SequenceNavigator(slot?.Activities.ToArray() ?? []);
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
}
