namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A report as a member published it. The revision rises each time the member publishes a report that differs from
/// its previous one, so a later fresh read shows this revision or a higher one (FR-011).
/// </summary>
public sealed record PublishedMemberReport(ClusterMemberIdentity Member, MemberReport Report, long Revision);
