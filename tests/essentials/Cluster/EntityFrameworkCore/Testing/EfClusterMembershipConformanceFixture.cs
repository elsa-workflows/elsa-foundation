using System.Data.Common;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Entities;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Testing;

/// <summary>
/// A fleet of <see cref="EfClusterMembership"/> members over one real membership table, on clocks the suite controls
/// (spec 183, FR-044). Heartbeats run exactly when they fall due as <see cref="AdvanceAsync"/> moves every clock
/// together, through the provider's own heartbeat; nothing here reimplements what the provider does.
/// </summary>
/// <remarks>
/// <para>
/// Each member reaches the store through the provider's real context registration. Isolating a member makes every
/// store operation it attempts fail as an unreachable database would, with a <see cref="DbException"/> the provider
/// has to wrap. A member whose join was refused is kept as the same waiting process, so the next start under its host
/// id retries that process's join and its watch on the incumbent, exactly as the host's own join loop does.
/// </para>
/// <para>
/// The store is emptied before first use, so every fixture starts from a fleet of none on an engine that several tests
/// share.
/// </para>
/// </remarks>
public sealed class EfClusterMembershipConformanceFixture(EfClusterMembershipTestStore store) : IClusterMembershipConformanceFixture
{
    private readonly FakeTimeProvider _clock = new();
    private readonly List<Member> _created = [];
    private readonly Dictionary<string, Member> _waiting = new(StringComparer.Ordinal);
    private ServiceProvider? _root;
    private bool _ready;

    public string ProviderName => EfClusterMembershipServiceCollectionExtensions.ProviderName;

    public ClusterProviderKind ProviderKind
    {
        get
        {
            EnsureAvailable();
            return ClusterProviderKind.Durable;
        }
    }

    public bool SupportsMultipleMembers
    {
        get
        {
            EnsureAvailable();
            return true;
        }
    }

    public ConformanceTimings Timings { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    public TimeSpan? CleanupPeriod => Settings.CleanupPeriod;

    /// <summary>The clock every member's clock is offset from, and the one hosts composed here run on.</summary>
    public FakeTimeProvider Clock => _clock;

    /// <summary>The EF provider's settings for this fixture's store.</summary>
    public EfClusterMembershipOptions Settings { get; } = new()
    {
        Provider = store.Provider,
        ConnectionString = store.ConnectionString,
        CleanupPeriod = TimeSpan.FromMinutes(10)
    };

    /// <summary>
    /// Composes the provider exactly as a host does, through <see cref="EfClusterMembershipServiceCollectionExtensions.AddEfClusterMembership"/>,
    /// on this fixture's clock and timings. The host's own migrator applies the module's migrations as it starts.
    /// </summary>
    public void ComposeProvider(IServiceCollection services)
    {
        EnsureAvailable();
        services.AddSingleton<TimeProvider>(_clock);
        services.Configure<ClusterMembershipOptions>(ApplyTimings);
        services.AddEfClusterMembership(Settings);
    }

    public async ValueTask<int> CountStoredEntriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        return await WithContextAsync(context => context.Members.CountAsync(cancellationToken));
    }

