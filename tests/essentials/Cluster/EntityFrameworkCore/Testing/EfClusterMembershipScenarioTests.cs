using System.Collections.Concurrent;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Testing;

/// <summary>
/// Spec 183's user stories 2 and 4 against the EF provider, on a controllable clock, over whichever engine the deriving
/// class supplies. Each test is named after the acceptance scenario it proves. Members driven by the fixture show the
/// fleet semantics deterministically; the graceful stop and the live duplicate run through real hosts, because what they
/// prove is the provider's own lifecycle.
/// </summary>
public abstract class EfClusterMembershipScenarioTests(EfClusterMembershipTestStore store) : IAsyncDisposable
{
    private const string Family = "scenario-family";
    private static TimeSpan Margin => TimeSpan.FromMilliseconds(1);
    private readonly List<IHost> _hosts = [];

    protected EfClusterMembershipConformanceFixture Fixture { get; } = new(store);

    private ConformanceTimings Timings => Fixture.Timings;

    private TimeSpan LivenessWindow => Timings.ExpiryPeriod + Timings.SkewAllowance;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
            host.Dispose();
        await Fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [SkippableFact]
    public async Task US2_1_hosts_that_share_a_store_each_see_every_host_with_its_identity_status_and_report()
    {
        var hosts = new[]
        {
            await StartAsync(NewHostId("a"), Reads("1")),
            await StartAsync(NewHostId("b"), Reads("1", "2")),
            await StartAsync(NewHostId("c"), Reads("2"))
        };

        foreach (var reader in hosts)
        {
            var view = await FreshAsync(reader);
            Assert.Equal(ClusterProviderKind.Durable, view.ProviderKind);
            AssertIdentities(hosts.Select(Identity), view.Members);
            foreach (var subject in hosts)
            {
                var seen = view.Find(Identity(subject))!;
                Assert.Equal(MemberStatus.Active, seen.Status);
                Assert.True(seen is { IsLive: true, IsDisplaced: false });
                Assert.Equal((await subject.Membership.PublishReportAsync()).Report, seen.Report);
            }
        }
    }

    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task US2_2_and_3_a_host_that_stops_heartbeating_is_live_until_its_expiry_and_skew_pass_and_expired_after(int skewDirection)
    {
        var survivor = await StartAsync(NewHostId("survivor"), clockOffset: Timings.SkewAllowance * skewDirection);
        var stopped = await StartAsync(NewHostId("stopped"), Reads("1"));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await stopped.KillAsync();
        var expiresAfter = await ExpiryAsSeenByAsync(survivor, Identity(stopped));

        foreach (var instant in new[] { expiresAfter - Timings.HeartbeatInterval, expiresAfter - Margin, expiresAfter })
        {
            await AdvanceUntilAsync(survivor, instant);
            Assert.True((await SeenAsync(survivor, Identity(stopped)))!.IsLive, $"Judged expired at {instant:O}, before {expiresAfter:O}.");
            Assert.Contains(Identity(stopped), (await CountingAsync(survivor)).Failures.Select(failure => failure.Member.Identity));
        }

        await AdvanceUntilAsync(survivor, expiresAfter + Margin);
        var expired = (await SeenAsync(survivor, Identity(stopped)))!;
        Assert.False(expired.IsLive, "Still judged live after its expiry period and the skew allowance had passed.");
        Assert.Contains(expired.Conditions, condition => condition.Kind == MemberConditionKind.Lapsed);
        var counting = await CountingAsync(survivor);
        Assert.DoesNotContain(Identity(stopped), counting.Matches.Concat(counting.Failures.Select(failure => failure.Member)).Select(member => member.Identity));
    }

