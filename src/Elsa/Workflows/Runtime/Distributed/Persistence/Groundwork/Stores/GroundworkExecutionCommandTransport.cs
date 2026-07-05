using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IExecutionCommandTransport"/> backed by the portable <see cref="IDocumentStore"/> (W27). Each
/// queued command is one document (document id = transport item id, a composite of the escaped workflow execution id
/// and the per-execution sequence), so the cross-node inbox survives process death and a survivor re-leases and
/// re-drives stranded items on failover.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sequence allocation.</b> <see cref="SendAsync"/> allocates max+1 over the execution's items and creates the
/// item document with <c>ExpectedVersion</c> = 0 (create-only). Because the document id embeds the sequence, two
/// concurrent senders that allocate the same number collide on the same id and the provider's storage layer refuses
/// the loser, which re-reads and takes the next sequence — per-execution sequences are strictly unique and
/// monotonic, enforced by the store, never trusted from memory. Sequential sends from one node observe their own
/// writes, so per-sender FIFO holds; the drain order is deterministic by <c>(Sequence, TransportItemId)</c>.
/// </para>
/// <para>
/// <b>Lease/ack.</b> Delivery is at-least-once with ack-based visibility leases, exactly like the in-memory
/// transport: leasing stamps the item behind <c>ExpectedVersion</c> CAS (a concurrent leaser wins at the storage
/// layer and the loser skips), and only the live lease holder may ack — an ack CAS-deletes and is refused when the
/// lease expired and another node re-leased. The persisted <see cref="ExecutionCommandTransportItem"/> is the frozen
/// v1 <c>executionCommandTransport</c> golden fixture shape; the wrapping document adds only the lifted
/// <c>workflowExecutionId</c> and constant collection partition for the declared indexes.
/// </para>
/// </remarks>
public sealed class GroundworkExecutionCommandTransport(IDocumentStore store) : IExecutionCommandTransport
{
    private const string Kind = DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind;
    private const int MaxCreateAttempts = 16;

    public async ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var items = await LoadItemsAsync(workflowExecutionId, cancellationToken);
            var sequence = items.Count == 0 ? 1 : items.Max(entry => entry.Item.Sequence) + 1;
            var transportItemId = ComposeTransportItemId(workflowExecutionId, sequence);

            var item = new ExecutionCommandTransportItem(
                transportItemId: transportItemId,
                workflowExecutionId: workflowExecutionId,
                envelope: envelope,
                sequence: sequence,
                enqueuedAt: now);

            // Create-only (ExpectedVersion 0): a concurrent sender that allocated the same sequence collides on
            // the same document id and the provider's storage layer refuses the loser — no command can be lost to
            // a silent overwrite, and per-execution sequences stay strictly unique.
            var result = await store.SaveAsync(
                new SaveDocumentRequest(
                    Kind,
                    transportItemId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(new TransportItemDocument(Kind, workflowExecutionId, item)),
                    ExpectedVersion: 0),
                cancellationToken);

            if (result.Status == DocumentStoreWriteStatus.Saved)
                return item;

            // A concurrent sender created this sequence first; re-read the inbox and take the next one.
        }

        throw new InvalidOperationException(
            $"Sending a command to workflow execution '{workflowExecutionId}' did not settle after {MaxCreateAttempts} sequence-allocation attempts.");
    }

    public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        cancellationToken.ThrowIfCancellationRequested();

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Max items must be greater than zero.");

        var leaseExpiresAt = now + leaseDuration;
        var leased = new List<ExecutionCommandTransportItem>();

        foreach (var (item, version) in await LoadItemsAsync(workflowExecutionId, cancellationToken))
        {
            if (leased.Count >= maxItems)
                break;

            if (!item.IsVisible(now))
                continue;

            // The lease stamp is guarded by the loaded envelope version, so a concurrent leaser wins at the
            // storage layer and the loser skips the item instead of double-holding it.
            var leasedItem = item.Lease(ownerId, leaseExpiresAt);
            var result = await store.SaveAsync(
                new SaveDocumentRequest(
                    Kind,
                    item.TransportItemId,
                    DistributedGroundworkStorageManifest.SchemaVersion,
                    DistributedGroundworkDocuments.Serialize(new TransportItemDocument(Kind, workflowExecutionId, leasedItem)),
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
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
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

    public async ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(Kind, DistributedGroundworkStorageManifest.ByCollectionIndex, Kind),
            cancellationToken);

        return envelopes
            .Select(envelope => DistributedGroundworkDocuments.Deserialize<TransportItemDocument>(envelope).Item)
            .Where(item => item.IsVisible(now))
            .Select(item => item.WorkflowExecutionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return (await LoadItemsAsync(workflowExecutionId, cancellationToken)).Count;
    }

    private async Task<IReadOnlyList<(ExecutionCommandTransportItem Item, long Version)>> LoadItemsAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(Kind, DistributedGroundworkStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId),
            cancellationToken);

        return envelopes
            .Select(envelope => (DistributedGroundworkDocuments.Deserialize<TransportItemDocument>(envelope).Item, envelope.Version))
            .OrderBy(entry => entry.Item.Sequence)
            .ThenBy(entry => entry.Item.TransportItemId, StringComparer.Ordinal)
            .ToArray();
    }

    // The document id embeds the escaped execution id and the sequence so a duplicate sequence allocation collides
    // on the id and is refused by the store. Escaping ('%' then ':') keeps a separator inside an execution id from
    // forging another execution's item id; case is preserved because execution ids are ordinal.
    private static string ComposeTransportItemId(string workflowExecutionId, long sequence) =>
        $"transport:{workflowExecutionId.Replace("%", "%25").Replace(":", "%3A")}:{sequence}";

    // The lifted workflowExecutionId and constant collection partition serve the declared indexes; the nested item
    // is the frozen v1 executionCommandTransport wire shape, unchanged.
    private sealed record TransportItemDocument(string Collection, string WorkflowExecutionId, ExecutionCommandTransportItem Item);
}
