using Elsa.Workflows.Runtime.Core.Contracts.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Startup-validated lookup for exact compiled operator-scheduling policy identities.</summary>
public sealed class OperatorActivitySchedulingPolicyRegistry : IOperatorActivitySchedulingPolicyRegistry
{
    private readonly IReadOnlyDictionary<(string PolicyKey, int SchemaVersion), IOperatorActivitySchedulingPolicy> _policies;

    public OperatorActivitySchedulingPolicyRegistry(IEnumerable<IOperatorActivitySchedulingPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        _policies = policies.ToDictionary(
            policy => (policy.PolicyKey, policy.SchemaVersion),
            policy => policy,
            new KeyComparer());
    }

    public IOperatorActivitySchedulingPolicy? Find(string policyKey, int schemaVersion) =>
        _policies.GetValueOrDefault((policyKey, schemaVersion));

    private sealed class KeyComparer : IEqualityComparer<(string PolicyKey, int SchemaVersion)>
    {
        public bool Equals((string PolicyKey, int SchemaVersion) left, (string PolicyKey, int SchemaVersion) right) =>
            left.SchemaVersion == right.SchemaVersion && StringComparer.Ordinal.Equals(left.PolicyKey, right.PolicyKey);

        public int GetHashCode((string PolicyKey, int SchemaVersion) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.PolicyKey), value.SchemaVersion);
    }
}
