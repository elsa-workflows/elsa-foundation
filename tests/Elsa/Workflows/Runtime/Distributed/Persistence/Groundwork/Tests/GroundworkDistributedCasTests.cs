using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Deterministic cross-node race tests for the Groundwork bridges — the load-bearing W27 behaviors. Each test
/// interleaves a competing node's write between one node's read and write using an intercepting document store, so
/// the race window that a wall-clock test could only hit probabilistically is exercised exactly once, every run.
/// A store that does read-check-write (instead of storage-level compare-and-swap) fails every test in this class.
/// </summary>
public sealed class GroundworkDistributedCasTests
{
    private const string ExecutionId = "wf-race";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private readonly InterceptingDocumentStore _interceptedStore;
    private readonly GroundworkExecutionPlacementStore _placementA;
    private readonly GroundworkExecutionPlacementStore _placementB;
    private readonly GroundworkExecutionCommandTransport _transportA;
    private readonly GroundworkExecutionCommandTransport _transportB;

    public GroundworkDistributedCasTests()
    {
        // Node A goes through the interceptor; node B's competing writes go straight to the shared store.
        var sharedStore = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        _interceptedStore = new InterceptingDocumentStore(sharedStore);
        _placementA = new GroundworkExecutionPlacementStore(_interceptedStore, sharedStore);
        _placementB = new GroundworkExecutionPlacementStore(sharedStore);
        _transportA = new GroundworkExecutionCommandTransport(
            _interceptedStore,
            GroundworkDistributedTestAccess.Scoped(),
            sharedStore);
        _transportB = new GroundworkExecutionCommandTransport(
            sharedStore,
            GroundworkDistributedTestAccess.Scoped());
    }

    [Fact]
    public async Task FirstClaimRace_ExactlyOneNodeIsGranted()
    {
        // Node B claims placement in the window between node A's read (absent) and node A's create.
        ExecutionPlacementClaimResult? resultB = null;
        _interceptedStore.OnBeforeSave = async _ => resultB = await _placementB.TryClaimAsync(Claim(NodeB), Now);

        var resultA = await _placementA.TryClaimAsync(Claim(NodeA), Now);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, resultB!.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, resultA.Outcome);
        Assert.Equal(NodeB, resultA.Lease.OwnerId);

        var stored = await _placementB.FindAsync(ExecutionId);
        Assert.Equal(NodeB, stored!.OwnerId);
    }

    [Fact]
    public async Task ExpiredLeaseTakeoverRace_ExactlyOneNodeIsGranted()
    {
        // A third node held the lease and died; both survivors race the takeover after expiry.
        await _placementB.TryClaimAsync(new ExecutionPlacementClaim(ExecutionId, "node-c", Now, Now + LeaseDuration), Now);
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);

        ExecutionPlacementClaimResult? resultB = null;
        _interceptedStore.OnBeforeSave = async _ => resultB = await _placementB.TryClaimAsync(Claim(NodeB, afterExpiry), afterExpiry);

        var resultA = await _placementA.TryClaimAsync(Claim(NodeA, afterExpiry), afterExpiry);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, resultB!.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, resultA.Outcome);

        var stored = await _placementB.FindAsync(ExecutionId);
        Assert.Equal(NodeB, stored!.OwnerId);
        Assert.Equal(2, stored.PlacementToken);
    }

    [Fact]
    public async Task SendRace_OnTheSameSequence_LosesNoCommand()
    {
        // Node B sends a command in the window between node A's stream-head read and node A's stream-head CAS create.
        // The store must refuse node A's stale head create; node A retries with sequence 2 without scanning the inbox.
        _interceptedStore.OnAfterLoad = async (kind, _) =>
        {
            if (!string.Equals(kind, DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind, StringComparison.Ordinal))
                return;

            await _transportB.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-b", Now), Now);
        };

        var itemA = await _transportA.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-a", Now), Now);

        Assert.Equal(2, await _transportB.CountPendingAsync(ExecutionId));
        Assert.Equal(2, itemA.Sequence);

        var drained = await _transportB.LeaseAsync(ExecutionId, NodeB, Now, LeaseDuration, maxItems: 10);
        Assert.Equal(new[] { "env-b", "env-a" }, drained.Select(item => item.Envelope.EnvelopeId).ToArray());
        Assert.Equal(new long[] { 1, 2 }, drained.Select(item => item.Sequence).ToArray());
        Assert.Equal(0, _interceptedStore.RollbackAttempts);
    }

    [Fact]
    public async Task LeaseRace_OnTheSameItem_ExactlyOneNodeHoldsIt()
    {
        await _transportB.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);

        // Node B leases the item in the window between node A's read (visible) and node A's lease stamp.
        IReadOnlyList<ExecutionCommandTransportItem>? leasedByB = null;
        _interceptedStore.OnBeforeSave = async _ =>
            leasedByB = await _transportB.LeaseAsync(ExecutionId, NodeB, Now, LeaseDuration, maxItems: 10);

        var leasedByA = await _transportA.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);

        Assert.Single(leasedByB!);
        Assert.Empty(leasedByA);
        Assert.False(await _transportA.AckAsync(ExecutionId, leasedByB![0].TransportItemId, NodeA, leasedByB[0].LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.True(await _transportB.AckAsync(ExecutionId, leasedByB[0].TransportItemId, NodeB, leasedByB[0].LeaseToken!.Value, Now.AddSeconds(1)));
    }

    private static ExecutionPlacementClaim Claim(string ownerId, DateTimeOffset? requestedAt = null)
    {
        var at = requestedAt ?? Now;
        return new ExecutionPlacementClaim(ExecutionId, ownerId, at, at + LeaseDuration);
    }

    /// <summary>
    /// Delegates to the shared store, invoking a one-shot hook before the next save so a competing node's write
    /// lands deterministically inside another node's read→write window.
    /// </summary>
    private sealed class InterceptingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public Func<SaveDocumentRequest, Task>? OnBeforeSave { get; set; }
        public Func<string, string, Task>? OnAfterLoad { get; set; }
        public int RollbackAttempts { get; private set; }
        public DocumentStoreAccess Access => inner.Access;

        public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            if (OnBeforeSave is { } hook)
            {
                OnBeforeSave = null;
                await hook(request);
            }

            return await inner.SaveAsync(request, cancellationToken);
        }

        public async Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            var envelope = await inner.LoadAsync(documentKind, id, cancellationToken);
            if (OnAfterLoad is { } hook)
            {
                OnAfterLoad = null;
                await hook(documentKind, id);
            }

            return envelope;
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            new InterceptingDocumentUnitOfWork(await inner.BeginAsync(scope, cancellationToken), this);

        public void RecordRollbackAttempt() =>
            RollbackAttempts++;
    }

    private sealed class InterceptingDocumentUnitOfWork(
        IDocumentUnitOfWork inner,
        InterceptingDocumentStore interceptor) : IDocumentUnitOfWork
    {
        public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            if (interceptor.OnBeforeSave is { } hook)
            {
                interceptor.OnBeforeSave = null;
                await hook(request);
            }

            return await inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            interceptor.RecordRollbackAttempt();
            return inner.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() =>
            inner.DisposeAsync();
    }
}
