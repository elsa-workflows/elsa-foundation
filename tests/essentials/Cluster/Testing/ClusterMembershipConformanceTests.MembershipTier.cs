using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Xunit;

namespace Elsa.Cluster.Testing;

public abstract partial class ClusterMembershipConformanceTests
{
    [SkippableFact]
    public async Task FR045_a_single_member_sees_itself()
    {
        var member = await Fixture.StartMemberAsync(Setup("solo", Reads("1")));
        var standing = member.Membership.GetLocalStanding();

        Assert.Equal(Fixture.ProviderKind, member.Membership.ProviderKind);
        Assert.Equal(MemberStatus.Active, standing.Status);
        Assert.False(standing.HasLapsed);
        foreach (var mode in new[] { FleetReadMode.Fresh, FleetReadMode.Cached })
        {
            var view = await member.Membership.ReadFleetAsync(mode);
            var self = Assert.Single(view.Members);
            Assert.Equal(Fixture.ProviderKind, view.ProviderKind);
            Assert.Equal(standing.Identity, self.Identity);
            Assert.Equal(MemberStatus.Active, self.Status);
            Assert.True(self.IsLive);
            Assert.False(self.IsDisplaced);
            Assert.Equal(new MemberReport(new ReadabilitySection([Reads("1")])), self.Report);
        }
    }

    [SkippableFact]
    public async Task FR046_members_that_share_a_store_see_each_other()
    {
        RequireMultipleMembers();
        var members = new[]
        {
            await Fixture.StartMemberAsync(Setup("a", Reads("1"))),
            await Fixture.StartMemberAsync(Setup("b", Reads("1", "2"))),
            await Fixture.StartMemberAsync(Setup("c", Reads("2")))
        };
        var expected = members.Select(Identity).ToArray();

        foreach (var reader in members)
        {
            var view = await FreshViewAsync(reader);
            AssertIdentities(expected, view.Members);
            foreach (var subject in members)
            {
                var seen = view.Find(Identity(subject))!;
                Assert.True(seen.IsLive);
                Assert.Equal(MemberStatus.Active, seen.Status);
                Assert.Equal((await SeenAsync(subject, Identity(subject)))!.Report, seen.Report);
            }
        }
    }

    [SkippableFact]
    public async Task FR011_a_publish_is_visible_to_the_publishing_member_at_once()
    {
        var member = await Fixture.StartMemberAsync(Setup("publisher", Reads("1")));
        var identity = Identity(member);

        member.SetReadability(Reads("1", "2"));
        var published = await member.Membership.PublishReportAsync();
        var seen = await SeenAsync(member, identity);

        Assert.Equal(identity, published.Member);
        Assert.Equal(new MemberReport(new ReadabilitySection([Reads("1", "2")])), published.Report);
        Assert.Equal(published.Report, seen!.Report);
        Assert.True(seen.ReportRevision >= published.Revision);

        member.SetReadability(Reads("2"));
        var changed = await member.Membership.PublishReportAsync();
        var unchanged = await member.Membership.PublishReportAsync();

        Assert.True(changed.Revision > published.Revision, "A changed report must publish a higher revision.");
        Assert.True(unchanged.Revision >= changed.Revision);
        Assert.Equal(changed.Report, (await SeenAsync(member, identity))!.Report);
    }

    [SkippableFact]
    public async Task FR047_a_publish_is_visible_to_the_next_fresh_read_on_another_member()
    {
        RequireMultipleMembers();
        var publisher = await Fixture.StartMemberAsync(Setup("publisher", Reads("1")));
        var reader = await Fixture.StartMemberAsync(Setup("reader"));

        for (var trial = 2; trial <= 21; trial++)
        {
            publisher.SetReadability(Reads("1", trial.ToString()));
            var published = await publisher.Membership.PublishReportAsync();
            var seen = await SeenAsync(reader, published.Member);

            Assert.NotNull(seen);
            Assert.Equal(published.Report, seen.Report);
            Assert.True(seen.ReportRevision >= published.Revision, $"Trial {trial}: revision {seen.ReportRevision} is older than the published {published.Revision}.");
        }
    }