    [SkippableFact]
    public async Task US2_4_a_host_that_stops_gracefully_is_draining_while_it_stops_and_left_once_it_has_and_never_counted_after()
    {
        var observer = await StartAsync(NewHostId("observer"));
        FleetMember? whileStopping = null;
        var host = await StartHostAsync(Fixture.Clock, NewHostId("graceful"), services => services.AddHostedService(provider =>
            new StopProbe(async () =>
                whileStopping = await SeenAsync(observer, provider.GetRequiredService<IClusterMembership>().GetLocalStanding().Identity))));
        var member = host.Services.GetRequiredService<IClusterMembership>();
        var identity = member.GetLocalStanding().Identity;
        Assert.Equal(MemberStatus.Active, (await SeenAsync(observer, identity))!.Status);

        await host.StopAsync();

        Assert.True(whileStopping is { Status: MemberStatus.Draining, IsLive: true }, $"While stopping the host was {whileStopping?.Status}.");
        var stopped = (await SeenAsync(observer, identity))!;
        Assert.Equal(MemberStatus.Left, stopped.Status);
        Assert.False(stopped.IsLive);
        Assert.Equal(MemberStatus.Left, member.GetLocalStanding().Status);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        var counting = await CountingAsync(observer);
        Assert.DoesNotContain(identity, counting.Matches.Concat(counting.Failures.Select(failure => failure.Member)).Select(seen => seen.Identity));
    }

