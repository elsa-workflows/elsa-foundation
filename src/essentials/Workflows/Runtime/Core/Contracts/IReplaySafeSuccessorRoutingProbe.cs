using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Answers the ADR 0047 ratification-resolution-#1 single-predecessor question for a successor edge inside a
/// ReplaySafe routing composite, without Runtime.Core referencing any composite's graph model. Each routing
/// composite assembly (Flowchart, Sequence) contributes an implementation that recognizes its own structure kind
/// and reads the spec-119 routing memo (<see cref="ExecutableNode.GetOrAddRoutingStructure{T}"/>) — no graph walk,
/// no second cache. The spec-123 D2 fused completion pump may only dispatch a successor <c>ScheduleActivity</c>
/// inline when some probe proves the successor has at most one inbound edge; an unanswered probe means the edge
/// cannot be proven single-predecessor and the discrete cascade stands (fan-in/join edges always fall back).
/// </summary>
public interface IReplaySafeSuccessorRoutingProbe
{
    /// <summary>
    /// Returns the number of inbound edges the successor node has within <paramref name="parentCompositeNode"/>,
    /// or <see langword="null"/> when this probe does not recognize the parent composite's structure. A composite's
    /// entry edge counts as zero inbound connections (trivially single-predecessor).
    /// </summary>
    int? TryGetInboundCount(ExecutableNode parentCompositeNode, string successorNodeId);
}