    [SkippableFact]
    public async Task FR048_no_member_is_judged_expired_before_its_expiry_and_skew_have_passed_and_every_member_is_after()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var subject = await Fixture.StartMemberAsync(Setup("subject"));
        var identity = Identity(subject);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);

        await subject.KillAsync();
        var expiresAfter = await ExpiryAsSeenByAsync(observer, identity);

        await AdvanceUntilAsync(observer, expiresAfter - Margin);
        Assert.True((await SeenAsync(observer, identity))!.IsLive, "Judged expired before its expiry period and the skew allowance had passed.");

        await AdvanceUntilAsync(observer, expiresAfter + Margin);
        var expired = await SeenAsync(observer, identity);
        Assert.False(expired?.IsLive ?? false, "Still judged live after its expiry period and the skew allowance had passed.");
        Assert.DoesNotContain(
            (await AskAsync(observer, MemberQuery.Counting())).Matches,
            member => member.Identity == identity);
    }

    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FR049_a_member_concludes_it_lapsed_no_later_than_another_member_judges_it_expired(int skewDirection)
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var subject = await Fixture.StartMemberAsync(Setup("subject") with { ClockOffset = Timings.SkewAllowance * skewDirection });
        var identity = Identity(subject);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await subject.IsolateAsync();

        var step = Timings.HeartbeatInterval / 8;
        var limit = Timings.ExpiryPeriod + Timings.SkewAllowance * 3 + Timings.HeartbeatInterval * 2;
        var judgedExpired = false;
        for (var elapsed = TimeSpan.Zero; elapsed <= limit && !judgedExpired; elapsed += step)
        {
            await Fixture.AdvanceAsync(step);
            judgedExpired = !((await SeenAsync(observer, identity))?.IsLive ?? false);
            var lapsed = subject.Membership.GetLocalStanding().HasLapsed;
            Assert.True(!judgedExpired || lapsed, $"Judged expired {elapsed + step} after isolation, before the member concluded that it lapsed.");
        }

        Assert.True(judgedExpired, "The isolated member was never judged expired.");
    }

    [SkippableFact]
    public async Task FR050_a_displaced_incarnation_is_counted_until_it_expires_and_excluded_from_placement_at_once()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var earlier = await Fixture.StartMemberAsync(Setup("restarting", Reads("1")));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await earlier.KillAsync();
        await Fixture.AdvanceAsync(DisplacementDelay);

        var later = await Fixture.StartMemberAsync(Restart(earlier, Reads("1", "2")));
        var displaced = (await SeenAsync(observer, Identity(earlier)))!;
        var current = (await SeenAsync(observer, Identity(later)))!;

        Assert.Equal(Identity(earlier).HostId, Identity(later).HostId);
        Assert.NotEqual(Identity(earlier), Identity(later));
        Assert.True(displaced is { IsDisplaced: true, IsLive: true }, "The earlier incarnation must be displaced but still live.");
        Assert.False(current.IsDisplaced);

        var placement = await AskAsync(observer, MemberQuery.Placement(new ReadsSchemaVersion(Family, "1")));
        AssertIdentities([Identity(later)], placement.Matches);
        Assert.DoesNotContain(placement.Failures, failure => failure.Member.Identity == Identity(earlier));

        var counting = await AskAsync(observer, MemberQuery.Counting(new ReadsSchemaVersion(Family, "2")));
        AssertIdentities([Identity(earlier)], counting.Failures.Select(failure => failure.Member));

        await AdvanceUntilAsync(observer, await ExpiryAsSeenByAsync(observer, Identity(earlier)) + Margin);
        Assert.True((await AskAsync(observer, MemberQuery.Counting(new ReadsSchemaVersion(Family, "2")))).EveryConsideredMemberMatches);
    }

    [SkippableFact]
    public async Task FR004b_a_second_live_process_under_the_same_host_id_is_refused_and_the_first_is_undisturbed()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var first = await Fixture.StartMemberAsync(Setup("claimed"));
        var identity = Identity(first);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);

        var refusal = await Assert.ThrowsAsync<ClusterMembershipJoinRefusedException>(async () => await Fixture.StartMemberAsync(Restart(first)));
        Assert.Equal(identity.HostId, refusal.HostId);
        Assert.Contains(identity.HostId, refusal.Message, StringComparison.Ordinal);

        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        var standing = first.Membership.GetLocalStanding();
        Assert.Equal(identity, standing.Identity);
        Assert.False(standing.HasLapsed);
        var sameHost = (await FreshViewAsync(observer)).Members.Where(member => member.HostId == identity.HostId).ToArray();
        var seen = Assert.Single(sameHost);
        Assert.Equal(identity, seen.Identity);
        Assert.True(seen is { IsLive: true, IsDisplaced: false });
    }

    [SkippableFact]
    public async Task FR051_a_member_status_only_moves_forward()
    {
        RequireMultipleMembers();
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var subject = await Fixture.JoinMemberAsync(Setup("subject"));
        var identity = Identity(subject);
        var observed = new List<MemberStatus> { (await SeenAsync(observer, identity))!.Status };

        foreach (var transition in new Func<ValueTask>[] { subject.ActivateAsync, subject.BeginStopAsync, subject.StopAsync })
        {
            await transition();
            await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
            observed.Add((await SeenAsync(observer, identity))?.Status ?? MemberStatus.Left);
        }

        Assert.Equal(new[] { MemberStatus.Joining, MemberStatus.Active, MemberStatus.Draining, MemberStatus.Left }, observed);
        Assert.DoesNotContain((await AskAsync(observer, MemberQuery.Counting())).Matches, member => member.Identity == identity);
    }

    [SkippableFact]
    public async Task FR052_a_fleet_view_is_never_partial()
    {
        RequireMultipleMembers();
        var members = new List<IConformanceMember>();
        for (var index = 0; index < 5; index++)
            members.Add(await Fixture.StartMemberAsync(Setup($"m{index}")));
        var expected = members.Select(Identity).ToArray();

        for (var round = 0; round < 3; round++)
        {
            foreach (var reader in members)
                AssertIdentities(expected, (await FreshViewAsync(reader)).Members);
            await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        }

        await members[0].IsolateAsync();
        await Assert.ThrowsAsync<ClusterMembershipReadException>(async () => await FreshViewAsync(members[0]));
        AssertIdentities(expected, (await FreshViewAsync(members[1])).Members);
    }

    [SkippableFact]
    public async Task FR053_an_entry_the_reader_cannot_interpret_is_returned_as_unknown()
    {
        RequireMultipleMembers();
        var reader = await Fixture.StartMemberAsync(Setup("reader", Reads("1")));
        var newerHost = NewHostId("newer");
        await Fixture.PlantUninterpretableEntryAsync(newerHost);

        var unknown = Assert.Single((await FreshViewAsync(reader)).Members, member => member.HostId == newerHost);
        Assert.True(unknown.Report.IsUnknown);
        Assert.True(unknown.IsLive);

        var counting = await AskAsync(reader, MemberQuery.Counting(new ReadsSchemaVersion(Family, "1")));
        Assert.Equal(newerHost, Assert.Single(counting.Failures).Member.HostId);
        var placement = await AskAsync(reader, MemberQuery.Placement(new ReadsSchemaVersion(Family, "1")));
        Assert.DoesNotContain(placement.Matches.Concat(placement.Failures.Select(failure => failure.Member)), member => member.HostId == newerHost);
    }

    [SkippableFact]
    public async Task FR054_the_readability_query_answers_and_blocks_across_a_scripted_rolling_upgrade()
    {
        RequireMultipleMembers();
        var question = MemberQuery.Counting(new ReadsSchemaVersion(Family, "2"));
        var observer = await Fixture.StartMemberAsync(Setup("observer"));
        var a = await Fixture.StartMemberAsync(Setup("a", Reads("1")));
        var b = await Fixture.StartMemberAsync(Setup("b", Reads("1")));
        var c = await Fixture.StartMemberAsync(Setup("c", Reads("1")));
        async Task AssertBlockersAsync(string step, params IConformanceMember[] blockers)
        {
            var answer = await AskAsync(observer, question);
            AssertIdentities(blockers.Select(Identity), answer.Failures.Select(failure => failure.Member));
            Assert.True(answer.EveryConsideredMemberMatches == (blockers.Length == 0), step);
        }

        await AssertBlockersAsync("every member reads only 1", a, b, c);

        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await a.KillAsync();
        await Fixture.AdvanceAsync(DisplacementDelay);
        var upgradedA = await Fixture.StartMemberAsync(Restart(a, Reads("1", "2")));
        await AssertBlockersAsync("the displaced incarnation of a is still counted", a, b, c);

        await AdvanceUntilAsync(observer, await ExpiryAsSeenByAsync(observer, Identity(a)) + Margin);
        await AssertBlockersAsync("the displaced incarnation of a expired", b, c);

        await b.StopAsync();
        await AssertBlockersAsync("b left", c);
        var upgradedB = await Fixture.StartMemberAsync(Restart(b, Reads("1", "2")));
        await AssertBlockersAsync("b rejoined upgraded", c);

        await c.IsolateAsync();
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await AssertBlockersAsync("c lapsed but has not expired", c);
        await Fixture.AdvanceAsync(Timings.ExpiryPeriod + Timings.SkewAllowance);
        await AssertBlockersAsync("c expired");
        AssertIdentities([Identity(upgradedA), Identity(upgradedB), Identity(observer)], (await AskAsync(observer, question)).Matches);

        var newerHost = NewHostId("newer");
        await Fixture.PlantUninterpretableEntryAsync(newerHost);
        var blocked = await AskAsync(observer, question);
        var unknown = Assert.Single(blocked.Failures).Member;
        Assert.Equal(newerHost, unknown.HostId);
        Assert.True(unknown.Report.IsUnknown);
    }

    [SkippableFact]
    public async Task FR055_cleanup_never_deletes_a_live_entry()
    {
        RequireMultipleMembers();
        Skip.If(Fixture.CleanupPeriod is null, "The provider stores nothing to clean up.");
        var survivor = await Fixture.StartMemberAsync(Setup("survivor"));
        var crashed = await Fixture.StartMemberAsync(Setup("crashed"));
        var departed = await Fixture.StartMemberAsync(Setup("departed"));
        await crashed.KillAsync();
        await departed.StopAsync();

        var horizon = Fixture.CleanupPeriod!.Value + Timings.ExpiryPeriod + Timings.SkewAllowance + Timings.HeartbeatInterval * 2;
        for (var elapsed = TimeSpan.Zero; elapsed <= horizon; elapsed += Timings.HeartbeatInterval)
        {
            await Fixture.RunCleanupAsync();
            Assert.NotNull(await SeenAsync(survivor, Identity(survivor)));
            await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        }

        var recent = await Fixture.StartMemberAsync(Setup("recent"));
        await recent.KillAsync();
        await Fixture.RunCleanupAsync();
        var view = await FreshViewAsync(survivor);
        AssertIdentities([Identity(survivor), Identity(recent)], view.Members);
    }

    [SkippableFact]
    public async Task FR023_a_member_answers_the_readability_query_from_its_own_readable_set()
    {
        var member = await Fixture.StartMemberAsync(Setup("reader", new ReadabilityEntry(Family, "ConformanceModule", ["1", "2"], "database-a")));
        var identity = Identity(member);
        MemberQuery Reading(string version, string? database = null) => MemberQuery.Counting(new ReadsSchemaVersion(Family, version, database));

        Assert.True((await AskAsync(member, Reading("2"))).EveryConsideredMemberMatches);
        Assert.True((await AskAsync(member, Reading("2", "database-a"))).EveryConsideredMemberMatches);
        Assert.True((await AskAsync(member, Reading("3", "database-b"))).EveryConsideredMemberMatches, "A member that names another database is not counted.");

        var blocked = await AskAsync(member, Reading("3", "database-a"));
        var blocker = Assert.Single(blocked.Failures).Member;
        Assert.Equal(identity, blocker.Identity);
        Assert.Equal(new[] { "1", "2" }, Assert.Single(blocker.Report.Readability!.Entries).ReadableVersions);
    }
}
