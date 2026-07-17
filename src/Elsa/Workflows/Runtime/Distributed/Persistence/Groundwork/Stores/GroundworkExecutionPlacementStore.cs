using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IExecutionPlacementStore"/> backed by the portable <see cref="IDocumentStore"/> (W27). One
/// document per workflow execution (document id = workflow execution id) holds the current placement lease, so
/// placement routing survives process death and a survivor can take over when the lease expires.
/// </summary>
/// <remarks>
/// Every mutation is a storage-level compare-and-swap: claims over an existing document save with
/// <c>ExpectedVersion</c> = the loaded envelope version, first claims create with <c>ExpectedVersion</c> = 0
/// (create-only — the provider refuses the insert when a concurrent claimant wins first), and release CAS-deletes.
/// A lost race re-reads and re-evaluates, so two nodes can never both hold a live lease — the same exact guarantee
/// the in-memory store gets from its process-local lock, here enforced by the document store's own storage layer.
/// The persisted <see cref="ExecutionPlacementLease"/> shape is the frozen v1 <c>executionPlacement</c> golden
/// fixture; the wrapping document adds only the constant collection partition for the list sweep.
/// </remarks>
public sealed class GroundworkExecutionPlacementStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : IExecutionPlacementStore, IPagedExecutionPlacementStore
{
    private const string Kind = DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind;
    private const int MaxCasAttempts = 8;

    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        $"Placement queries for '{Kind}' require an admitted bounded document-store runtime.");

    public async ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = await store.LoadAsync(Kind, workflowExecutionId, cancellationToken);
        return envelope is null ? null : DistributedGroundworkDocuments.Deserialize<ExecutionPlacementDocument>(envelope).Lease;
    }

    public async ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            var envelope = await store.LoadAsync(Kind, claim.WorkflowExecutionId, cancellationToken);
            var current = envelope is null ? null : DistributedGroundworkDocuments.Deserialize<ExecutionPlacementDocument>(envelope).Lease;

            // A live lease held by another node blocks the claim; placement stays with the current owner.
            if (current is not null && !current.IsExpired(now) && !StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId))
                return new ExecutionPlacementClaimResult(ExecutionPlacementClaimOutcome.Denied, current);

            var outcome = current is not null && StringComparer.Ordinal.Equals(current.OwnerId, claim.OwnerId) && !current.IsExpired(now)
                ? ExecutionPlacementClaimOutcome.Renewed
                : ExecutionPlacementClaimOutcome.Granted;

            var lease = new ExecutionPlacementLease(
                workflowExecutionId: claim.WorkflowExecutionId,
                ownerId: claim.OwnerId,
                placementToken: (current?.PlacementToken ?? 0) + 1,
                acquiredAt: claim.RequestedAt,
                expiresAt: claim.ExpiresAt);

            // Storage-level compare-and-swap: ExpectedVersion 0 claims first creation (create-only — the provider
            // refuses the loser of a concurrent first claim), and the loaded envelope version guards every
            // subsequent transition so a concurrent claim/renewal/release forces a re-read instead of a clobber.
            var result = await store.SaveAsync(
                new SaveDocumentRequest(
                    Kind,
                    claim.WorkflowExecutionId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(new ExecutionPlacementDocument(Kind, lease)),
                    ExpectedVersion: envelope?.Version ?? 0),
                cancellationToken);

            if (result.Status == DocumentStoreWriteStatus.Saved)
                return new ExecutionPlacementClaimResult(outcome, lease);

            // Lost the compare-and-swap (a concurrent claim, renewal, or release moved the document); re-read and
            // re-evaluate against the winner's state.
        }

        throw new InvalidOperationException(
            $"Placement claim for workflow execution '{claim.WorkflowExecutionId}' did not settle after {MaxCasAttempts} compare-and-swap attempts.");
    }

    public async ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxCasAttempts; attempt++)
        {
            var envelope = await store.LoadAsync(Kind, lease.WorkflowExecutionId, cancellationToken);
            if (envelope is null)
                return;

            var current = DistributedGroundworkDocuments.Deserialize<ExecutionPlacementDocument>(envelope).Lease;

            // Release only when the stored lease still matches the caller's owner id and placement token, so a
            // superseded holder cannot clear a newer owner's placement. A no-op otherwise.
            if (!StringComparer.Ordinal.Equals(current.OwnerId, lease.OwnerId) || current.PlacementToken != lease.PlacementToken)
                return;

            var result = await store.DeleteAsync(
                new DeleteDocumentRequest(Kind, lease.WorkflowExecutionId, ExpectedVersion: envelope.Version),
                cancellationToken);
            if (result.Status is DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound)
                return;

            // Lost the compare-and-swap (a concurrent claim renewed the lease); re-read — the next iteration will
            // observe the newer owner/token and no-op.
        }
    }

    public async ValueTask<IReadOnlyCollection<ExecutionPlacementLease>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            ListQuery(skip: null, take: null),
            cancellationToken);

        return result.Documents
            .Select(envelope => DistributedGroundworkDocuments.Deserialize<ExecutionPlacementDocument>(envelope).Lease)
            .OrderBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<ExecutionPlacementLeasePage> ListPageAsync(ExecutionPlacementLeasePageRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = await BoundedStore.CountAsync(ListQuery(skip: null, take: null), cancellationToken);
        var result = await BoundedStore.QueryAsync(
            ListQuery(request.NormalizedSkip, request.NormalizedTake),
            cancellationToken);

        var items = result.Documents
            .Select(envelope => DistributedGroundworkDocuments.Deserialize<ExecutionPlacementDocument>(envelope).Lease)
            .OrderBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal)
            .ToArray();

        return new ExecutionPlacementLeasePage(items, count);
    }

    private static DocumentQuery ListQuery(int? skip, int? take) => new(
        Kind,
        DistributedGroundworkStorageManifest.ListAllQuery,
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            DistributedGroundworkStorageManifest.CollectionField,
            Kind))],
        [],
        skip,
        take);

    // The constant collection partition lets the list sweep use a keyword equality index instead of a provider-wide
    // scan; the nested lease is the frozen v1 executionPlacement wire shape, unchanged.
    private sealed record ExecutionPlacementDocument(string Collection, ExecutionPlacementLease Lease);
}
