using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static partial class RuntimeIngressReservationRules
{
    internal static IReadOnlyDictionary<string, IIngressReservationPolicy> Policies(IEnumerable<IIngressReservationPolicy>? policies)
    {
        var result = new Dictionary<string, IIngressReservationPolicy>(StringComparer.Ordinal);
        foreach (var policy in policies ?? [])
        {
            if (policy is null || string.IsNullOrWhiteSpace(policy.StimulusType) || !result.TryAdd(policy.StimulusType, policy))
                throw new InvalidOperationException("Exclusive ingress semantics require one policy owner per original stimulus type.");
        }
        return result;
    }

    internal static bool SamePolicies(IReadOnlyDictionary<string, IIngressReservationPolicy> first,
        IReadOnlyDictionary<string, IIngressReservationPolicy> second) => first.Count == second.Count &&
        first.All(pair => second.TryGetValue(pair.Key, out var other) && ReferenceEquals(pair.Value, other));

    internal static void CheckSemanticCandidates(IReadOnlyList<RuntimeIngressReservation> claims,
        IReadOnlyDictionary<string, IIngressReservationPolicy> policies)
    {
        for (var index = 0; index < claims.Count; index++)
        {
            var candidate = claims[index];
            if (!policies.TryGetValue(candidate.StimulusType, out var policy)) continue;
            var descriptor = Descriptor(candidate);
            _ = policy.Conflicts(descriptor, descriptor); // Validate even when the current namespace is empty.
            for (var other = index + 1; other < claims.Count; other++)
                if (claims[other].StimulusType == candidate.StimulusType && policy.Conflicts(descriptor, Descriptor(claims[other])))
                    throw new TriggerOperationException("route-conflict", "One publication proposes ambiguous exclusive endpoints.");
        }
    }

    internal static void CheckSemanticOwner(RuntimeIngressReservation existing, RuntimeIngressReservation candidate,
        IReadOnlyDictionary<string, IIngressReservationPolicy> policies)
    {
        if (existing.Scope.Persistence != candidate.Scope.Persistence || existing.StimulusType != candidate.StimulusType ||
            !policies.TryGetValue(candidate.StimulusType, out var policy)) return;
        if (!policy.Conflicts(Descriptor(existing), Descriptor(candidate))) return;
        var sameOwner = existing.OwnerKind == candidate.OwnerKind && existing.OwnerId == candidate.OwnerId &&
            (existing.Scope.TenantId == candidate.Scope.TenantId || existing.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner);
        if (!sameOwner) throw new TriggerOperationException("route-conflict", "Another source owns an equivalent endpoint reservation.");
    }

    internal static async ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ConflictingBindingsAsync(
        IWorkflowTriggerBindingStore bindings, RuntimeIngressReservation claim,
        IReadOnlyDictionary<string, IIngressReservationPolicy> policies, CancellationToken cancellationToken = default)
    {
        if (!policies.TryGetValue(claim.StimulusType, out var policy))
            return await bindings.ListAllByStimulusAsync(claim.StimulusType, claim.StimulusHash, cancellationToken);
        // Keep the ORIGINAL binding for physical-scope and same-slot checks; legacy rows need no new metadata stamp.
        var current = await bindings.ListAllByStimulusTypeAsync(claim.StimulusType, cancellationToken);
        return current.Where(binding => policy.Conflicts(Descriptor(claim),
            new(binding.StimulusType, binding.StimulusHash, binding.Metadata))).ToArray();
    }

    private static RuntimeIngressReservationDescriptor Descriptor(RuntimeIngressReservation value) =>
        new(value.StimulusType, value.StimulusHash, value.Metadata);
}
