using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Contracts;

/// <summary>
/// Decides Flowchart's <b>implicit</b> joins — the inference that is the whole of Flowchart's difficulty,
/// since the author declares no gateways (ADR 0064).
/// <para>
/// A join policy is an ordinary <see cref="IFlowchartPolicy"/> and lives in the same registry, so the two
/// semantics ship side by side and choosing between them is a configuration flip
/// (<see cref="FlowchartOptions.JoinPolicyKind"/>) rather than a fork of the engine. That is what lets the
/// conformance corpus run the same graphs through both and assert where they agree.
/// </para>
/// </summary>
public interface IFlowchartJoinPolicy : IFlowchartPolicy
{
    /// <summary>
    /// Whether a completing node emits <see cref="FlowchartArrivalStatus.Dead"/> arrivals on the outbound
    /// connections its outcomes did not take, and carries that deadness forward through any node whose every
    /// inbound arrived dead. A policy that derives deadness instead of representing it returns <c>false</c>,
    /// and the engine emits nothing on untaken edges.
    /// </summary>
    bool PropagatesDeadPaths { get; }

    /// <summary>
    /// Whether <see cref="FlowchartJoinContext.TargetNodeId"/> must wait for more inbound branches before it
    /// can be scheduled. Only ever asked about a target with more than one inbound connection.
    /// </summary>
    bool ShouldWait(FlowchartJoinContext context);
}