    [SkippableFact]
    public async Task US4_1_3_and_4_a_restart_after_the_earlier_incarnation_expired_joins_at_once_and_displaces_it()
    {
        var observer = await StartAsync(NewHostId("observer"), clockOffset: -Timings.SkewAllowance);
        var earlier = await StartAsync(NewHostId("restarting"), Reads("1"));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await earlier.KillAsync();
        // Past the earlier incarnation's expiry as the joiner judges it, and still within it for an observer whose
        // clock runs the whole skew allowance behind.
        await Fixture.AdvanceAsync(LivenessWindow + Margin);

        var later = await StartAsync(Identity(earlier).HostId, Reads("1", "2"));

        var view = await FreshAsync(observer);
        var displaced = view.Find(Identity(earlier))!;
        var current = view.Find(Identity(later))!;
        Assert.True(displaced is { IsDisplaced: true, IsLive: true }, "The earlier incarnation must be displaced and, to this observer, still live.");
        Assert.Contains(displaced.Conditions, condition => condition.Kind == MemberConditionKind.Displaced);
        Assert.True(current is { IsDisplaced: false, IsLive: true, Status: MemberStatus.Active });

        var placement = await observer.Membership.QueryAsync(MemberQuery.Placement(new ReadsSchemaVersion(Family, "1")), FleetReadMode.Fresh);
        AssertIdentities([Identity(later)], placement.Matches);
        Assert.DoesNotContain(Identity(earlier), placement.Failures.Select(failure => failure.Member.Identity));

        var counting = await observer.Membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, "2")), FleetReadMode.Fresh);
        AssertIdentities([Identity(earlier)], counting.Failures.Select(failure => failure.Member));
    }

    [SkippableFact]
    public async Task US4_2_a_restart_under_a_live_incarnation_is_refused_until_that_incarnation_has_gone_unrenewed_for_a_full_liveness_window()
    {
        var observer = await StartAsync(NewHostId("observer"));
        var earlier = await StartAsync(NewHostId("restarting"));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await earlier.KillAsync();
        var lastHeartbeat = (await SeenAsync(observer, Identity(earlier)))!.LastHeartbeatAt;
        var firstObservedAt = Fixture.Clock.GetUtcNow();

        IConformanceMember? restarted = null;
        var refusals = 0;
        while (restarted is null)
        {
            try
            {
                restarted = await Fixture.StartMemberAsync(new ConformanceMemberSetup(Identity(earlier).HostId, []));
            }
            catch (ClusterMembershipJoinRefusedException refusal)
            {
                refusals++;
                Assert.Equal(Identity(earlier).Incarnation, refusal.Incarnation);
                Assert.True(Fixture.Clock.GetUtcNow() <= lastHeartbeat + LivenessWindow, "A join was refused after the earlier incarnation had expired.");
                Assert.False((await SeenAsync(observer, Identity(earlier)))!.IsDisplaced, "A refused join disturbed the live incarnation.");
                await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
            }
        }

        var joinedAt = Fixture.Clock.GetUtcNow();
        Assert.True(refusals > 0);
        Assert.True(joinedAt > lastHeartbeat + LivenessWindow, "The join displaced an incarnation that was still live.");
        Assert.True(joinedAt <= firstObservedAt + LivenessWindow + Timings.HeartbeatInterval, "The joiner waited longer than one liveness window and one retry.");
        Assert.True((await SeenAsync(observer, Identity(earlier)))!.IsDisplaced);
        Assert.Equal(Identity(earlier).HostId, Identity(restarted).HostId);
    }

    [SkippableFact]
    public async Task US4_5_a_second_live_process_under_one_host_id_refuses_to_start_within_one_liveness_window_and_leaves_the_first_undisturbed()
    {
        var observer = await StartAsync(NewHostId("observer"));
        var hostId = NewHostId("claimed");
        var first = await StartHostAsync(Fixture.Clock, hostId);
        var firstMember = first.Services.GetRequiredService<IClusterMembership>();
        var incumbent = firstMember.GetLocalStanding().Identity;
        var logs = new CapturedLogs();

        var second = BuildHost(Fixture.Clock, hostId, logs: logs);
        var begunAt = Fixture.Clock.GetUtcNow();
        using var abandon = new CancellationTokenSource();
        var starting = second.StartAsync(abandon.Token);
        await AttemptedAsync(starting, logs, attempts: 1);
        var advances = (int)Math.Ceiling(LivenessWindow / Timings.HeartbeatInterval) + 2;
        for (var advance = 0; advance < advances && !starting.IsCompleted; advance++)
        {
            var attempts = logs.At(LogLevel.Warning).Count(IsRefusal) + 1;
            await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
            await HeartbeatLandedAsync(observer, incumbent);
            await AttemptedAsync(starting, logs, attempts);
        }

        if (!starting.IsCompleted)
        {
            await abandon.CancelAsync();
            Assert.Fail("The second process was still retrying its join past one liveness window: a live duplicate must refuse to start, never retry forever.");
        }

        var refusal = await Assert.ThrowsAsync<ClusterMembershipDuplicateHostIdException>(() => starting);
        Assert.True(Fixture.Clock.GetUtcNow() - begunAt <= LivenessWindow, "The duplicate was reported later than one liveness window after the attempt began.");
        Assert.Equal(hostId, refusal.HostId);
        Assert.Equal(MemberConditionKind.DuplicateHostId, refusal.Condition.Kind);
        Assert.Contains(hostId, refusal.Condition.Message, StringComparison.Ordinal);
        Assert.Contains(incumbent.Incarnation.ToString(), refusal.Condition.Message, StringComparison.Ordinal);
        var waitedOn = logs.At(LogLevel.Warning).Where(message => message.Contains(hostId, StringComparison.Ordinal) && message.Contains(incumbent.Incarnation.ToString(), StringComparison.Ordinal));
        Assert.NotEmpty(waitedOn);
        Assert.Contains(logs.At(LogLevel.Error), message => message.Contains(hostId, StringComparison.Ordinal));

        var standing = firstMember.GetLocalStanding();
        Assert.Equal(incumbent, standing.Identity);
        Assert.False(standing.HasLapsed);
        var sameHost = Assert.Single((await FreshAsync(observer)).Members, member => member.HostId == hostId);
        Assert.Equal(incumbent, sameHost.Identity);
        Assert.True(sameHost is { IsLive: true, IsDisplaced: false, Status: MemberStatus.Active });
    }

    /// <summary>
    /// FR-004b's race: two joins in flight for one host id, with no earlier incarnation and with an expired one to
    /// displace. The unique index on the current incarnation and the compare-and-set on the displaced one let exactly one
    /// through; the other is refused and writes nothing.
    /// </summary>
    [SkippableFact]
    public async Task FR004b_two_joins_racing_for_one_host_id_leave_exactly_one_current_incarnation()
    {
        for (var race = 0; race < 10; race++)
        {
            var hostId = NewHostId("raced");
            var displacing = race % 2 == 1;
            if (displacing)
                await Fixture.WriteRowAsync(Fixture.NewRow(hostId) with { HeartbeatAtUtcTicks = (Fixture.Clock.GetUtcNow() - TimeSpan.FromHours(1)).UtcTicks });
            var contenders = new[]
            {
                await Fixture.PrepareMemberAsync(new ConformanceMemberSetup(hostId, [])),
                await Fixture.PrepareMemberAsync(new ConformanceMemberSetup(hostId, []))
            };

            var outcomes = await Task.WhenAll(contenders.Select(async contender =>
            {
                try
                {
                    await contender.Membership.JoinAsync();
                    return (Contender: contender, Joined: true);
                }
                catch (ClusterMembershipJoinRefusedException)
                {
                    return (Contender: contender, Joined: false);
                }
            }));

            var winner = Assert.Single(outcomes, outcome => outcome.Joined).Contender;
            var rows = (await Fixture.ReadRowsAsync()).Where(row => row.HostId == hostId).ToArray();
            Assert.Equal(displacing ? 2 : 1, rows.Length);
            Assert.Equal(Identity(winner).Incarnation.Value, Assert.Single(rows, row => row.CurrentHostId is not null).Incarnation);
        }
    }

    [SkippableFact]
    public async Task US4_6_a_restart_under_a_new_host_id_never_displaces_the_old_entry_which_is_counted_until_it_expires()
    {
        var observer = await StartAsync(NewHostId("observer"));
        var old = await StartAsync(NewHostId("pod-1"), Reads("1"));
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await old.KillAsync();

        var renamed = await StartAsync(NewHostId("pod-2"), Reads("1", "2"));

        var seen = (await SeenAsync(observer, Identity(old)))!;
        Assert.True(seen is { IsDisplaced: false, IsLive: true });
        Assert.NotEqual(Identity(old).HostId, Identity(renamed).HostId);
        AssertIdentities([Identity(old)], (await CountingAsync(observer, "2")).Failures.Select(failure => failure.Member));

        await AdvanceUntilAsync(observer, await ExpiryAsSeenByAsync(observer, Identity(old)) + Margin);
        Assert.False((await SeenAsync(observer, Identity(old)))!.IsDisplaced);
        Assert.True((await CountingAsync(observer, "2")).EveryConsideredMemberMatches);
    }

    [SkippableFact]
    public async Task FR007_a_member_that_lapsed_rejoins_as_a_new_incarnation_once_its_earlier_entry_has_expired()
    {
        var observer = await StartAsync(NewHostId("observer"));
        var member = (EfClusterMembershipConformanceFixture.Member)await StartAsync(NewHostId("flaky"), Reads("1"));
        var lapsed = Identity(member);
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await member.IsolateAsync();

        await Fixture.AdvanceAsync(Timings.ExpiryPeriod);
        Assert.Equal(MemberLapseReason.ExpiryPassed, member.Membership.GetLocalStanding().Lapse?.Reason);
        Assert.True((await SeenAsync(observer, lapsed))!.IsLive, "Test setup: the lapsed entry must still be live to others.");

        // Back in reach while others may still count its earlier entry: the rejoin waits that entry out.
        member.Reconnect();
        await member.Membership.HeartbeatAsync();
        Assert.Equal(lapsed, Identity(member));
        Assert.True(member.Membership.GetLocalStanding().HasLapsed);
        Assert.False((await SeenAsync(observer, lapsed))!.IsDisplaced, "A rejoin displaced its own earlier incarnation while it was still live.");

        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        var standing = member.Membership.GetLocalStanding();
        Assert.NotEqual(lapsed, standing.Identity);
        Assert.Equal(lapsed.HostId, standing.Identity.HostId);
        Assert.False(standing.HasLapsed);
        var view = await FreshAsync(observer);
        Assert.True(view.Find(lapsed) is { IsDisplaced: true, IsLive: false });
        Assert.True(view.Find(standing.Identity) is { IsLive: true, IsDisplaced: false, Status: MemberStatus.Active });
    }

    [SkippableFact]
    public async Task FR007_a_displaced_member_lapses_and_never_rejoins()
    {
        var observer = await StartAsync(NewHostId("observer"));
        var member = (EfClusterMembershipConformanceFixture.Member)await StartAsync(NewHostId("cut-off"));
        var displacedIdentity = Identity(member);
        await member.IsolateAsync();
        await Fixture.AdvanceAsync(LivenessWindow + Timings.HeartbeatInterval);
        var successor = await StartAsync(displacedIdentity.HostId);

        member.Reconnect();
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval * 3);

        var standing = member.Membership.GetLocalStanding();
        Assert.Equal(displacedIdentity, standing.Identity);
        Assert.Equal(MemberLapseReason.Displaced, standing.Lapse?.Reason);
        var sameHost = (await FreshAsync(observer)).Members.Where(seen => seen.HostId == displacedIdentity.HostId).ToArray();
        Assert.Equal(2, sameHost.Length);
        Assert.Single(sameHost, seen => !seen.IsDisplaced && seen.Identity == Identity(successor));
    }

    /// <summary>
    /// FR-007, both ways, the case a member that only stays lapsed would hide: a host whose member finds itself displaced,
    /// as a later incarnation's join marks it, stops, saying so as critical, rather than keep working under an identity the
    /// fleet no longer counts; a host whose member only lapsed, here because its entry went missing, keeps running and
    /// rejoins as a new incarnation.
    /// </summary>
    [SkippableFact]
    public async Task FR007_a_host_whose_member_is_displaced_stops_and_one_whose_member_only_lapsed_keeps_running()
    {
        // The store is readied, and emptied, on first use: before the hosts join, not after.
        await Fixture.CountStoredEntriesAsync();
        var displacedLogs = new CapturedLogs();
        var displaced = BuildHost(Fixture.Clock, NewHostId("displaced"), logs: displacedLogs);
        await displaced.StartAsync();
        var lapsed = await StartHostAsync(Fixture.Clock, NewHostId("lapsed"));
        var displacedIdentity = displaced.Services.GetRequiredService<IClusterMembership>().GetLocalStanding().Identity;
        var lapsedMember = lapsed.Services.GetRequiredService<IClusterMembership>();
        var lapsedIdentity = lapsedMember.GetLocalStanding().Identity;

        await Fixture.WithStoreAsync(async context =>
        {
            // What a later incarnation's join writes over the earlier one (FR-004a), and an entry cleanup removed.
            await context.Members.Where(row => row.HostId == displacedIdentity.HostId).ExecuteUpdateAsync(row => row.SetProperty(entity => entity.CurrentHostId, (string?)null));
            return await context.Members.Where(row => row.HostId == lapsedIdentity.HostId).ExecuteDeleteAsync();
        });
        var stopping = displaced.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

        // The next heartbeat finds both: the one displaced, the other's entry missing.
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await ConditionAsync(() => stopping.IsCancellationRequested && lapsedMember.GetLocalStanding().HasLapsed);

        Assert.True(stopping.IsCancellationRequested, "A host whose member was displaced kept running.");
        Assert.Equal(MemberLapseReason.Displaced, displaced.Services.GetRequiredService<IClusterMembership>().GetLocalStanding().Lapse?.Reason);
        Assert.Contains(displacedLogs.At(LogLevel.Critical), message => message.Contains(displacedIdentity.HostId, StringComparison.Ordinal));
        Assert.Equal(MemberLapseReason.EntryMissing, lapsedMember.GetLocalStanding().Lapse?.Reason);

        // The one after rejoins the member that only lapsed, in a host that never stopped.
        await Fixture.AdvanceAsync(Timings.HeartbeatInterval);
        await ConditionAsync(() => !lapsedMember.GetLocalStanding().HasLapsed);

        Assert.False(lapsed.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested, "A host whose member only lapsed was stopped.");
        var rejoined = lapsedMember.GetLocalStanding();
        Assert.False(rejoined.HasLapsed);
        Assert.Equal(lapsedIdentity.HostId, rejoined.Identity.HostId);
        Assert.NotEqual(lapsedIdentity, rejoined.Identity);
    }

    private static string NewHostId(string name) => $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)];

    /// <summary>
    /// Waits, in real time, until <paramref name="condition"/> holds or a few seconds have passed: a host's heartbeat fires
    /// on the fake clock, but runs on the thread pool.
    /// </summary>
    private static async Task ConditionAsync(Func<bool> condition)
    {
        for (var poll = 0; poll < 100 && !condition(); poll++)
            await Task.Delay(TimeSpan.FromMilliseconds(50));
    }

    private static ReadabilityEntry Reads(params string[] versions) => new(Family, "ScenarioModule", versions);

    private static ClusterMemberIdentity Identity(IConformanceMember member) => member.Membership.GetLocalStanding().Identity;

    private async Task<IConformanceMember> StartAsync(string hostId, ReadabilityEntry? reads = null, TimeSpan clockOffset = default) =>
        await Fixture.StartMemberAsync(new ConformanceMemberSetup(hostId, reads is null ? [] : [reads], clockOffset));

    private static async Task<FleetView> FreshAsync(IConformanceMember reader) => await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh);

    private static async Task<FleetMember?> SeenAsync(IConformanceMember reader, ClusterMemberIdentity identity) => (await FreshAsync(reader)).Find(identity);

    private static async Task<MemberQueryAnswer> CountingAsync(IConformanceMember reader, string version = "3") =>
        await reader.Membership.QueryAsync(MemberQuery.Counting(new ReadsSchemaVersion(Family, version)), FleetReadMode.Fresh);

    private async Task<DateTimeOffset> ExpiryAsSeenByAsync(IConformanceMember reader, ClusterMemberIdentity subject)
    {
        var entry = (await SeenAsync(reader, subject))!;
        return MemberLiveness.ExpiresAfter(entry.LastHeartbeatAt, entry.ExpiryPeriod, Timings.SkewAllowance);
    }

    private async Task AdvanceUntilAsync(IConformanceMember reader, DateTimeOffset instant)
    {
        var delta = instant - reader.Clock.GetUtcNow();
        Assert.True(delta >= TimeSpan.Zero, $"Cannot move the clock back to {instant:O}.");
        await Fixture.AdvanceAsync(delta);
    }

    private static void AssertIdentities(IEnumerable<ClusterMemberIdentity> expected, IEnumerable<FleetMember> actual) =>
        Assert.Equal(
            expected.Select(identity => identity.ToString()).Order(StringComparer.Ordinal),
            actual.Select(member => member.Identity.ToString()).Order(StringComparer.Ordinal));

    /// <summary>A real host composing the provider on the fixture's clock, started.</summary>
    private async Task<IHost> StartHostAsync(TimeProvider clock, string hostId, Action<IServiceCollection>? compose = null)
    {
        var host = BuildHost(clock, hostId, compose);
        await host.StartAsync();
        return host;
    }

    private IHost BuildHost(TimeProvider clock, string hostId, Action<IServiceCollection>? compose = null, CapturedLogs? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Configure<ClusterMembershipOptions>(options => options.HostId = hostId);
        Fixture.ComposeProvider(builder.Services);
        compose?.Invoke(builder.Services);
        if (logs is not null)
            builder.Logging.AddProvider(logs);
        var host = builder.Build();
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// Waits, in real time, until a host's heartbeat for the fixture's current instant has committed: the host's timer
    /// fires on the fake clock, but its heartbeat runs on the thread pool.
    /// </summary>
    private async Task HeartbeatLandedAsync(IConformanceMember reader, ClusterMemberIdentity host)
    {
        var due = Fixture.Clock.GetUtcNow();
        for (var poll = 0; poll < 100; poll++)
        {
            if ((await SeenAsync(reader, host))?.LastHeartbeatAt >= due)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static bool IsRefusal(string message) => message.Contains("cannot join yet", StringComparison.Ordinal);

    /// <summary>
    /// Waits, in real time, until a starting host has made its <paramref name="attempts"/>th join attempt or finished
    /// starting: its retry delay runs on the fake clock, but the attempt itself on the thread pool.
    /// </summary>
    private static async Task AttemptedAsync(Task starting, CapturedLogs logs, int attempts)
    {
        for (var poll = 0; poll < 200 && !starting.IsCompleted && logs.At(LogLevel.Warning).Count(IsRefusal) < attempts; poll++)
            await Task.Delay(TimeSpan.FromMilliseconds(50));
    }

    /// <summary>Runs a check while the host stops: after every service's StoppingAsync, before the member leaves.</summary>
    private sealed class StopProbe(Func<Task> whileStopping) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => whileStopping();
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IEnumerable<string> At(LogLevel level) => _entries.Where(entry => entry.Level == level).Select(entry => entry.Message);

        public ILogger CreateLogger(string categoryName) => new Logger(_entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