    public async ValueTask<IConformanceMember> StartMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        var member = (Member)await JoinMemberAsync(setup, cancellationToken);
        await member.ActivateAsync();
        return member;
    }

    public async ValueTask<IConformanceMember> JoinMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var member = _waiting.Remove(setup.HostId, out var waiting) ? waiting : Create(setup);
        try
        {
            await member.Membership.JoinAsync(cancellationToken);
        }
        catch (ClusterMembershipJoinRefusedException)
        {
            _waiting[setup.HostId] = member;
            throw;
        }

        member.IsJoined = true;
        member.NextHeartbeatAt = _clock.GetUtcNow() + Timings.HeartbeatInterval;
        return member;
    }

    /// <summary>Creates a member that has not attempted to join, for a test that drives its join itself.</summary>
    public async Task<Member> PrepareMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        return Create(setup);
    }

    public async ValueTask AdvanceAsync(TimeSpan delta, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var target = _clock.GetUtcNow() + delta;
        while (_created.Where(member => member.IsRunning && member.NextHeartbeatAt <= target).Select(member => (DateTimeOffset?)member.NextHeartbeatAt).Min() is { } due)
        {
            MoveClocksTo(due);
            foreach (var member in _created.Where(member => member.IsRunning && member.NextHeartbeatAt <= due).ToArray())
            {
                await member.Membership.HeartbeatAsync(cancellationToken);
                member.NextHeartbeatAt += Timings.HeartbeatInterval;
            }
        }

        MoveClocksTo(target);
    }

    /// <summary>Writes a live entry stamped with a newer schema version, whose report is in a shape this build does not
    /// know, as a newer provider version would.</summary>
    public ValueTask PlantUninterpretableEntryAsync(string hostId, CancellationToken cancellationToken = default) =>
        new(WriteRowAsync(NewRow(hostId) with
        {
            Status = "Serving",
            ReportJson = """{"sections":[{"kind":"placement","roles":["worker"]}]}""",
            SchemaVersion = "2.0.0"
        }, cancellationToken));

    /// <summary>A current, live, active entry for <paramref name="hostId"/> as this build writes one, heartbeating now,
    /// for a test to alter before <see cref="WriteRowAsync"/>.</summary>
    public ClusterMemberRow NewRow(string hostId) => new(
        hostId,
        MemberIncarnation.New().Value,
        hostId,
        nameof(MemberStatus.Active),
        _clock.GetUtcNow().UtcTicks,
        Timings.ExpiryPeriod.Ticks,
        LeftAtUtcTicks: null,
        """{"readability":null}""",
        SchemaVersion: ClusterMembershipEfModule.SchemaVersion);

    /// <summary>Inserts a row directly, bypassing the provider, as another provider version or a damaged store would.</summary>
    public async Task WriteRowAsync(ClusterMemberRow row, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await WithContextAsync(context =>
        {
            context.Members.Add(new ClusterMemberEntity
            {
                HostId = row.HostId,
                Incarnation = row.Incarnation,
                CurrentHostId = row.CurrentHostId,
                Status = row.Status,
                HeartbeatAtUtcTicks = row.HeartbeatAtUtcTicks,
                ExpiryPeriodTicks = row.ExpiryPeriodTicks,
                LeftAtUtcTicks = row.LeftAtUtcTicks,
                ReportJson = row.ReportJson,
                ReportRevision = 1,
                Revision = 1,
                SchemaVersion = row.SchemaVersion
            });
            return context.SaveChangesAsync(cancellationToken);
        });
    }

    /// <summary>Runs the provider's own cleanup once, as a member on this fixture's clock would.</summary>
    public async ValueTask RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await NewMembership("janitor", _clock, readability: null, Root.GetRequiredService<IServiceScopeFactory>()).CleanupAsync(cancellationToken);
    }

    /// <summary>Reads the membership table directly, bypassing every provider judgement.</summary>
    public async Task<IReadOnlyList<ClusterMemberEntity>> ReadRowsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        return await WithContextAsync<IReadOnlyList<ClusterMemberEntity>>(async context =>
            await context.Members.AsNoTracking().ToListAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_root is not null)
            await _root.DisposeAsync();
        store.Dispose();
    }

    private void EnsureAvailable() => Skip.If(store.SkipReason is not null, store.SkipReason);

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (_ready)
            return;

        await WithContextAsync(async context =>
        {
            await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(store.Provider), EfMigratePolicy.AutoMigrate, cancellationToken);
            return await context.Members.ExecuteDeleteAsync(cancellationToken);
        });
        _ready = true;
    }

    private ServiceProvider Root => _root ??= BuildRoot();

    private ServiceProvider BuildRoot()
    {
        var services = new ServiceCollection();
        services.AddEfClusterMembership(Settings);
        return services.BuildServiceProvider();
    }

    private async Task<T> WithContextAsync<T>(Func<ClusterMembershipDbContext, Task<T>> run)
    {
        await using var scope = Root.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<ClusterMembershipDbContext>());
    }

    private Member Create(ConformanceMemberSetup setup)
    {
        var clock = new FakeTimeProvider(_clock.GetUtcNow() + setup.ClockOffset);
        var readability = setup.Readability is null ? null : new ConformanceReadabilitySource(setup.Readability);
        var member = new Member(clock, readability);
        member.Membership = NewMembership(setup.HostId, clock, readability, new IsolatableScopes(Root.GetRequiredService<IServiceScopeFactory>(), member));
        _created.Add(member);
        return member;
    }

    private EfClusterMembership NewMembership(string hostId, TimeProvider clock, IMemberReportSource<ReadabilitySection>? readability, IServiceScopeFactory scopes) =>
        new(
            scopes,
            Options.Create(Timed(new ClusterMembershipOptions { HostId = hostId })),
            Settings,
            readability is null ? [] : [readability],
            clock,
            NullLogger<EfClusterMembership>.Instance);

    private ClusterMembershipOptions Timed(ClusterMembershipOptions options)
    {
        ApplyTimings(options);
        return options;
    }

    private void ApplyTimings(ClusterMembershipOptions options)
    {
        options.HeartbeatInterval = Timings.HeartbeatInterval;
        options.ExpiryPeriod = Timings.ExpiryPeriod;
        options.SkewAllowance = Timings.SkewAllowance;
    }

    private void MoveClocksTo(DateTimeOffset instant)
    {
        var delta = instant - _clock.GetUtcNow();
        if (delta <= TimeSpan.Zero)
            return;

        _clock.Advance(delta);
        foreach (var member in _created)
            member.Clock.Advance(delta);
    }

    /// <summary>One member of the fleet: one process, with its own clock and its own reach to the store.</summary>
    public sealed class Member(FakeTimeProvider clock, ConformanceReadabilitySource? readability) : IConformanceMember
    {
        public EfClusterMembership Membership { get; internal set; } = null!;

        IClusterMembership IConformanceMember.Membership => Membership;

        public FakeTimeProvider Clock => clock;

        TimeProvider IConformanceMember.Clock => clock;

        public bool IsIsolated { get; private set; }

        public bool IsKilled { get; private set; }

        internal bool IsJoined { get; set; }

        internal DateTimeOffset NextHeartbeatAt { get; set; }

        internal bool IsRunning => IsJoined && !IsKilled && Membership.GetLocalStanding().Status != MemberStatus.Left;

        public void SetReadability(params ReadabilityEntry[] entries) =>
            (readability ?? throw new InvalidOperationException("This member has no readability source.")).Set(entries);

        public ValueTask ActivateAsync() => new(Membership.ActivateAsync());

        public ValueTask BeginStopAsync() => new(Membership.DrainAsync());

        public ValueTask StopAsync() => new(Membership.LeaveAsync());

        public ValueTask KillAsync()
        {
            IsKilled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask IsolateAsync()
        {
            IsIsolated = true;
            return ValueTask.CompletedTask;
        }

        /// <summary>The process reaches the store again.</summary>
        public void Reconnect() => IsIsolated = false;
    }

    /// <summary>Opens the member's contexts, unless the member is cut off from the store.</summary>
    private sealed class IsolatableScopes(IServiceScopeFactory inner, Member member) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => member.IsIsolated ? throw new StoreUnreachableException() : inner.CreateScope();
    }
}

/// <summary>One membership row, as a test writes it directly.</summary>
public sealed record ClusterMemberRow(
    string HostId,
    string Incarnation,
    string? CurrentHostId,
    string Status,
    long HeartbeatAtUtcTicks,
    long ExpiryPeriodTicks,
    long? LeftAtUtcTicks,
    string ReportJson,
    string SchemaVersion);

/// <summary>What an isolated member's store operations fail with: a provider failure, as an unreachable database raises.</summary>
public sealed class StoreUnreachableException() : DbException("The membership store is unreachable from this member.");
