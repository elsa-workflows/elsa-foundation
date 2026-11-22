namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A failure mode a member reports about itself and makes visible in its own fleet entry, naming the
/// hosts involved.
/// </summary>
public sealed record MemberCondition(MemberConditionKind Kind, IReadOnlyList<string> HostIds, string Message);

/// <summary>The failure modes a member reports, one kind each, so a report is traceable to a single failure mode.</summary>
public enum MemberConditionKind
{
    /// <summary>The member concluded that it lapsed.</summary>
    Lapsed,

    /// <summary>A later incarnation of the member's host id displaced it.</summary>
    Displaced,

    /// <summary>The member was displaced while its own heartbeats were still succeeding.</summary>
    DuplicateHostId,

    /// <summary>Another entry's heartbeat time is ahead of the member's clock by more than the skew allowance.</summary>
    ClockSkew,

    /// <summary>The member read an entry it cannot interpret.</summary>
    UninterpretableEntry,

    /// <summary>A fresh read of the fleet failed.</summary>
    FailedFreshRead
}
