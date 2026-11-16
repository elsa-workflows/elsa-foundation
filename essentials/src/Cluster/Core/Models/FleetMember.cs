namespace Elsa.Cluster.Core.Models;

/// <summary>
/// One member as a fleet view shows it: its identity, status and liveness as the reader judged them, and its
/// latest published report with that report's revision.
/// </summary>
/// <param name="IsLive">Whether the reader judges the member live: it has not left, and its last heartbeat plus its own
/// expiry period plus the reader's skew allowance has not passed (<see cref="MemberLiveness"/>).</param>
/// <param name="IsDisplaced">Whether a later incarnation of the same host id has joined. A displaced member that is still
/// live is counted, but never placed.</param>
/// <param name="Conditions">The failure modes the member reports about itself.</param>
public sealed record FleetMember(
    ClusterMemberIdentity Identity,
    MemberStatus Status,
    DateTimeOffset LastHeartbeatAt,
    TimeSpan ExpiryPeriod,
    bool IsLive,
    bool IsDisplaced,
    MemberReport Report,
    long ReportRevision,
    IReadOnlyList<MemberCondition> Conditions)
{
    public string HostId => Identity.HostId;
}
