using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Tests.Core;

public sealed class MemberLivenessTests
{
    private static readonly DateTimeOffset LastHeartbeat = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Deadline = LastHeartbeat + Expiry + Skew;

    [Fact]
    public void A_member_stays_live_until_its_expiry_and_the_skew_allowance_have_passed()
    {
        Assert.Equal(Deadline, MemberLiveness.ExpiresAfter(LastHeartbeat, Expiry, Skew));
        Assert.True(MemberLiveness.IsLive(MemberStatus.Active, LastHeartbeat, Expiry, Skew, Deadline));
        Assert.False(MemberLiveness.IsLive(MemberStatus.Active, LastHeartbeat, Expiry, Skew, Deadline + TimeSpan.FromTicks(1)));
    }

    [Theory]
    [InlineData(MemberStatus.Joining)]
    [InlineData(MemberStatus.Draining)]
    public void Liveness_does_not_depend_on_status_before_leaving(MemberStatus status) =>
        Assert.True(MemberLiveness.IsLive(status, LastHeartbeat, Expiry, Skew, LastHeartbeat));

    [Fact]
    public void A_member_that_left_is_never_live() =>
        Assert.False(MemberLiveness.IsLive(MemberStatus.Left, LastHeartbeat, Expiry, Skew, LastHeartbeat));

    [Fact]
    public void A_member_lapses_once_its_expiry_period_has_passed()
    {
        Assert.False(MemberLiveness.HasLapsed(Expiry - TimeSpan.FromTicks(1), Expiry - TimeSpan.FromTicks(1), Expiry));
        Assert.True(MemberLiveness.HasLapsed(Expiry, Expiry, Expiry));
    }

    [Fact]
    public void The_wall_clock_catches_a_monotonic_clock_that_stood_still_while_suspended() =>
        Assert.True(MemberLiveness.HasLapsed(wallClockElapsed: Expiry, monotonicElapsed: TimeSpan.FromSeconds(1), Expiry));

    [Fact]
    public void The_monotonic_clock_catches_a_wall_clock_stepped_backwards() =>
        Assert.True(MemberLiveness.HasLapsed(wallClockElapsed: TimeSpan.FromSeconds(-10), monotonicElapsed: Expiry, Expiry));

    [Fact]
    public void A_member_lapses_no_later_than_a_reader_judges_it_expired_for_any_skew_within_the_allowance()
    {
        foreach (var offset in new[] { -Skew, TimeSpan.Zero, Skew })
        {
            var stampedByMember = LastHeartbeat + offset;
            var lapseInstant = LastHeartbeat + Expiry;
            var readerDeadline = MemberLiveness.ExpiresAfter(stampedByMember, Expiry, Skew);

            Assert.True(MemberLiveness.HasLapsed(lapseInstant - LastHeartbeat, lapseInstant - LastHeartbeat, Expiry));
            Assert.True(MemberLiveness.IsLive(MemberStatus.Active, stampedByMember, Expiry, Skew, lapseInstant), $"Offset {offset}: judged expired before the member lapsed.");
            Assert.True(readerDeadline >= lapseInstant);
        }
    }
}
