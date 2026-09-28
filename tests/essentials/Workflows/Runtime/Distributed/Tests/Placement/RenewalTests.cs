using Elsa.Cluster.Testing.Runtime;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.Distributed.Tests.Placement.PlacementCluster;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// Spec 184, FR-014 and mechanism 3: renewal extends only the lease the member still holds, under its placement token, as
/// a compare-and-set, and never grants. A reclaim that lands between the pump listing its leases and renewing them wins,
/// and the lease it released is not taken back.
/// </summary>
public sealed class RenewalTests : IAsyncDisposable
{
    private readonly PlacementCluster _cluster = new();

    [Fact]
    public async Task A_lease_released_between_listing_and_renewal_is_not_granted_back_by_the_renewal()
    {
        ReleaseBeforeRenewalStore? racing = null;
        var owner = await _cluster.StartAsync("host-a", setup => setup.Configure = services =>
            services.Replace(ServiceDescriptor.Scoped<IExecutionPlacementStore>(_ => racing ??= new ReleaseBeforeRenewalStore(_cluster.State.Placement, _cluster.Clock))));
        await owner.Runtime.DispatchAsync(RuntimeWork.Work(ExecutionId, _cluster.Now, "owned"));
        Assert.Equal(owner.HostId, (await _cluster.State.Placement.FindAsync(ExecutionId))?.OwnerId);

        racing!.Armed = true;
        var sweep = await owner.Runtime.Pump.SweepOnceAsync();

        Assert.Equal(0, sweep.RenewedCount);
        Assert.Null(await _cluster.State.Placement.FindAsync(ExecutionId));
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

    /// <summary>A placement store where, once armed, a reclaim releases the lease just before the pump renews it.</summary>
    private sealed class ReleaseBeforeRenewalStore(InMemoryExecutionPlacementStore inner, FakeTimeProvider clock) : IExecutionPlacementStore
    {
        public bool Armed { get; set; }

        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, cancellationToken);

        public async ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await ReclaimIfArmedAsync(claim.WorkflowExecutionId, cancellationToken);
            return await inner.TryClaimAsync(claim, now, cancellationToken);
        }

        public async ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease held, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            await ReclaimIfArmedAsync(held.WorkflowExecutionId, cancellationToken);
            return await inner.TryRenewAsync(held, now, expiresAt, cancellationToken);
        }

        public ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListOwnedAsync(request, cancellationToken);

        private async ValueTask ReclaimIfArmedAsync(string workflowExecutionId, CancellationToken cancellationToken)
        {
            if (!Armed || await inner.FindAsync(workflowExecutionId, cancellationToken) is not { } current)
                return;

            Armed = false;
            Assert.True(await inner.ReleaseAsync(current, cancellationToken), $"The simulated reclaim at {clock.GetUtcNow():O} released nothing.");
        }
    }
}
