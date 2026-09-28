using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Exceptions;

/// <summary>The base of every failure the membership contract reports; providers wrap store failures in one (§2.23.5).</summary>
public abstract class ClusterMembershipException : Exception
{
    protected ClusterMembershipException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>A host's membership composition is invalid: two providers, two sources for one report section, or an
/// invalid setting. Raised at startup.</summary>
public sealed class ClusterMembershipConfigurationException(string message) : ClusterMembershipException(message);

/// <summary>
/// A fresh read could not return every member, so it returned none (FR-012). The caller must not treat the failure as
/// an empty or smaller fleet.
/// </summary>
public sealed class ClusterMembershipReadException(string message, Exception? innerException = null)
    : ClusterMembershipException(message, innerException);

/// <summary>
/// A join was refused because another live process holds the host id (FR-004b). The live incarnation is left
/// undisturbed; the operator must configure a distinct host id.
/// </summary>
/// <remarks>
/// The caller retries this refusal (FR-004b), so it recurs once per attempt while the wait lasts. This exception
/// carries the incarnation it is waiting on so each attempt can be reported, but it does not log anything itself:
/// whichever caller owns the retry loop — B2's hosted join, or any other shipped retry — MUST log a warning per
/// refused attempt naming the host id and this incarnation (#2097 review). The retry loop in this contract's own
/// conformance test is a test helper, not shipped code, and does not log.
/// </remarks>
public sealed class ClusterMembershipJoinRefusedException(string hostId, MemberIncarnation incarnation, DateTimeOffset lastHeartbeatAt)
    : ClusterMembershipException(
        $"Cannot join the cluster as host id '{hostId}': another live process (incarnation '{incarnation}', last " +
        $"heartbeat at {lastHeartbeatAt:O}) is already a member under it. Configure a distinct host id for this process.")
{
    public string HostId { get; } = hostId;

    /// <summary>The live incarnation this join is waiting on.</summary>
    public MemberIncarnation Incarnation { get; } = incarnation;

    /// <summary>The live incarnation's last heartbeat at the moment this join was refused.</summary>
    public DateTimeOffset LastHeartbeatAt { get; } = lastHeartbeatAt;
}

/// <summary>
/// A join was refused throughout one full liveness window (heartbeat, expiry and skew, FR-006), because the live
/// incumbent kept renewing the whole time: a live duplicate process holds the host id, not a crash to wait out
/// (FR-004b, FR-039). Unlike <see cref="ClusterMembershipJoinRefusedException"/>, the caller MUST NOT keep retrying;
/// retrying forever would hide an operator mistake instead of surfacing it.
/// </summary>
public sealed class ClusterMembershipDuplicateHostIdException(string hostId, MemberCondition condition)
    : ClusterMembershipException(
        $"Cannot join the cluster as host id '{hostId}': another live process has held it for a full liveness window " +
        "and kept renewing throughout it. This is a live duplicate, not a crashed process to wait out. Configure a " +
        "distinct host id for this process; do not keep retrying.")
{
    public string HostId { get; } = hostId;

    /// <summary>The FR-039 condition this failure reports.</summary>
    public MemberCondition Condition { get; } = condition;
}
