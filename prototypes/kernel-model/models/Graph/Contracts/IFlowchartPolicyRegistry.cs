namespace Elsa.Activities.Flowchart.Contracts;

public interface IFlowchartPolicyRegistry
{
    IReadOnlyCollection<IFlowchartPolicy> Policies { get; }
    bool TryGet(string policyKind, out IFlowchartPolicy policy);
    IFlowchartPolicy GetRequired(string policyKind);
    void Register(IFlowchartPolicy policy);
}
