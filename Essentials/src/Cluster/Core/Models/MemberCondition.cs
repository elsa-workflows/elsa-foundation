namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A failure mode a member reports about itself and makes visible in its own fleet entry (FR-037 to FR-042), naming the
/// hosts involved.
/// </summary>
public sealed record MemberCondition(MemberConditionKind Kind, IReadOnlyList<string> HostIds, string Message);

/// <summary>The failure modes of FR-037 to FR-042, one each, so a report is traceable to a single requirement.</summary>
public enum MemberConditionKind
{
    /// <summary>The member concluded that it lapsed (FR-037).</summary>
    Lapsed,

    /// <summary>A later incarnation of the member's host id displaced it (FR-038).</summary>
    Displaced,

    /// <summary>The member was displaced while its own heartbeats were still succeeding (FR-039).</summary>
    DuplicateHostId,

    /// <summary>Another entry's heartbeat time is ahead of the member's clock by more than the skew allowance (FR-040).</summary>
    ClockSkew,

    /// <summary>The member read an entry it cannot interpret (FR-041).</summary>
    UninterpretableEntry,

    /// <summary>A fresh read of the fleet failed (FR-042).</summary>
    FailedFreshRead
}
