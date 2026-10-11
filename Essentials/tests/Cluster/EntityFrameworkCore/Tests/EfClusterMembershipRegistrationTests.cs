using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>How the EF provider is composed on a host, and the lifecycle it runs there.</summary>
public sealed class EfClusterMembershipRegistrationTests : IAsyncDisposable
{
    private readonly EfClusterMembershipConformanceFixture _fixture = new(EfClusterMembershipTestStore.CreateSqlite());

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>
    /// FR-024: the module migrates through the plain-host migrator. CShells copies every host registration into each
    /// shell, so a shell hook would migrate the membership store again on every shell activation, from the shell's
    /// configuration. The control proves the check would see one.
    /// </summary>
    [Fact]
    public void The_module_migrates_on_the_host_only_never_on_a_shell_activation()
    {
        var composed = new ServiceCollection().AddEfClusterMembership(_fixture.Settings);
        var control = new ServiceCollection().AddEfModuleMigrations<ClusterMembershipDbContext>("Sqlite");

        Assert.DoesNotContain(composed, MigratesOnShellActivation);
        Assert.Contains(control, MigratesOnShellActivation);
        Assert.Contains(composed, descriptor => descriptor.ServiceType == typeof(EfModuleMigrator<ClusterMembershipDbContext>));
    }

    [Fact]
    public void An_engine_no_module_supports_is_refused_when_composed()
    {
        var failure = Assert.ThrowsAny<Exception>(() =>
            new ServiceCollection().AddEfClusterMembership(new EfClusterMembershipOptions { Provider = "Oracle" }));

        Assert.Contains("Oracle", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>FR-031: cleanup could otherwise delete an entry a reader still counts.</summary>
    [Fact]
    public async Task A_cleanup_period_within_the_liveness_window_refuses_to_start_before_joining()
    {
        _fixture.Settings.CleanupPeriod = _fixture.Timings.ExpiryPeriod + _fixture.Timings.SkewAllowance;
        var stored = await _fixture.CountStoredEntriesAsync();

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => StartHostAsync(NewHostId("hasty")));

        Assert.Contains(nameof(EfClusterMembershipOptions.CleanupPeriod), failure.Message, StringComparison.Ordinal);
        Assert.Equal(stored, await _fixture.CountStoredEntriesAsync());
    }

    /// <summary>
    /// FR-027 through the host's own lifecycle: the member heartbeats on its interval as the host's clock advances, so it
    /// stays live long past its expiry period, and it is active once the host has started.
    /// </summary>
    [Fact]
    public async Task A_started_host_heartbeats_on_its_interval_and_stays_live_past_its_expiry_period()
    {
        var observer = await _fixture.StartMemberAsync(new ConformanceMemberSetup(NewHostId("observer"), []));
        using var host = await StartHostAsync(NewHostId("hosted"));
        var identity = host.Services.GetRequiredService<IClusterMembership>().GetLocalStanding().Identity;
        var joinedAt = (await SeenAsync(observer, identity))!.LastHeartbeatAt;

        var intervals = (int)Math.Ceiling((_fixture.Timings.ExpiryPeriod + _fixture.Timings.SkewAllowance) / _fixture.Timings.HeartbeatInterval) + 2;
        for (var interval = 0; interval < intervals; interval++)
        {
            await _fixture.AdvanceAsync(_fixture.Timings.HeartbeatInterval);
            await HeartbeatLandedAsync(observer, identity);
        }

        var seen = (await SeenAsync(observer, identity))!;
        Assert.True(seen is { IsLive: true, Status: MemberStatus.Active }, $"The hosted member is {seen.Status}, live: {seen.IsLive}.");
        Assert.Equal(_fixture.Clock.GetUtcNow(), seen.LastHeartbeatAt);
        Assert.True(seen.LastHeartbeatAt > joinedAt + _fixture.Timings.ExpiryPeriod);
        Assert.False(host.Services.GetRequiredService<IClusterMembership>().GetLocalStanding().HasLapsed);
    }

    /// <summary>
    /// The provider publishes every section whose source the host composed beside it (spec 183, FR-014; spec 184,
    /// FR-008): a host whose runtime reports what it can run is seen with that section, never without it.
    /// </summary>
    [Fact]
    public async Task A_started_host_publishes_every_section_whose_source_it_composed_beside_the_provider()
    {
        var observer = await _fixture.StartMemberAsync(new ConformanceMemberSetup(NewHostId("observer"), []));
        var reads = new ReadabilityEntry("registration-family", "RegistrationModule", ["1"]);
        var runs = new RunnabilityEntry([new RunnableConsumer("clr", ["1"])], ["json"], ["Elsa.WriteLine"]);
        using var host = await StartHostAsync(NewHostId("hosted"), services => services
            .AddSingleton<IMemberReportSource<ReadabilitySection>>(new ConformanceReadabilitySource([reads]))
            .AddSingleton<IMemberReportSource<RunnabilitySection>>(new ConformanceRunnabilitySource([runs])));
        var identity = host.Services.GetRequiredService<IClusterMembership>().GetLocalStanding().Identity;

        var seen = (await SeenAsync(observer, identity))!;

        Assert.Equal(new MemberReport(new ReadabilitySection([reads]), new RunnabilitySection([runs])), seen.Report);
    }

    private static bool MigratesOnShellActivation(ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance is ShellInitializerRegistration { InitializerType: var type } &&
        type == typeof(EfModuleMigrator<ClusterMembershipDbContext>);

    private static string NewHostId(string name) => $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)];

    private static async Task<FleetMember?> SeenAsync(IConformanceMember reader, ClusterMemberIdentity identity) =>
        (await reader.Membership.ReadFleetAsync(FleetReadMode.Fresh)).Find(identity);

    private async Task<IHost> StartHostAsync(string hostId, Action<IServiceCollection>? compose = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Configure<ClusterMembershipOptions>(options => options.HostId = hostId);
        compose?.Invoke(builder.Services);
        _fixture.ComposeProvider(builder.Services);
        var host = builder.Build();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>The host's timer fires on the fake clock, but its heartbeat runs on the thread pool: wait for it to land.</summary>
    private async Task HeartbeatLandedAsync(IConformanceMember reader, ClusterMemberIdentity identity)
    {
        var due = _fixture.Clock.GetUtcNow();
        for (var poll = 0; poll < 200 && (await SeenAsync(reader, identity))?.LastHeartbeatAt < due; poll++)
            await Task.Delay(TimeSpan.FromMilliseconds(50));
    }
}
