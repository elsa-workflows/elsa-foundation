namespace Elsa.Cluster.Core.Models;

/// <summary>
/// This process's member as it knows itself: its identity, its status, and whether it has concluded that others may
/// have counted it as expired (FR-007, MR-005). A lapsed member may keep writing only what it last observed as
/// finalized until it rejoins; a displaced one never rejoins.
/// </summary>
public sealed record LocalMemberStanding(ClusterMemberIdentity Identity, MemberStatus Status, MemberLapse? Lapse)
{
    public bool HasLapsed => Lapse is not null;
}

/// <summary>Why and when a member concluded that it lapsed.</summary>
public sealed record MemberLapse(MemberLapseReason Reason, DateTimeOffset ConcludedAt);

/// <summary>The conditions under which a member concludes that it lapsed (FR-007).</summary>
public enum MemberLapseReason
{
    /// <summary>Its expiry period passed since the start of its last successful heartbeat.</summary>
    ExpiryPassed,

    /// <summary>Its entry is missing from the store.</summary>
    EntryMissing,

    /// <summary>A later incarnation of its host id displaced it. It never rejoins.</summary>
    Displaced,

    /// <summary>It was displaced while its own heartbeats were still succeeding: another live process holds its host id.</summary>
    DuplicateHostId
}
