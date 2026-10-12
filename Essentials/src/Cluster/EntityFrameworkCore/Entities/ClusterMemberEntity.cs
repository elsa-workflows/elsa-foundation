namespace Elsa.Cluster.EntityFrameworkCore.Entities;

/// <summary>
/// One member of the fleet: one incarnation of one host id (spec 183, FR-026). A member writes only its own row, except
/// that a joining member marks the current incarnation of its host id displaced, and cleanup deletes rows. Every write is
/// a compare-and-set on <see cref="Revision"/>.
/// </summary>
public sealed class ClusterMemberEntity
{
    public string HostId { get; set; } = null!;

    public string Incarnation { get; set; } = null!;

    /// <summary>
    /// Equal to <see cref="HostId"/> while this row is its host id's current incarnation, and <see langword="null"/> once
    /// a later incarnation has displaced it. A unique index over it makes "at most one current incarnation per host id" a
    /// fact the database enforces, so two joins racing for one host id cannot both succeed (FR-004b).
    /// </summary>
    public string? CurrentHostId { get; set; }

    /// <summary>The member's status by name: joining, active, draining or left. It only moves forward (FR-005).</summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// The member's clock at the start of its last successful heartbeat, which is also the instant it measures its own
    /// lapse from (FR-008). Ticks, so every engine stores and compares it exactly.
    /// </summary>
    public long HeartbeatAtUtcTicks { get; set; }

    /// <summary>The entry's own expiry period, which readers judge it by (FR-006, FR-028).</summary>
    public long ExpiryPeriodTicks { get; set; }

    /// <summary>When the member left, on its own clock; what cleanup measures a departed entry from (FR-031).</summary>
    public long? LeftAtUtcTicks { get; set; }

    /// <summary>The member's latest published report.</summary>
    public string ReportJson { get; set; } = null!;

    /// <summary>Rises each time the member publishes a report that differs from its previous one (FR-011).</summary>
    public long ReportRevision { get; set; }

    /// <summary>The compare-and-set token of every write (FR-026). It only moves forward while the row exists.</summary>
    public long Revision { get; set; }

    /// <summary>The persisted-schema version of this row: <see cref="ClusterMembershipEfModule.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
}
