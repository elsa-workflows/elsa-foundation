using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;

namespace Elsa.Activities.Flowchart.Internal;

public sealed class FlowchartPolicyRegistry : IFlowchartPolicyRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IFlowchartPolicy> _policies = new(StringComparer.Ordinal);

    public FlowchartPolicyRegistry(IEnumerable<IFlowchartPolicy> policies)
    {
        foreach (var policy in policies)
            Register(policy);
    }

    public IReadOnlyCollection<IFlowchartPolicy> Policies
    {
        get
        {
            lock (_syncRoot)
                return _policies.Values.ToArray();
        }
    }

    public bool TryGet(string policyKind, out IFlowchartPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyKind);

        lock (_syncRoot)
            return _policies.TryGetValue(policyKind.Trim(), out policy!);
    }

    public IFlowchartPolicy GetRequired(string policyKind)
    {
        if (TryGet(policyKind, out var policy))
            return policy;

        throw new FlowchartExecutionException($"Flowchart policy '{policyKind}' is not registered.");
    }

    public void Register(IFlowchartPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.PolicyKind);

        lock (_syncRoot)
        {
            if (_policies.ContainsKey(policy.PolicyKind))
                throw new FlowchartExecutionException($"Flowchart policy '{policy.PolicyKind}' is already registered.");

            _policies[policy.PolicyKind] = policy;
        }
    }
}
