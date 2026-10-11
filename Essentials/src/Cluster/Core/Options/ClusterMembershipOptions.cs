namespace Elsa.Cluster.Core.Options;

/// <summary>
/// Host-level membership settings. Membership is selected once per host, on the host container (ADR 0076 D9; spec
/// 183, Decisions, Q20), so these are bound there and never per shell.
/// </summary>
public sealed class ClusterMembershipOptions
{
    /// <summary>The configuration section a host binds these settings from.</summary>
    public const string SectionName = "Elsa:Cluster:Membership";

    /// <summary>
    /// The host id this process joins under. When unset, the in-process provider uses the machine name; a durable
    /// provider refuses to start without an explicit one (FR-003, FR-003a).
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>How often a member renews its entry (FR-006).</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long an entry stays live without renewal, carried on each entry. At least three heartbeat intervals, so one
    /// lost heartbeat does not make a member lapse (FR-006).
    /// </summary>
    public TimeSpan ExpiryPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How far apart two hosts' clocks may be while expiry and lapse stay ordered (FR-006, FR-008).</summary>
    public TimeSpan SkewAllowance { get; set; } = TimeSpan.FromSeconds(5);
}
