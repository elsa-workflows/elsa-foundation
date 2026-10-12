using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Xunit;

namespace Elsa.Cluster.Testing;

public abstract partial class ClusterMembershipConformanceTests
{
    /// <summary>
    /// FR-037 to FR-042: the diagnostics a member reports about a fleet entry. The in-process default never meets any
    /// of them (its class comment says why), so every test here requires a multi-member fixture.
    /// </summary>
    [SkippableFact]
    public async Task FR037_a_lapsed_member_is_reported_as_lapsed_in_its_own_fleet_entry()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var subject = await Fixture.StartMemberAsync(Setup("subject"));
        var identity = Identity(subject);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await subject.KillAsync();

        await AdvanceUntilAsync(observer, await ExpiryAsSeenByAsync(observer, identity) + Margin);
        var seen = await SeenAsync(observer, identity);

        Assert.NotNull(seen);
        Assert.Contains(seen!.Conditions, condition => condition.Kind == MemberConditionKind.Lapsed && condition.HostIds.Contains(identity.HostId));
    }

    [SkippableFact]
    public async Task FR038_a_displaced_member_is_reported_as_displaced()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer") with { ClockOffset = DisplacedButStillLiveObserverOffset });
        var earlier = await Fixture.StartMemberAsync(Setup("restarting"));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await KillAndAdvanceToDisplaceableAsync(observer, earlier);
        var identity = Identity(earlier);

        await Fixture.StartMemberAsync(Restart(earlier));
        var displaced = await SeenAsync(observer, identity);

        Assert.NotNull(displaced);
        Assert.Contains(displaced!.Conditions, condition => condition.Kind == MemberConditionKind.Displaced && condition.HostIds.Contains(identity.HostId));
    }

    /// <summary>
    /// The reference store's <c>Join</c> holds one lock across the liveness check and the displacement, so a live
    /// incumbent is never silently displaced (FR-004b): this reference provider never produces a
    /// <see cref="FleetMember"/> whose <see cref="MemberCondition"/> is <see cref="MemberConditionKind.DuplicateHostId"/>.
    /// FR-039 instead surfaces on the terminal <see cref="ClusterMembershipDuplicateHostIdException"/>, proven by
    /// <see cref="ClusterMembershipConformanceTests.FR004b_a_live_duplicate_that_keeps_renewing_fails_startup_within_one_liveness_window"/>.
    /// </summary>
    [SkippableFact]
    public Task FR039_a_duplicate_host_id_is_reported_when_displaced_while_still_heartbeating()
    {
        RequireMultipleMembers();
        Skip.If(true, "The reference store's join is race-free (one lock spans the liveness check and the displacement), " +
            "so this never shows up in a fleet entry; FR-039 is proven on the terminal exception instead, by " +
            "FR004b_a_live_duplicate_that_keeps_renewing_fails_startup_within_one_liveness_window.");
        return Task.CompletedTask;
    }

    [SkippableFact]
    public async Task FR040_an_entry_ahead_of_the_readers_clock_by_more_than_skew_is_reported()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer") with { ClockOffset = -(Timings.SkewAllowance * 2) });
        var subject = await Fixture.StartMemberAsync(Setup("subject"));
        var identity = Identity(subject);

        var seen = await SeenAsync(observer, identity);

        Assert.NotNull(seen);
        Assert.Contains(
            seen!.Conditions,
            condition => condition.Kind == MemberConditionKind.ClockSkew && condition.HostIds.Contains(identity.HostId) && condition.HostIds.Contains(Identity(observer).HostId));
    }

    [SkippableFact]
    public async Task FR041_an_uninterpretable_entry_is_reported()
    {
        RequireMultipleMembers();
        var reader = await Fixture.StartMemberAsync(Setup("reader", Reads("1")));
        var newerHost = NewHostId("newer");
        await Fixture.PlantUninterpretableEntryAsync(newerHost);

        var seen = Assert.Single((await FreshViewAsync(reader)).Members, member => member.HostId == newerHost);

        Assert.Contains(seen.Conditions, condition => condition.Kind == MemberConditionKind.UninterpretableEntry && condition.HostIds.Contains(newerHost));
    }

    [SkippableFact]
    public async Task FR042_a_failed_fresh_read_is_reported_on_the_next_successful_read()
    {
        RequireMultipleMembers();
        var subject = await Fixture.StartMemberAsync(Setup("subject"));
        var identity = Identity(subject);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await subject.IsolateAsync();

        await Assert.ThrowsAsync<ClusterMembershipReadException>(async () => await subject.Membership.ReadFleetAsync(FleetReadMode.Fresh));

        var recovered = await subject.Membership.ReadFleetAsync(FleetReadMode.Cached);
        var seen = recovered.Find(identity);
        Assert.NotNull(seen);
        Assert.Contains(seen!.Conditions, condition => condition.Kind == MemberConditionKind.FailedFreshRead && condition.HostIds.Contains(identity.HostId));

        var again = await subject.Membership.ReadFleetAsync(FleetReadMode.Cached);
        Assert.DoesNotContain(again.Find(identity)!.Conditions, condition => condition.Kind == MemberConditionKind.FailedFreshRead);
    }
}
