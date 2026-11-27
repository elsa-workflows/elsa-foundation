namespace Kernel.Foundation.Contracts;

/// <summary>The effective tenant default and its durable revision. Revision zero keeps all statuses indefinitely.</summary>
public sealed record ActorRetentionPolicy(long Revision, ActorRetentionWindows Windows);

/// <summary>The outcome of comparing an expected tenant policy with the current durable policy.</summary>
public enum ActorRetentionPolicyChangeOutcome
{
    Applied,
    Unchanged,
    Stale,
    OverLimit
}

/// <summary>The effective policy after the attempt and the provider's newly eligible count.</summary>
public sealed record ActorRetentionPolicyChange(ActorRetentionPolicyChangeOutcome Outcome, ActorRetentionPolicy Current,
    ActorRetentionQuote Quote, string? Confirmation = null);

/// <summary>Trusted host access to the tenant default for this Foundation's configured actor type.</summary>
public interface IActorRetentionPolicies
{
    ValueTask<ActorRetentionPolicy> ReadRetentionPolicyAsync(BoundScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the policy only at the expected revision and if the freshly evaluated increase is within the approved count.
    /// An unchanged policy returns a zero count. An over-limit count returns a receipt bound to the current store and revision.
    /// </summary>
    ValueTask<ActorRetentionPolicyChange> TryApplyRetentionPolicyAsync(BoundScope scope, long expectedRevision,
        ActorRetentionWindows proposed, long automaticNewlyEligibleLimit, string? confirmation = null, CancellationToken cancellationToken = default);
}
