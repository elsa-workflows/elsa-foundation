namespace Elsa.Cluster.Core.Models;

/// <summary>
/// A member's status. It only moves forward within an incarnation, in declaration order (FR-005). A crashed member
/// never writes <see cref="Left"/>; it expires instead.
/// </summary>
public enum MemberStatus
{
    /// <summary>From joining until the host has started.</summary>
    Joining,

    /// <summary>The host has started and is serving.</summary>
    Active,

    /// <summary>The host has begun to stop.</summary>
    Draining,

    /// <summary>The host has stopped gracefully. A member that has left is never counted.</summary>
    Left
}
