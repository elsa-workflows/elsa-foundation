using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IDurableTimerStore"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/>. Each timer is one document, so pending timers survive process restarts —
/// this is what makes a <c>Delay</c> restart-durable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scan cost.</b> Groundwork's portable query contract is equality-only (no native range/order index),
/// so <see cref="ListDueAsync"/> queries the whole timer partition through the constant <c>by-collection</c>
/// keyword index, then filters <c>DueTime &lt;= asOf</c>, orders, and caps in memory — mirroring the other
/// list-capable bridges. The pump's per-tick limit bounds dispatches, not this load: a large backlog of
/// not-yet-due timers is still materialized each sweep. A native range/due-time index in Groundwork is a
/// recorded follow-up (see <c>EXTENSION_POINTS.md</c>).
/// </para>
/// <para>
/// <see cref="SaveAsync"/> is an idempotent upsert keyed by (WorkflowExecutionId, TimerId): an existing
/// timer wins and is returned, so a deterministic-id re-invoke after a crash cannot duplicate a timer or
/// shift a committed deadline.
/// </para>
/// </remarks>
public sealed class GroundworkDurableTimerStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IDurableTimerStore
{
    public async ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = BuildId(timer.WorkflowExecutionId, timer.TimerId);
        var existing = await store.LoadAsync(
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            documentId,
            cancellationToken);
        if (existing is not null)
            return Map(existing);

        var envelope = new DurableTimerEnvelope(
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            timer.WorkflowExecutionId,
            timer);
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.DurableTimerDocumentKind, envelope);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
                documentId,
                schemaVersion,
                content),
            cancellationToken);

        return timer;
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-timer listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind),
            cancellationToken);

        return envelopes
            .Select(Map)
            .Where(timer => timer.DueTime <= asOf)
            .OrderBy(timer => timer.DueTime)
            .ThenBy(timer => timer.TimerId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            BuildId(workflowExecutionId, timerId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        await store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
                BuildId(workflowExecutionId, timerId)),
            cancellationToken);
    }

    private DurableTimer Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<DurableTimerEnvelope>(envelope).Timer;

    // Deterministic, collision-free composite document id. Parts are escaped so a separator inside an id
    // cannot forge a different (workflowExecutionId, timerId) pair.
    private static string BuildId(string workflowExecutionId, string timerId) =>
        $"{Escape(workflowExecutionId)}:{Escape(timerId)}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");

    // The constant collection partition lets the due-timer sweep use a keyword equality index instead of a
    // provider-wide scan, mirroring the other list-capable bridges.
    private sealed record DurableTimerEnvelope(string Collection, string WorkflowExecutionId, DurableTimer Timer);
}
