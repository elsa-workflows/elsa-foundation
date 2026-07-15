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
public sealed class GroundworkDurableTimerStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.DurableTimerDocumentKind, boundedStore), IDurableTimerStore
{
    public async ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = GroundworkCompositeDocumentId.From(timer.WorkflowExecutionId, timer.TimerId);
        var existing = await LoadDocumentAsync<DurableTimerEnvelope, DurableTimer>(
            documentId, envelope => envelope.Timer, cancellationToken);
        if (existing is not null)
            return existing;

        var document = new DurableTimerEnvelope(
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            timer.WorkflowExecutionId,
            timer);
        await SaveDocumentAsync(documentId, document, cancellationToken);

        return timer;
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-timer listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var timers = await QueryDocumentsAsync<DurableTimerEnvelope, DurableTimer>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            envelope => envelope.Timer,
            cancellationToken);

        return timers
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

        return await LoadDocumentAsync<DurableTimerEnvelope, DurableTimer>(
            GroundworkCompositeDocumentId.From(workflowExecutionId, timerId), envelope => envelope.Timer, cancellationToken);
    }

    public async ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        await DeleteDocumentAsync(GroundworkCompositeDocumentId.From(workflowExecutionId, timerId), cancellationToken);
    }

    // The constant collection partition lets the due-timer sweep use a keyword equality index instead of a
    // provider-wide scan, mirroring the other list-capable bridges.
    private sealed record DurableTimerEnvelope(string Collection, string WorkflowExecutionId, DurableTimer Timer);
}
