using Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Hosting;

/// <summary>The exact child and executable selection prepared by a built-in actor owner.</summary>
public sealed record PendingChildBirth(string Tenant, ActorRef Actor, string Type, string TypeData, string Issuance);

/// <summary>How a trusted host settled a proposed batch of child births.</summary>
public enum ChildBirthProtectionOutcome
{
    /// <summary>The same prepared batch may be tried again.</summary>
    Retry,
    /// <summary>All selections are durably protected, or explicitly require no external protection.</summary>
    Protected,
    /// <summary>An issuance has been permanently disarmed. The owner must discard the prepared batch.</summary>
    Stale,
    /// <summary>An executable selection cannot be protected.</summary>
    Refused
}

/// <summary>
/// Protects every child before the built-in owner opens its checkpoint write transaction.
/// A retry presents the same immutable issuance for the child. Partial protection never authorizes the batch.
/// </summary>
public delegate ValueTask<ChildBirthProtectionOutcome> ProtectChildBirthsAsync(IReadOnlyList<PendingChildBirth> births,
    CancellationToken cancellationToken);

/// <summary>The durable disposition of an exact child birth, as proved by its issuing store.</summary>
public enum FoundationChildBirthProof
{
    /// <summary>The store cannot yet prove retirement or irrevocable absence.</summary>
    Unresolved,
    /// <summary>The exact child birth was durably retired.</summary>
    BirthRetired,
    /// <summary>The birth is absent and its issuing checkpoint is permanently fenced.</summary>
    IssuanceFencedAbsent
}
