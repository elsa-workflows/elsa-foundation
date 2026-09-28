using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>Why a host id's leases were reclaimed (spec 184, FR-028).</summary>
public enum HostIdReclaimKind
{
    /// <summary>A shell's distributed runtime first activated in a new process under the host id (FR-022).</summary>
    JoinSweep,

    /// <summary>A fresh read showed the host id departed: no live incarnation, its latest one left or expired (FR-023).</summary>
    Departure
}

/// <summary>What one reclaim released, per kind of per-execution lease (FR-028).</summary>
public sealed record HostIdReclaimResult(int PlacementLeases, int TransportItemLeases, int RecoveryCandidates)
{
    public static HostIdReclaimResult None { get; } = new(0, 0, 0);

    public HostIdReclaimResult Add(HostIdReclaimResult other) =>
        new(PlacementLeases + other.PlacementLeases, TransportItemLeases + other.TransportItemLeases, RecoveryCandidates + other.RecoveryCandidates);
}

/// <summary>
/// Reclaims the per-execution leases held under one host id in one persistence scope of this shell's stores (spec 184,
/// FR-024): it releases each placement lease and makes each transport item lease visible, both as a compare-and-set on
/// the lease as observed, and it makes each execution whose execution lease or heartbeat is held under the host id a
/// recovery candidate at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reclaim never decides a commit</b> (FR-025, invariant 2). It writes no execution lease and no fencing token: a
/// re-drive acquires its own lease, with a strictly greater token, so a member reclaimed from while it is still alive
/// reaches its commit with a stale token and is refused by the fence.
/// </para>
/// <para>
/// Only leases acquired at or before <c>acquiredAtOrBefore</c> are reclaimed: for a join sweep, the instant this process
/// first began one, for a departure, the instant of the fresh read that confirmed it. A lease a later incarnation of the
/// host id, or this process, acquired since is left alone. A transport item records when its lease expires, not when it
/// began, so its start is taken as its expiry less this member's lease duration. Reclaim runs in bounded batches, is
/// idempotent, and is safe when several members run it concurrently, because every release is a compare-and-set.
/// </para>
/// </remarks>
public sealed class HostIdReclaimer(
    ReclaimedExecutionRegistry candidates,
    TimeProvider timeProvider,
    ILogger<HostIdReclaimer>? logger = null)
{
    private const int BatchSize = 100;
    private const int MaximumBatches = 100;
    private readonly ILogger _logger = logger ?? NullLogger<HostIdReclaimer>.Instance;

    /// <summary>Reclaims <paramref name="hostId"/>'s leases in the persistence scope <paramref name="scope"/>, whose
    /// operation-scope services are <paramref name="services"/>.</summary>
    public async ValueTask<HostIdReclaimResult> ReclaimAsync(
        IServiceProvider services,
        string scope,
        string hostId,
        DateTimeOffset acquiredAtOrBefore,
        TimeSpan leaseDuration,
        HostIdReclaimKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        DistributedRuntimeIdentityConstraints.Validate(hostId, nameof(hostId));

        var placementLeases = await ReleasePlacementLeasesAsync(services.GetRequiredService<IExecutionPlacementStore>(), hostId, acquiredAtOrBefore, cancellationToken);
        var itemLeases = await ReleaseTransportItemLeasesAsync(services.GetRequiredService<IExecutionCommandTransport>(), hostId, acquiredAtOrBefore, leaseDuration, cancellationToken);
        var recoveryCandidates = await MarkRecoveryCandidatesAsync(services.GetService<IExecutionLivenessStateStore>(), scope, hostId, acquiredAtOrBefore, kind, cancellationToken);
        return new HostIdReclaimResult(placementLeases, itemLeases, recoveryCandidates);
    }

    private async ValueTask<int> ReleasePlacementLeasesAsync(
        IExecutionPlacementStore store,
        string hostId,
        DateTimeOffset acquiredAtOrBefore,
        CancellationToken cancellationToken)
    {
        var released = 0;
        for (var batch = 0; batch < MaximumBatches; batch++)
        {
            var leases = await store.ListOwnedAsync(new ExecutionPlacementLeaseListRequest(hostId, timeProvider.GetUtcNow(), BatchSize), cancellationToken);
            var releasedInBatch = 0;
            foreach (var lease in leases.Where(lease => lease.AcquiredAt <= acquiredAtOrBefore))
            {
                if (await store.ReleaseAsync(lease, cancellationToken))
                    releasedInBatch++;
            }

            released += releasedInBatch;
            if (leases.Count < BatchSize || releasedInBatch == 0)
                break;
        }

        return released;
    }

    private async ValueTask<int> ReleaseTransportItemLeasesAsync(
        IExecutionCommandTransport transport,
        string hostId,
        DateTimeOffset acquiredAtOrBefore,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var released = 0;
        for (var batch = 0; batch < MaximumBatches; batch++)
        {
            var now = timeProvider.GetUtcNow();
            var items = await transport.ListLeasedAsync(hostId, now, BatchSize, cancellationToken: cancellationToken);
            var releasedInBatch = 0;
            foreach (var item in items.Where(item => item.LeaseExpiresAt - leaseDuration <= acquiredAtOrBefore))
            {
                if (await transport.ReleaseLeaseAsync(item.WorkflowExecutionId, item.TransportItemId, hostId, item.LeaseToken!.Value, now, cancellationToken))
                    releasedInBatch++;
            }

            released += releasedInBatch;
            if (items.Count < BatchSize || releasedInBatch == 0)
                break;
        }

        return released;
    }

    private async ValueTask<int> MarkRecoveryCandidatesAsync(
        IExecutionLivenessStateStore? store,
        string scope,
        string hostId,
        DateTimeOffset acquiredAtOrBefore,
        HostIdReclaimKind kind,
        CancellationToken cancellationToken)
    {
        if (store is null)
            return 0;

        candidates.Prune(timeProvider.GetUtcNow());
        var marked = 0;
        string? continuation = null;
        try
        {
            for (var batch = 0; batch < MaximumBatches; batch++)
            {
                var page = await store.ListOwnedPageAsync(hostId, new RuntimeStorePageRequest(BatchSize, continuation), cancellationToken);
                foreach (var state in page.Items.Where(state => HeldAtOrBefore(state, hostId, acquiredAtOrBefore)))
                {
                    candidates.Add(scope, state, hostId, kind, timeProvider.GetUtcNow());
                    marked++;
                }

                continuation = page.NextContinuationToken;
                if (continuation is null)
                    break;
            }
        }
        catch (NotSupportedException exception)
        {
            // A liveness store that cannot list by owner leaves its execution leases to time out, as before reclaim.
            _logger.LogWarning(exception, "The execution-liveness store cannot list executions by owner; executions held under host id {HostId} are recovered when their execution leases time out.", hostId);
        }

        return marked;
    }

    private static bool HeldAtOrBefore(ExecutionLivenessState state, string hostId, DateTimeOffset acquiredAtOrBefore) =>
        state.ExecutionLease is { } lease && StringComparer.Ordinal.Equals(lease.OwnerId, hostId) && lease.AcquiredAt <= acquiredAtOrBefore ||
        state.Heartbeat is { } heartbeat && StringComparer.Ordinal.Equals(heartbeat.OwnerId, hostId) && heartbeat.RecordedAt <= acquiredAtOrBefore;
}
