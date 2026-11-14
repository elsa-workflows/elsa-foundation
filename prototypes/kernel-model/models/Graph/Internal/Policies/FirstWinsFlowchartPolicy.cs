namespace Kernel.Graph.Internal.Policies;

public sealed class FirstWinsFlowchartPolicy() : MatchingOutboundFlowchartPolicyBase(FlowchartPolicyKinds.FirstWins, "First Wins");
