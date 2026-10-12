namespace Elsa.Cluster.Core.Models;

/// <summary>
/// One member of the fleet: a host id and one incarnation of it. A restarted host is a new member under the same host
/// id (ADR 0078, Decision).
/// </summary>
public sealed record ClusterMemberIdentity
{
    public ClusterMemberIdentity(string hostId, MemberIncarnation incarnation)
    {
        HostId = ClusterHostIdConstraints.Validate(hostId, nameof(hostId));
        Incarnation = incarnation ?? throw new ArgumentNullException(nameof(incarnation));
    }

    public string HostId { get; }

    public MemberIncarnation Incarnation { get; }

    public override string ToString() => $"{HostId} ({Incarnation})";
}
