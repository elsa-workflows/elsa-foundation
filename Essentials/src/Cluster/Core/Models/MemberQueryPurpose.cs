namespace Elsa.Cluster.Core.Models;

/// <summary>
/// Why a member query is asked. The two purposes consider different members on purpose (FR-016): a wrong routing
/// decision is harmless because the fence at the commit rejects a stale writer, while a premature finalization is not.
/// </summary>
public enum MemberQueryPurpose
{
    /// <summary>
    /// Every live member that is joining, active or draining, displaced or not. A member whose report is unknown fails
    /// every requirement, so a doubtful member always blocks.
    /// </summary>
    Counting,

    /// <summary>
    /// Only the current incarnation of each host id, and only while it is active. Members that are displaced, doubtful
    /// or whose report is unknown are not considered.
    /// </summary>
    Placement
}
