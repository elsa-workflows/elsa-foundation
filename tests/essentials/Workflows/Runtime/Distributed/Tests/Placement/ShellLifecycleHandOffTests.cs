using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, FR-021 and FR-022, on a real CShells host whose shell runs the distributed runtime over a store its
/// generations share. The leases a shell's runtime holds are its host id's: a reload, which activates the new generation
/// before it drains the old one, leaves every one of them held and the new generation renews them, while a host stop
/// hands every one of them off.
/// </summary>
public sealed class ShellLifecycleHandOffTests : IAsyncDisposable
{
    private const string ShellName = "placement";

    private readonly InMemoryExecutionPlacementStore _placement = new();
    private readonly InMemoryExecutionCommandTransport _transport = new();
    private readonly string _hostId = $"shell-lifecycle-{Guid.NewGuid():N}";
    private readonly IHost _host;
    private bool _stopped;

    public ShellLifecycleHandOffTests()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        services.Configure<ClusterMembershipOptions>(options => options.HostId = _hostId);
        // What the process and its database keep across a reload: the join sweep ledger and the placement and transport
        // stores. Every shell generation is built from copies of these registrations, so each one resolves the same.
        services.AddSingleton(new JoinSweepLedger());
        services.AddScoped<IExecutionPlacementStore>(_ => _placement);
        services.AddScoped<IExecutionCommandTransport>(_ => _transport);
        services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(NoopWorkflowExecutionCommandExecutor.Instance);
        services.AddSingleton<IDistributedLockProvider>(new NoLocks());
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(TasksFeature).Assembly, typeof(WorkflowsRuntimeDistributedFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<TasksFeature>()
                // The test drives every sweep; the timer never fires while it runs.
                .WithFeature<WorkflowsRuntimeDistributedFeature>(feature => feature.SweepIntervalSeconds = TimeSpan.FromHours(1).TotalSeconds)));
        _host = builder.Build();
    }

    private IShellRegistry Shells => _host.Services.GetRequiredService<IShellRegistry>();

    private static DateTimeOffset Now => TimeProvider.System.GetUtcNow();

    [Fact]
    public async Task A_shell_reload_keeps_every_lease_and_the_new_generation_renews_them()
    {
        var first = await ActivateAsync();
        var held = await HoldAsync();

        var reload = await Shells.ReloadAsync(ShellName);
        Assert.Null(reload.Error);
        AssertTerminated(await reload.Drain!.WaitAsync());

        Assert.Equal(held.PlacementToken, (await _placement.FindAsync(ExecutionId))?.PlacementToken);
        Assert.Single(await _transport.ListLeasedAsync(_hostId, Now, 10));

        var second = Shells.GetActive(ShellName)!;
        Assert.NotSame(first, second);
        var sweep = await Pump(second).SweepOnceAsync();

        Assert.Equal(1, sweep.RenewedCount);
        var renewed = await _placement.FindAsync(ExecutionId);
        Assert.Equal(_hostId, renewed?.OwnerId);
        Assert.True(renewed!.PlacementToken > held.PlacementToken);
    }

    [Fact]
    public async Task A_host_stop_hands_off_every_lease()
    {
        await ActivateAsync();
        await HoldAsync();

        await StopAsync();

        Assert.Null(await _placement.FindAsync(ExecutionId));
        Assert.Empty(await _transport.ListLeasedAsync(_hostId, Now, 10));
        Assert.Equal(1, await _transport.CountPendingAsync(ExecutionId));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _host.Dispose();
    }

    private async Task<IShell> ActivateAsync()
    {
        await _host.StartAsync();
        return await Shells.GetOrActivateAsync(ShellName);
    }

    /// <summary>Holds a placement lease and an unacknowledged transport item lease under the host id, as a runtime that
    /// took the execution and is draining its command does.</summary>
    private async Task<ExecutionPlacementLease> HoldAsync()
    {
        var now = Now;
        var claim = await _placement.TryClaimAsync(new ExecutionPlacementClaim(ExecutionId, _hostId, now, now.AddMinutes(10)), now);
        Assert.True(claim.IsOwnedByClaimant);
        await _transport.SendAsync(ExecutionId, RuntimeWork.Work(ExecutionId, now, "unacknowledged"), now);
        Assert.Single(await _transport.LeaseAsync(ExecutionId, _hostId, now, TimeSpan.FromMinutes(10), 10));
        return claim.Lease;
    }

    private async Task StopAsync()
    {
        if (_stopped)
            return;
        _stopped = true;
        await _host.StopAsync();
    }

    private static ExecutionPlacementPumpTask Pump(IShell shell) =>
        shell.ServiceProvider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>().Single();

    /// <summary>The old generation's terminators ran, so its placement pump was stopped before these assertions.</summary>
    private static void AssertTerminated(DrainResult drained) =>
        Assert.Contains(drained.TerminatorResults, result => result.TerminatorTypeName == nameof(Elsa.Tasks.Services.StopShellTasksTerminator) && result.Completed);
}
