using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, User Story 6, FR-019 to FR-021 and SC-007: a draining member claims nothing new and hands off every
/// execution it holds, at once when idle and at the drain's next boundary when mid-drain; when it stops, it releases
/// whatever it still holds; an active member claims each execution at its next sweep, and nothing is lost.
/// </summary>
public sealed class DrainingTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();

    [Fact]
    public async Task A_draining_member_claims_nothing_new_and_forwards_the_command()
    {
        var draining = await _cluster.StartAsync("host-a");
        draining.Member.Become(MemberStatus.Draining);

        var result = await draining.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "new"));
        var sweep = await draining.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.Equal(nameof(PlacementRefusalKind.MemberNotActive), result.Metadata[ForwardingWorkflowExecutionActor.PlacementRefusedMetadataKey]);
        Assert.Equal(0, sweep.ClaimedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(draining.Runtime.Commands.Committed);
    }

    [Fact]
    public async Task An_idle_execution_is_handed_off_at_once_and_an_active_member_claims_it_with_its_unacknowledged_work()
    {
        var draining = await _cluster.StartAsync("host-a");
        var active = await _cluster.StartAsync("host-b");
        await draining.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "drained"));
        await active.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "unacknowledged"));
        Assert.Single(await _cluster.State.Transport.LeaseAsync(ExecutionId, draining.HostId, _cluster.Now, TimeSpan.FromMinutes(10), 10));

        draining.Member.Become(MemberStatus.Draining);
        var handOff = await draining.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(1, handOff.HandedOffCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(await _cluster.State.Transport.ListLeasedAsync(draining.HostId, _cluster.Now, 10));

        await active.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(active.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        Assert.Equal("unacknowledged", Assert.Single(active.Runtime.Commands.Committed).EnvelopeId);
        Assert.Equal(0, await _cluster.State.Transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task An_execution_mid_drain_is_handed_off_only_after_the_drain_reaches_its_boundary()
    {
        var draining = await _cluster.StartAsync("host-a");
        var hold = draining.Runtime.Commands.HoldNextDrain();
        var drain = draining.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "mid-drain")).AsTask();
        await hold.Started.Task;

        draining.Member.Become(MemberStatus.Draining);
        var sweep = draining.Runtime.Pump.SweepOnceAsync().AsTask();

        Assert.False(sweep.IsCompleted);
        Assert.Equal(draining.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);

        hold.Release();
        await drain;
        Assert.Equal(1, (await sweep).HandedOffCount);
        Assert.Equal("mid-drain", Assert.Single(draining.Runtime.Commands.Committed).EnvelopeId);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
    }

    [Fact]
    public async Task A_member_that_stops_releases_every_lease_it_still_holds()
    {
        var stopping = await _cluster.StartAsync("host-a");
        var active = await _cluster.StartAsync("host-b");
        await stopping.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "drained"));
        await active.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "unacknowledged"));
        Assert.Single(await _cluster.State.Transport.LeaseAsync(ExecutionId, stopping.HostId, _cluster.Now, TimeSpan.FromMinutes(10), 10));

        await stopping.Runtime.StopAsync();

        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
        Assert.Empty(await _cluster.State.Transport.ListLeasedAsync(stopping.HostId, _cluster.Now, 10));
        await active.Runtime.Pump.SweepOnceAsync();
        Assert.Equal("unacknowledged", Assert.Single(active.Runtime.Commands.Committed).EnvelopeId);
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();
}
