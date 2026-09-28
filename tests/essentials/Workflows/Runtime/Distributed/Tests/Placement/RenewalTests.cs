using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, FR-014 and mechanism 3: renewal extends only the lease the member still holds, under its placement token, as
/// a compare-and-set, and never grants. A reclaim that lands between the pump listing its leases and renewing them wins,
/// and the lease it released is not taken back. A renewal that loses to a newer generation of the same shell, during a
/// reload, releases nothing either: the lease is still the host id's, and that generation keeps renewing it.
/// </summary>
public sealed class RenewalTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();

    [Fact]
    public async Task A_lease_released_between_listing_and_renewal_is_not_granted_back_by_the_renewal()
    {
        var (owner, racing) = await StartRacingAsync(async current =>
            Assert.True(await _cluster.State.Placement.ReleaseAsync(current), $"The simulated reclaim at {_cluster.Now:O} released nothing."));

        racing.Armed = true;
        var sweep = await owner.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(0, sweep.RenewedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
    }

    [Fact]
    public async Task A_lease_a_newer_generation_renewed_between_listing_and_renewal_stays_held()
    {
        ExecutionPlacementLease? renewedByNewer = null;
        var (owner, racing) = await StartRacingAsync(async current =>
            renewedByNewer = await _cluster.State.Placement.TryRenewAsync(current, _cluster.Now, _cluster.Now.AddMinutes(10)));

        racing.Armed = true;
        var sweep = await owner.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(0, sweep.RenewedCount);
        var lease = await _cluster.State.Placement.FindAsync(ExecutionId);
        Assert.Equal(owner.HostId, lease?.OwnerId);
        Assert.Equal(renewedByNewer!.PlacementToken, lease!.PlacementToken);
    }

    [Fact]
    public async Task The_in_memory_store_renews_only_the_live_lease_held_under_the_same_token()
    {
        var store = new InMemoryExecutionPlacementStore();
        var now = _cluster.Now;
        var held = (await store.TryClaimAsync(new(ExecutionId, "host-a", now, now.AddSeconds(30)), now)).Lease;

        var renewed = await store.TryRenewAsync(held, now, now.AddSeconds(60));
        Assert.NotNull(renewed);
        Assert.True(renewed.PlacementToken > held.PlacementToken);
        Assert.Null(await store.TryRenewAsync(held, now, now.AddSeconds(60)));

        Assert.True(await store.ReleaseAsync(renewed));
        Assert.False(await store.ReleaseAsync(renewed));
        Assert.Null(await store.TryRenewAsync(renewed, now, now.AddSeconds(60)));
        Assert.Null(await store.FindAsync(ExecutionId));

        var other = (await store.TryClaimAsync(new(ExecutionId, "host-b", now, now.AddSeconds(30)), now)).Lease;
        Assert.Null(await store.TryRenewAsync(renewed, now, now.AddSeconds(60)));
        Assert.Equal("host-b", (await store.FindAsync(ExecutionId))?.OwnerId);

        var expired = now.AddSeconds(31);
        Assert.Null(await store.TryRenewAsync(other, expired, expired.AddSeconds(30)));
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    /// <summary>A member that owns the execution through a placement store where, once armed, <paramref name="interfere"/>
    /// changes the lease just after the pump listed it and just before it renews it.</summary>
    private async Task<(PlacementMember Owner, InterferingStore Racing)> StartRacingAsync(Func<ExecutionPlacementLease, Task> interfere)
    {
        var racing = new InterferingStore(_cluster.State.Placement, interfere);
        var owner = await _cluster.StartAsync("host-a", setup => setup.Configure = services =>
            services.Replace(ServiceDescriptor.Scoped<IExecutionPlacementStore>(_ => racing)));
        await owner.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "owned"));
        Assert.Equal(owner.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);
        return (owner, racing);
    }

    /// <summary>A placement store where, once armed, another writer changes the lease just before the pump claims or
    /// renews it.</summary>
    private sealed class InterferingStore(InMemoryExecutionPlacementStore inner, Func<ExecutionPlacementLease, Task> interfere) : IExecutionPlacementStore
    {
        public bool Armed { get; set; }

        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, cancellationToken);

        public async ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await InterfereIfArmedAsync(claim.WorkflowExecutionId, cancellationToken);
            return await inner.TryClaimAsync(claim, now, cancellationToken);
        }

        public async ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease held, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            await InterfereIfArmedAsync(held.WorkflowExecutionId, cancellationToken);
            return await inner.TryRenewAsync(held, now, expiresAt, cancellationToken);
        }

        public ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListOwnedAsync(request, cancellationToken);

        private async ValueTask InterfereIfArmedAsync(string workflowExecutionId, CancellationToken cancellationToken)
        {
            if (!Armed || await inner.FindAsync(workflowExecutionId, cancellationToken) is not { } current)
                return;

            Armed = false;
            await interfere(current);
        }
    }
}
