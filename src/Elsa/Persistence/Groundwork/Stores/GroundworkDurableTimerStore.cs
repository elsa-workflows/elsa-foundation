using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IDurableTimerStore"/> backed by provider-portable Groundwork documents. Creation is
/// existing-wins and create-only; delivery uses bounded, renewable, expected-version claims so a stale
/// timer pump cannot remove a timer reclaimed by a successor.
/// </summary>
public sealed class GroundworkDurableTimerStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.DurableTimerDocumentKind, boundedStore), IDurableTimerStore
{
    private const int MaxTransitionAttempts = 16;

    public bool SupportsClaimTransitions => true;

    public async ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = PhysicalDocumentId(timer.WorkflowExecutionId, timer.TimerId);
        var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
        if (existing is not null)
        {
            var existingTimer = Deserialize(existing).Timer;
            EnsureLogicalIdentity(existingTimer, timer.WorkflowExecutionId, timer.TimerId);
            return existingTimer;
        }

        var result = await SaveDocumentAsync(
            documentId,
            NewEnvelope(timer),
            cancellationToken,
            expectedVersion: 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return timer;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Groundwork rejected durable timer '{timer.TimerId}' with status '{result.Status}'.");

        existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Durable timer '{timer.TimerId}' conflicted during creation but could not be reloaded.");
        var winner = Deserialize(existing).Timer;
        EnsureLogicalIdentity(winner, timer.WorkflowExecutionId, timer.TimerId);
        return winner;
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Due-timer listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListDueDurableTimersQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    ElsaRuntimeStorageManifest.DurableTimerDueTimeField,
                    asOf.ToString("O", CultureInfo.InvariantCulture)))],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerDueTimeField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerIdField)
                ],
                take: limit),
            cancellationToken);
        return result.Documents.Select(envelope => Deserialize(envelope).Timer).ToArray();
    }

    public async ValueTask<DurableTimer?> FindAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        return await LoadDocumentAsync<DurableTimerEnvelope, DurableTimer>(
            PhysicalDocumentId(workflowExecutionId, timerId),
            envelope => envelope.Timer,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<DurableTimer>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var timers = await QueryDocumentsAsync<DurableTimerEnvelope, DurableTimer>(
            ElsaRuntimeStorageManifest.ListDurableTimersByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            envelope => envelope.Timer,
            cancellationToken);
        return timers.OrderBy(timer => timer.TimerId, StringComparer.Ordinal).ToArray();
    }

    public async ValueTask DeleteAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();
        var documentId = PhysicalDocumentId(workflowExecutionId, timerId);

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
            if (existing is null)
                return;
            EnsureLogicalIdentity(Deserialize(existing).Timer, workflowExecutionId, timerId);

            var result = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, documentId, existing.Version),
                cancellationToken);
            if (result.Status is DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound)
                return;
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                continue;

            throw new InvalidOperationException($"Groundwork rejected durable timer deletion for '{timerId}' with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("delete", workflowExecutionId, timerId);
    }

    public async ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
        RuntimeDurableTimerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField,
                    ClaimOrderUpperBound(request.Now)))],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField)],
                take: request.Limit),
            cancellationToken);
        var claims = new List<RuntimeDurableTimerClaim>(Math.Min(request.Limit, result.Documents.Count));
        foreach (var candidate in result.Documents)
        {
            if (claims.Count == request.Limit)
                break;

            var existing = await Store.LoadAsync(DocumentKind, candidate.Id, cancellationToken);
            if (existing is null)
                continue;
            var current = Deserialize(existing);
            if (!CanClaim(current, request))
                continue;

            var claimedAt = request.Now;
            var visibleAfter = claimedAt.Add(request.VisibilityTimeout);
            var updated = current with
            {
                ClaimOrderKey = ClaimOrderKey(visibleAfter, current.Timer),
                ClaimOwnerId = request.OwnerId,
                ClaimToken = checked(current.ClaimToken + 1),
                ClaimedAt = claimedAt,
                VisibleAfter = visibleAfter
            };
            var write = await SaveDocumentAsync(existing.Id, updated, cancellationToken, existing.Version);
            if (write.Status == DocumentStoreWriteStatus.Saved)
            {
                claims.Add(NewClaim(updated, write.Document?.Version ?? checked(existing.Version + 1)));
                continue;
            }

            if (write.Status is not (DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound))
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected durable timer claim for '{current.Timer.TimerId}' with status '{write.Status}'.");
            }
        }

        return claims;
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Durable timer visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;

        var visibleAfter = now.Add(visibilityTimeout);
        var updated = existing.Value.Envelope with
        {
            ClaimOrderKey = ClaimOrderKey(visibleAfter, claim.Timer),
            VisibleAfter = visibleAfter
        };
        var result = await SaveDocumentAsync(existing.Value.Document.Id, updated, cancellationToken, claim.Revision);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => RuntimeDurableTimerClaimTransitionResult.Applied(
                NewClaim(updated, result.Document?.Version ?? checked(claim.Revision + 1))),
            DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound =>
                RuntimeDurableTimerClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected durable timer claim renewal for '{claim.Timer.TimerId}' with status '{result.Status}'.")
        };
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;

        var result = await Store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, existing.Value.Document.Id, claim.Revision),
            cancellationToken);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Deleted => RuntimeDurableTimerClaimTransitionResult.Applied(),
            DocumentStoreWriteStatus.NotFound => RuntimeDurableTimerClaimTransitionResult.AlreadyApplied,
            DocumentStoreWriteStatus.ConcurrencyConflict => RuntimeDurableTimerClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected durable timer claim completion for '{claim.Timer.TimerId}' with status '{result.Status}'.")
        };
    }

    public async ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeDurableTimerClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeDurableTimerClaimTransitionResult.Stale;

        var updated = existing.Value.Envelope with
        {
            ClaimOrderKey = ClaimOrderKey(visibleAt, claim.Timer),
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleAfter = visibleAt,
            FailureCount = checked(existing.Value.Envelope.FailureCount + 1)
        };
        var result = await SaveDocumentAsync(existing.Value.Document.Id, updated, cancellationToken, claim.Revision);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => RuntimeDurableTimerClaimTransitionResult.Applied(),
            DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound =>
                RuntimeDurableTimerClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected durable timer claim release for '{claim.Timer.TimerId}' with status '{result.Status}'.")
        };
    }

    private async ValueTask<(DocumentEnvelope Document, DurableTimerEnvelope Envelope)?> LoadClaimDocumentAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken)
    {
        var document = await Store.LoadAsync(
            DocumentKind,
            PhysicalDocumentId(claim.Timer.WorkflowExecutionId, claim.Timer.TimerId),
            cancellationToken);
        if (document is null)
            return null;

        var envelope = Deserialize(document);
        EnsureLogicalIdentity(envelope.Timer, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        return (document, envelope);
    }

    private DurableTimerEnvelope Deserialize(DocumentEnvelope document) =>
        Serializer.Deserialize<DurableTimerEnvelope>(document);

    private static DurableTimerEnvelope NewEnvelope(DurableTimer timer) =>
        new(
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
            timer.WorkflowExecutionId,
            ClaimOrderKey(timer.DueTime, timer),
            null,
            0,
            null,
            null,
            0,
            timer);

    private static bool CanClaim(
        DurableTimerEnvelope envelope,
        RuntimeDurableTimerClaimRequest request) =>
        envelope.Timer.DueTime <= request.Now &&
        (envelope.VisibleAfter is null || envelope.VisibleAfter <= request.Now);

    private static RuntimeDurableTimerClaim NewClaim(DurableTimerEnvelope envelope, long revision) =>
        new(
            envelope.Timer,
            envelope.ClaimOwnerId!,
            envelope.ClaimToken,
            revision,
            envelope.ClaimedAt!.Value,
            envelope.VisibleAfter!.Value,
            envelope.FailureCount);

    private static bool Matches(
        DocumentEnvelope document,
        DurableTimerEnvelope envelope,
        RuntimeDurableTimerClaim claim) =>
        document.Version == claim.Revision &&
        envelope.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(envelope.ClaimOwnerId, claim.OwnerId);

    private static string PhysicalDocumentId(string workflowExecutionId, string timerId) =>
        GroundworkPhysicalDocumentId.FromLogicalId(
            GroundworkCompositeDocumentId.From(workflowExecutionId, timerId));

    private static string ClaimOrderKey(DateTimeOffset availableAt, DurableTimer timer) =>
        $"{availableAt.UtcTicks:D19}.{StableHash(GroundworkCompositeDocumentId.From(timer.WorkflowExecutionId, timer.TimerId))}";

    private static string ClaimOrderUpperBound(DateTimeOffset asOf) => $"{asOf.UtcTicks:D19}.~";

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureLogicalIdentity(DurableTimer timer, string workflowExecutionId, string timerId)
    {
        if (!StringComparer.Ordinal.Equals(timer.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(timer.TimerId, timerId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for durable timer '{timerId}' in workflow execution '{workflowExecutionId}'.");
        }
    }

    private static InvalidOperationException TransitionDidNotSettle(
        string transition,
        string workflowExecutionId,
        string timerId) =>
        new(
            $"Durable timer {transition} for workflow execution '{workflowExecutionId}' and timer '{timerId}' " +
            $"did not settle after {MaxTransitionAttempts} compare-and-swap attempts.");

    private sealed record DurableTimerEnvelope(
        string Collection,
        string WorkflowExecutionId,
        string ClaimOrderKey,
        string? ClaimOwnerId,
        long ClaimToken,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? VisibleAfter,
        int FailureCount,
        DurableTimer Timer);
}
