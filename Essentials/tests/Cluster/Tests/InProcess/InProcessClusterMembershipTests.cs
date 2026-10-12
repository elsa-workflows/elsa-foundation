using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Cluster.Tests.InProcess;

/// <summary>What the in-process provider promises beyond the conformance suite: it costs nothing, needs no
/// configuration, and is one member per process however many containers ask for it (FR-017, FR-018).</summary>
public sealed class InProcessClusterMembershipTests : IAsyncDisposable
{
    private readonly CountingClock _clock = new();
    private readonly List<ServiceProvider> _containers = [];

    [Fact]
    public void The_host_id_defaults_to_the_machine_name() =>
        Assert.Equal(Environment.MachineName, Membership().GetLocalStanding().Identity.HostId);

    [Fact]
    public void A_configured_host_id_is_used() =>
        Assert.Equal("configured-host", Membership("configured-host").GetLocalStanding().Identity.HostId);

    [Fact]
    public void An_invalid_host_id_is_refused() =>
        Assert.All(new[] { " ", "host-\uD800" }, hostId => Assert.Throws<OptionsValidationException>(() => Membership(hostId)));

    [Fact]
    public async Task The_member_never_lapses_and_stays_live_however_long_the_host_runs()
    {
        var membership = Membership(NewHostId());
        _clock.Advance(TimeSpan.FromDays(400));

        var self = Assert.Single((await membership.ReadFleetAsync(FleetReadMode.Fresh)).Members);

        Assert.False(membership.GetLocalStanding().HasLapsed);
        Assert.True(self.IsLive);
        Assert.Equal(ClusterProviderKind.InProcess, membership.ProviderKind);
    }

    [Fact]
    public async Task Fresh_and_cached_reads_are_the_same()
    {
        var membership = Membership(NewHostId());

        var fresh = Assert.Single((await membership.ReadFleetAsync(FleetReadMode.Fresh)).Members);
        var cached = Assert.Single((await membership.ReadFleetAsync(FleetReadMode.Cached)).Members);

        Assert.Equal(fresh.Identity, cached.Identity);
        Assert.Equal(fresh.Report, cached.Report);
        Assert.Equal(fresh.ReportRevision, cached.ReportRevision);
    }

    [Fact]
    public async Task It_starts_no_timer_and_registers_no_hosted_service()
    {
        var services = new ServiceCollection().TryAddInProcessClusterMembership();
        var membership = Membership(NewHostId());

        await membership.ReadFleetAsync(FleetReadMode.Fresh);
        await membership.PublishReportAsync();
        await membership.QueryAsync(MemberQuery.Counting(), FleetReadMode.Cached);

        Assert.Equal(0, _clock.TimersCreated);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task Every_container_of_the_process_is_the_same_member_even_when_each_registers_the_default_itself()
    {
        var hostId = NewHostId();
        var readability = new ConformanceReadabilitySource([new ReadabilityEntry("orders", "OrdersModule", ["1"])]);
        var first = Membership(hostId, readability);
        var second = Membership(hostId, readability);

        Assert.Equal(first.GetLocalStanding().Identity, second.GetLocalStanding().Identity);

        readability.Set([new ReadabilityEntry("orders", "OrdersModule", ["1", "2"])]);
        var published = await first.PublishReportAsync();
        var seen = Assert.Single((await second.ReadFleetAsync(FleetReadMode.Fresh)).Members);

        Assert.Equal(published.Report, seen.Report);
        Assert.Equal(published.Revision, seen.ReportRevision);
    }

    [Fact]
    public void Two_readability_sources_refuse_the_provider()
    {
        var services = Services(NewHostId())
            .AddSingleton<IMemberReportSource<ReadabilitySection>>(new ConformanceReadabilitySource([]))
            .AddSingleton<IMemberReportSource<ReadabilitySection>>(new ConformanceReadabilitySource([]));

        Assert.Throws<ClusterMembershipConfigurationException>(() => Resolve(services));
    }

    [Fact]
    public async Task Without_a_readability_source_the_report_has_no_readability_section()
    {
        var published = await Membership(NewHostId()).PublishReportAsync();

        Assert.Equal(MemberReport.Empty, published.Report);
        Assert.Equal(1, published.Revision);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var container in _containers)
            await container.DisposeAsync();
    }

    private static string NewHostId() => $"in-process-{Guid.NewGuid():N}";

    private IServiceCollection Services(string? hostId)
    {
        var services = new ServiceCollection().AddSingleton<TimeProvider>(_clock);
        if (hostId is not null)
            services.Configure<ClusterMembershipOptions>(options => options.HostId = hostId);
        return services;
    }

    private IClusterMembership Membership(string? hostId = null, IMemberReportSource<ReadabilitySection>? readability = null)
    {
        var services = Services(hostId);
        if (readability is not null)
            services.AddSingleton(readability);
        return Resolve(services);
    }

    private IClusterMembership Resolve(IServiceCollection services)
    {
        var container = services.TryAddInProcessClusterMembership().BuildServiceProvider();
        _containers.Add(container);
        return container.GetRequiredService<IClusterMembership>();
    }

    /// <summary>A test clock that also counts the timers anything asks it for.</summary>
    private sealed class CountingClock : FakeTimeProvider
    {
        public int TimersCreated { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimersCreated++;
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
