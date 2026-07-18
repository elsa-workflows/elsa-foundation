using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Core.Text;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IExecutionCommandTransport"/> backed by the portable <see cref="IDocumentStore"/> (W27). Each
/// queued command is one document (document id = transport item id, a composite of the escaped workflow execution id
/// and the per-execution sequence), so the cross-node inbox survives process death and a survivor re-leases and
/// re-drives stranded items on failover.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sequence allocation.</b> <see cref="SendAsync"/> advances one per-execution stream-head document under
/// provider-level <c>ExpectedVersion</c> compare-and-swap, then atomically commits the advanced head and create-only
/// command item in one document unit of work. The transport never scans the execution backlog to discover max
/// sequence; contention is decided by the stream-head CAS, and losers re-read the head and retry.
/// </para>
/// <para>
/// <b>Lease/ack.</b> Delivery is at-least-once with ack-based visibility leases, exactly like the in-memory
/// transport: leasing stamps the item behind <c>ExpectedVersion</c> CAS (a concurrent leaser wins at the storage
/// layer and the loser skips), and only the live lease holder may ack — an ack CAS-deletes and is refused when the
/// lease expired and another node re-leased. The persisted <see cref="ExecutionCommandTransportItem"/> is the frozen
/// v1 <c>executionCommandTransport</c> golden fixture shape; the wrapping document lifts the collection,
/// workflow-comparison key, visibility, and sequence fields required by the declared physical routes.
/// </para>
/// </remarks>
public sealed class GroundworkExecutionCommandTransport(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null) : IExecutionCommandTransport
{
    private const string Kind = DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind;
    private const string StreamHeadKind = DistributedRuntimeStorageManifest.ExecutionCommandStreamHeadDocumentKind;
    private const int MaxCreateAttempts = 16;

    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        $"Command-transport queries for '{Kind}' require an admitted bounded document-store runtime.");

    public async ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        accessContextAccessor.Current.EnsureScope(new PersistenceScope(envelope.Partition.Value));

        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var (streamHeadId, sequence, streamHeadExpectedVersion) = await LoadNextSequenceAsync(workflowExecutionId, cancellationToken);
            var transportItemId = ComposeTransportItemId(workflowExecutionId, sequence);
            var item = new ExecutionCommandTransportItem(
                transportItemId: transportItemId,
                workflowExecutionId: workflowExecutionId,
                envelope: envelope,
                sequence: sequence,
                enqueuedAt: now);

            await using var unitOfWork = await store.BeginAsync(DocumentCommitScope.Of(StreamHeadKind, Kind), cancellationToken);
            var headSave = await unitOfWork.SaveAsync(
                new SaveDocumentRequest(
                    StreamHeadKind,
                    streamHeadId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(new StreamHeadDocument(StreamHeadKind, workflowExecutionId, sequence)),
                    ExpectedVersion: streamHeadExpectedVersion),
                cancellationToken);
            if (headSave.Status != DocumentStoreWriteStatus.Saved)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                if (IsSequenceContention(headSave.Status))
                    continue;

                throw new InvalidOperationException(
                    $"Advancing command stream head '{streamHeadId}' failed with status '{headSave.Status}'.");
            }

            var itemSave = await unitOfWork.SaveAsync(
                new SaveDocumentRequest(
                    Kind,
                    transportItemId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(TransportItemDocument.From(Kind, item)),
                    ExpectedVersion: 0),
                cancellationToken);
            if (itemSave.Status != DocumentStoreWriteStatus.Saved)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Creating command transport item '{transportItemId}' after stream-head allocation failed with status '{itemSave.Status}'.");
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return item;
        }

        throw new InvalidOperationException(
            $"Sending a command to workflow execution '{workflowExecutionId}' did not settle after {MaxCreateAttempts} stream-head compare-and-swap attempts.");
    }

    private async Task<(string StreamHeadId, long Sequence, long ExpectedVersion)> LoadNextSequenceAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var streamHeadId = ComposeStreamHeadId(workflowExecutionId);
        var envelope = await store.LoadAsync(StreamHeadKind, streamHeadId, cancellationToken);
        if (envelope is null)
            return (streamHeadId, 1, 0);

        var head = DistributedGroundworkDocuments.Deserialize<StreamHeadDocument>(envelope);
        if (!string.Equals(head.WorkflowExecutionId, workflowExecutionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Command stream head '{streamHeadId}' belongs to workflow execution '{head.WorkflowExecutionId}', not '{workflowExecutionId}'.");

        return (streamHeadId, head.LastSequence + 1, envelope.Version);
    }

    private static bool IsSequenceContention(DocumentStoreWriteStatus status) =>
        status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound;

    public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        cancellationToken.ThrowIfCancellationRequested();

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));

        var leaseExpiresAt = now + leaseDuration;
        var leased = new List<ExecutionCommandTransportItem>();

        foreach (var (item, version) in await LoadVisibleItemsAsync(workflowExecutionId, now, maxItems, cancellationToken))
        {
            // The lease stamp is guarded by the loaded envelope version, so a concurrent leaser wins at the
            // storage layer and the loser skips the item instead of double-holding it.
            var leasedItem = item.Lease(ownerId, leaseExpiresAt);
            var result = await store.SaveAsync(
                new SaveDocumentRequest(
                    Kind,
                    item.TransportItemId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(TransportItemDocument.From(Kind, leasedItem)),
                    ExpectedVersion: version),
                cancellationToken);

            if (result.Status == DocumentStoreWriteStatus.Saved)
                leased.Add(leasedItem);

            // A lost compare-and-swap means another node leased or acked the item between our read and write; skip it.
        }

        return leased;
    }

    public async ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = await store.LoadAsync(Kind, transportItemId, cancellationToken);
        if (envelope is null)
            return false;

        var item = DistributedGroundworkDocuments.Deserialize<TransportItemDocument>(envelope).Item;
        if (!string.Equals(item.WorkflowExecutionId, workflowExecutionId, StringComparison.Ordinal))
            return false;

        // Only the node that currently holds a live lease may ack. If the lease expired and another node re-leased
        // it, the superseded node's ack is refused so the survivor stays responsible for the command.
        if (!string.Equals(item.LeasedByOwnerId, ownerId, StringComparison.Ordinal) || item.IsVisible(now))
            return false;

        // CAS-delete on the loaded envelope version: if the item moved (re-leased by a survivor) between our read
        // and this delete, the delete conflicts and the ack is refused.
        var result = await store.DeleteAsync(
            new DeleteDocumentRequest(Kind, transportItemId, ExpectedVersion: envelope.Version),
            cancellationToken);
        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                    Kind,
                    DistributedGroundworkStorageManifest.ListPendingExecutionIdsQuery,
                    [
                        DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                            DistributedGroundworkStorageManifest.CollectionField,
                            Kind)),
                        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                            DistributedGroundworkStorageManifest.VisibleAtField,
                            now.ToString("O")))
                    ],
                    [
                        new DocumentQueryOrder(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField),
                        new DocumentQueryOrder(
                            DistributedGroundworkStorageManifest.SequenceField,
                            global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Descending)
                    ],
                    take: maxItems)
                .LatestPerKey(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField),
            cancellationToken);

        return result.Documents
            .Select(envelope => DistributedGroundworkDocuments.Deserialize<TransportItemDocument>(envelope).WorkflowExecutionId)
            .ToArray();
    }

    public async ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();

        var count = await BoundedStore.CountAsync(
            CountQuery(workflowExecutionId)
                .Select(global::Groundwork.Core.PhysicalStorage.BoundedQueryResultOperation.Count),
            cancellationToken);
        return checked((int)count);
    }

    private async Task<IReadOnlyList<(ExecutionCommandTransportItem Item, long Version)>> LoadVisibleItemsAsync(
        string workflowExecutionId,
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                Kind,
                DistributedGroundworkStorageManifest.LeaseVisibleCommandsQuery,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                        OrdinalKey(workflowExecutionId))),
                    DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                        DistributedGroundworkStorageManifest.VisibleAtField,
                        now.ToString("O")))
                ],
                [new DocumentQueryOrder(DistributedGroundworkStorageManifest.SequenceField)],
                take: take),
            cancellationToken);

        return result.Documents
            .Select(envelope => (DistributedGroundworkDocuments.Deserialize<TransportItemDocument>(envelope).Item, envelope.Version))
            .ToArray();
    }

    private static DocumentQuery CountQuery(string workflowExecutionId) => new(
        Kind,
        DistributedGroundworkStorageManifest.CountPendingCommandsQuery,
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
            OrdinalKey(workflowExecutionId)))]);

    // The document id embeds the escaped execution id and the sequence so a duplicate sequence allocation collides
    // on the id and is refused by the store. Escaping ('%' then ':') keeps a separator inside an execution id from
    // forging another execution's item id; case is preserved because execution ids are ordinal.
    private static string ComposeTransportItemId(string workflowExecutionId, long sequence) =>
        $"transport:{workflowExecutionId.Replace("%", "%25").Replace(":", "%3A")}:{sequence}";

    private static string ComposeStreamHeadId(string workflowExecutionId) =>
        $"transport-head:{workflowExecutionId.Replace("%", "%25").Replace(":", "%3A")}";

    private sealed record StreamHeadDocument(string Collection, string WorkflowExecutionId, long LastSequence);

    // The lifted collection, workflow comparison key, visibility, and sequence fields serve the declared physical
    // routes; the nested item remains the frozen v1 executionCommandTransport wire shape.
    private sealed record TransportItemDocument(
        string Collection,
        string WorkflowExecutionId,
        string WorkflowExecutionIdKey,
        DateTimeOffset VisibleAt,
        long Sequence,
        ExecutionCommandTransportItem Item)
    {
        public static TransportItemDocument From(string collection, ExecutionCommandTransportItem item) => new(
            collection,
            item.WorkflowExecutionId,
            OrdinalKey(item.WorkflowExecutionId),
            item.LeaseExpiresAt ?? DateTimeOffset.MinValue,
            item.Sequence,
            item);
    }

    private static string OrdinalKey(string value) =>
        PortableStringComparison.CreateOrdinal(value);
}
