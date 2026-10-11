using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Internal;

/// <summary>
/// Sequence implementation of the spec-123 D2 single-predecessor probe (ADR 0047 resolution #1). A sequence is a
/// linear chain — every child has at most one predecessor by construction — so any recognized successor answers with
/// its intrinsic inbound count: zero for the first child (the entry edge), one for every other child.
/// </summary>
public sealed class SequenceReplaySafeSuccessorRoutingProbe : IReplaySafeSuccessorRoutingProbe
{
    public int? TryGetInboundCount(ExecutableNode parentCompositeNode, string successorNodeId)
    {
        ArgumentNullException.ThrowIfNull(parentCompositeNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(successorNodeId);

        if (!StringComparer.Ordinal.Equals(parentCompositeNode.Structure?.Kind, SequenceActivity.StructureKind))
            return null;

        var navigator = parentCompositeNode.GetOrAddRoutingStructure(SequenceNavigator.From);
        var first = navigator.SelectFirst();
        if (first is not null && StringComparer.Ordinal.Equals(first.ExecutableNodeId, successorNodeId))
            return 0;

        return navigator.Children.Any(child => StringComparer.Ordinal.Equals(child.ExecutableNodeId, successorNodeId))
            ? 1
            : null;
    }
}
