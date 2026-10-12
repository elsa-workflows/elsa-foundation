namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The liveness rules every provider applies, so that expiry and lapse stay ordered the same way everywhere
/// (FR-006 to FR-008).
/// </summary>
/// <remarks>
/// A member stamps its heartbeat from its own clock at the start of the heartbeat it measures its lapse from. It
/// concludes that it lapsed once its expiry period has passed since then; a reader judges it expired only once the
/// expiry period plus the skew allowance has passed on the reader's clock. For any skew between the two clocks within
/// the allowance, the member therefore knows it lapsed no later than anyone counts it as expired. At the boundary
/// instant both rules take the conservative side: the member has lapsed, and the reader still counts it.
/// </remarks>
public static class MemberLiveness
{
    /// <summary>The first instant, on a reader's clock, at which a member whose entry was last renewed at
    /// <paramref name="lastHeartbeatAt"/> may be judged expired.</summary>
    public static DateTimeOffset ExpiresAfter(DateTimeOffset lastHeartbeatAt, TimeSpan expiryPeriod, TimeSpan skewAllowance) =>
        lastHeartbeatAt + expiryPeriod + skewAllowance;

    /// <summary>Whether a reader judges a member live at <paramref name="judgedAt"/> on the reader's clock.</summary>
    public static bool IsLive(MemberStatus status, DateTimeOffset lastHeartbeatAt, TimeSpan expiryPeriod, TimeSpan skewAllowance, DateTimeOffset judgedAt) =>
        status != MemberStatus.Left && judgedAt <= ExpiresAfter(lastHeartbeatAt, expiryPeriod, skewAllowance);

    /// <summary>
    /// Whether a member has lapsed, given the time elapsed since the start of its last successful heartbeat measured by
    /// the wall clock and by a monotonic clock. The larger of the two counts: the wall clock catches a suspended VM
    /// whose monotonic clock stood still, and the monotonic clock catches a wall clock stepped backwards (FR-007).
    /// </summary>
    public static bool HasLapsed(TimeSpan wallClockElapsed, TimeSpan monotonicElapsed, TimeSpan expiryPeriod) =>
        (wallClockElapsed > monotonicElapsed ? wallClockElapsed : monotonicElapsed) >= expiryPeriod;
}
