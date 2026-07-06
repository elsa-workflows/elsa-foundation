namespace Elsa.Activities.Flowchart.Internal.Policies;

/// <summary>
/// Parallel join: fires once every inbound branch has arrived (the engine's implicit-join accounting), then
/// schedules its matching outbound connections. Branch <b>faults</b> are handled outside this policy: a faulted
/// inbound branch can never arrive, so the join would otherwise wait forever. The engine's
/// <c>FlowchartExecutionEngine.OnChildFaultedAsync</c> resolves that deterministically by faulting the whole
/// Flowchart (#308), mirroring the <c>Parallel</c> composite — rather than releasing the join as if the faulted
/// branch had simply taken an untaken outcome.
/// </summary>
public sealed class ParallelJoinFlowchartPolicy() : MatchingOutboundFlowchartPolicyBase(FlowchartPolicyKinds.ParallelJoin, "Parallel Join");
