namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Safe causal facts contributed by a composite boundary fault.</summary>
public interface IActivityFaultCausation
{
    string CausalIncidentId { get; }
    string CausalActivityExecutionId { get; }
    string CausalExecutableNodeId { get; }
    string CausationKind { get; }
}
