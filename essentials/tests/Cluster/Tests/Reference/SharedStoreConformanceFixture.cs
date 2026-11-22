using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Cluster.Tests.Reference;

/// <summary>
/// A fleet of <see cref="SharedStoreClusterMembership"/> members over one store. Heartbeats run exactly when they fall
/// due as the fixture advances every member's clock together.
/// </summary>
public sealed class SharedStoreConformanceFixture(SharedStoreFault fault = SharedStoreFault.None) : IClusterMembershipConformanceFixture
{
    public const string Name = "shared-store";

    private readonly SharedMembershipStore _store = new();
    // After 2020: the runtime tier composes the distributed runtime on these clocks, and its identities need one.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly List<Member> _members = [];

    public string ProviderName => Name;

    public ClusterProviderKind ProviderKind => ClusterProviderKind.Durable;

    public bool SupportsMultipleMembers => true;

    public ConformanceTimings Timings { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    public TimeSpan? CleanupPeriod => TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registers the provider the way a durable provider must: through its registration, sharing one member between
    /// every container built from the host's registrations, and joining while the host starts.
    /// </summary>
    public void ComposeProvider(IServiceCollection services)
    {
        var holder = new HostMember(this);
        if (fault == SharedStoreFault.LastWriteWins)
        {
            foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(IClusterMembership) || descriptor.ServiceType == typeof(ClusterMembershipProviderRegistration)).ToArray())
                services.Remove(existing);
        }

        services.AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(
            Name,
            ClusterProviderKind.Durable,
            ServiceDescriptor.Singleton<IClusterMembership>(holder.Resolve)));
        services.AddHostedService(holder.JoinOnStart);
    }

    public ValueTask<int> CountStoredEntriesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_store.Count);

    public async ValueTask<IConformanceMember> StartMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        var member = await JoinMemberAsync(setup, cancellationToken);
        await member.ActivateAsync();
        return member;
    }

    public async ValueTask<IConformanceMember> JoinMemberAsync(ConformanceMemberSetup setup, CancellationToken cancellationToken = default)
    {
        var clock = new FakeTimeProvider(_clock.GetUtcNow() + setup.ClockOffset);
        var readability = setup.Readability is null ? null : new ConformanceReadabilitySource(setup.Readability);
        var runnability = setup.Runnability is null ? null : new ConformanceRunnabilitySource(setup.Runnability);
        var membership = Create(setup.HostId, clock, readability, runnability);
        await membership.JoinAsync(cancellationToken);

        var member = new Member(membership, clock, readability, runnability) { NextHeartbeatAt = _clock.GetUtcNow() + Timings.HeartbeatInterval };
        _members.Add(member);
        return member;
    }

    public async ValueTask AdvanceAsync(TimeSpan delta, CancellationToken cancellationToken = default)
    {
        var target = _clock.GetUtcNow() + delta;
        while (_members.Where(member => member.IsRunning && member.NextHeartbeatAt <= target).Select(member => (DateTimeOffset?)member.NextHeartbeatAt).Min() is { } due)
        {
            MoveClocksTo(due);
            foreach (var member in _members.Where(member => member.IsRunning && member.NextHeartbeatAt <= due).ToArray())
            {
                await member.Membership.HeartbeatAsync(cancellationToken);
                member.NextHeartbeatAt += Timings.HeartbeatInterval;
            }
        }

        MoveClocksTo(target);
    }

    public ValueTask PlantUninterpretableEntryAsync(string hostId, CancellationToken cancellationToken = default)
    {
        _store.PlantUninterpretable(hostId, _clock.GetUtcNow(), Timings.ExpiryPeriod);
        return ValueTask.CompletedTask;
    }

    public ValueTask RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        _store.Cleanup(_clock.GetUtcNow(), CleanupPeriod!.Value, Timings.SkewAllowance);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private SharedStoreClusterMembership Create(
        string hostId,
        TimeProvider clock,
        IMemberReportSource<ReadabilitySection>? readability,
        IMemberReportSource<RunnabilitySection>? runnability) =>
        new(_store, hostId, Timings, clock, readability, runnability, fault);

    private void MoveClocksTo(DateTimeOffset instant)
    {
        var delta = instant - _clock.GetUtcNow();
        if (delta <= TimeSpan.Zero)
            return;

        _clock.Advance(delta);
        foreach (var member in _members)
            member.Clock.Advance(delta);
    }

    private sealed class Member(
        SharedStoreClusterMembership membership,
        FakeTimeProvider clock,
        ConformanceReadabilitySource? readability,
        ConformanceRunnabilitySource? runnability) : IConformanceMember
    {
        public SharedStoreClusterMembership Membership => membership;

        IClusterMembership IConformanceMember.Membership => membership;

        public FakeTimeProvider Clock => clock;

        TimeProvider IConformanceMember.Clock => clock;

        public DateTimeOffset NextHeartbeatAt { get; set; }

        public bool IsRunning => !membership.IsKilled && membership.Status != MemberStatus.Left;

        public void SetReadability(params ReadabilityEntry[] entries) =>
            (readability ?? throw new InvalidOperationException("This member has no readability source.")).Set(entries);

        public void SetRunnability(params RunnabilityEntry[] entries) =>
            (runnability ?? throw new InvalidOperationException("This member has no runnability source.")).Set(entries);

        public ValueTask ActivateAsync() => Transition(MemberStatus.Active);

        public ValueTask BeginStopAsync() => Transition(MemberStatus.Draining);

        public ValueTask StopAsync() => Transition(MemberStatus.Left);

        public ValueTask KillAsync()
        {
            membership.Kill();
            return ValueTask.CompletedTask;
        }

        public ValueTask IsolateAsync()
        {
            membership.Isolate();
            return ValueTask.CompletedTask;
        }

        private ValueTask Transition(MemberStatus status)
        {
            membership.SetStatus(status);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The one member a host's registrations share, created from the host's settings on first use.</summary>
    private sealed class HostMember(SharedStoreConformanceFixture fixture)
    {
        private readonly object _gate = new();
        private SharedStoreClusterMembership? _member;

        public IClusterMembership Resolve(IServiceProvider services) => Get(services);

        public Joiner JoinOnStart(IServiceProvider services) => new(() => Get(services));

        private SharedStoreClusterMembership Get(IServiceProvider services)
        {
            lock (_gate)
                return _member ??= fixture.Create(
                    services.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value.HostId!,
                    fixture._clock,
                    MemberReportComposition.SingleSource(services.GetServices<IMemberReportSource<ReadabilitySection>>()),
                    MemberReportComposition.SingleSource(services.GetServices<IMemberReportSource<RunnabilitySection>>()));
        }
    }

    private sealed class Joiner(Func<SharedStoreClusterMembership> member) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var joining = member();
            await joining.JoinAsync(cancellationToken);
            joining.SetStatus(MemberStatus.Active);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
