using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IWorkflowSchedulerWorkQueue"/> backed by provider-portable Groundwork documents.
/// Enqueue is create-only by Elsa's scoped work-item identity; delivery uses renewable, expected-version
/// claims so a stale consumer cannot acknowledge or remove work reclaimed by a successor.
/// </summary>
public sealed class GroundworkWorkflowSchedulerWorkQueue(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind, boundedStore), IWorkflowSchedulerWorkQueue, IWorkflowSchedulerWorkClaimInspection, IRuntimeSchedulerWorkItemResolver
{
    private const int MaxTransitionAttempts = 16;
    private readonly RuntimeCheckpointRecoveryAuthorityCodec _recoveryAuthorityCodec = new();

    public bool SupportsClaimTransitions => true;

    public async ValueTask<RuntimeCheckpointRecoveryResolution> ResolveAsync(
        RuntimeCheckpointRecoveryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();

        if (authority.Version != 1 || !StringComparer.Ordinal.Equals(authority.Kind, "runtime.scheduler-work"))
            return new(RuntimeCheckpointRecoveryStatus.UnsupportedVersionOrKind, authority);

        ArgumentException.ThrowIfNullOrWhiteSpace(authority.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.WorkItemId);

        try
        {
            var matches = new List<RuntimeSchedulerWorkItem>(2);
            string? continuation = null;
            do
            {
                var page = await ListAsync(
                    new RuntimeSchedulerWorkQuery(
                        authority.WorkflowExecutionId,
                        IRuntimeSchedulerWorkItemResolver.MaximumPageSize,
                        continuation),
                    cancellationToken);
                matches.AddRange(page.Items.Where(item =>
                    StringComparer.Ordinal.Equals(item.WorkItemId, authority.WorkItemId)).Take(2 - matches.Count));
                continuation = page.NextContinuationToken;
            } while (continuation is not null && matches.Count < 2);

            if (matches.Count == 0)
                return new(RuntimeCheckpointRecoveryStatus.Missing, authority);
            if (matches.Count > 1)
                return new(RuntimeCheckpointRecoveryStatus.Ambiguous, authority);

            var item = matches[0];
            var actual = _recoveryAuthorityCodec.Encode(item);
            return StringComparer.Ordinal.Equals(actual.Fingerprint, authority.Fingerprint)
                ? new(RuntimeCheckpointRecoveryStatus.Exact, authority, item)
                : new(RuntimeCheckpointRecoveryStatus.FingerprintMismatch, authority);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and
                                          not RuntimeSchedulerWorkRecoveryResolutionException)
        {
            throw new RuntimeSchedulerWorkRecoveryResolutionException(
                authority.WorkflowExecutionId,
                authority.WorkItemId,
                exception);
        }
    }

    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = PhysicalDocumentId(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
        if (existing is not null)
        {
            var existingItem = Deserialize(existing).Item;
            EnsureLogicalIdentity(existingItem, workItem.WorkflowExecutionId, workItem.WorkItemId);
            return existingItem;
        }

        var result = await SaveDocumentAsync(documentId, NewEnvelope(workItem), cancellationToken, expectedVersion: 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return workItem;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Groundwork rejected scheduler work item '{workItem.WorkItemId}' with status '{result.Status}'.");

        existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Scheduler work item '{workItem.WorkItemId}' conflicted during creation but could not be reloaded.");
        var item = Deserialize(existing).Item;
        EnsureLogicalIdentity(item, workItem.WorkflowExecutionId, workItem.WorkItemId);
        return item;
    }

    public async ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await QueryOrderedAsync(query, cancellationToken);
        return new RuntimeStorePage<RuntimeSchedulerWorkItem>(
            query,
            result.Documents.Select(document => Deserialize(document).Item).ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var head = await FirstOrderedAsync(workflowExecutionId, cancellationToken);
            if (head is null)
                return null;

            var envelope = Deserialize(head);
            EnsureLogicalIdentity(envelope.Item, workflowExecutionId, envelope.Item.WorkItemId);
            var result = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, head.Id, head.Version),
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Deleted)
                return envelope.Item;
            if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
                continue;

            throw new InvalidOperationException($"Groundwork rejected scheduler work dequeue for '{envelope.Item.WorkItemId}' with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("dequeue", workflowExecutionId);
    }

    public async ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var documentId = PhysicalDocumentId(workflowExecutionId, workItemId);

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
            if (existing is null)
                return false;
            EnsureLogicalIdentity(Deserialize(existing).Item, workflowExecutionId, workItemId);

            var result = await Store.DeleteAsync(
                new DeleteDocumentRequest(DocumentKind, documentId, existing.Version),
                cancellationToken);
            if (result.Status == DocumentStoreWriteStatus.Deleted)
                return true;
            if (result.Status == DocumentStoreWriteStatus.NotFound)
                return false;
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                continue;

            throw new InvalidOperationException($"Groundwork rejected scheduler work deletion for '{workItemId}' with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("delete", workflowExecutionId, workItemId);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                    DocumentKind,
                    ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery,
                    [
                        DocumentQueryClause.Of(
                            DocumentQueryComparison.Equal(
                                ElsaRuntimeStorageManifest.CollectionField,
                                DocumentKind))
                    ],
                    [
                        new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                        new DocumentQueryOrder(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)
                    ],
                    take: limit)
                .LatestPerKey(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
            cancellationToken);

        return result.Documents
            .Select(document => Deserialize(document).Item.WorkflowExecutionId)
            .ToArray();
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(
        string workflowExecutionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var claims = new List<RuntimeSchedulerWorkClaim>();
        var query = new RuntimeSchedulerWorkQuery(workflowExecutionId);
        do
        {
            var page = await QueryOrderedAsync(query, cancellationToken);
            foreach (var document in page.Documents)
            {
                var envelope = Deserialize(document);
                if (envelope.ClaimedAt is not null && envelope.VisibleAfter is { } visibleAfter && visibleAfter > now)
                    claims.Add(NewClaim(envelope, document.Version));
            }
            query = new RuntimeSchedulerWorkQuery(workflowExecutionId, query.Limit, page.NextContinuation);
        } while (query.ContinuationToken is not null);
        return claims;
    }

    public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var head = await FirstOrderedAsync(request.WorkflowExecutionId, cancellationToken);
            if (head is null)
                return null;

            var current = Deserialize(head);
            EnsureLogicalIdentity(current.Item, request.WorkflowExecutionId, current.Item.WorkItemId);
            if (current.VisibleAfter is { } visibleAfter && visibleAfter > request.Now)
                return null;

            var claimedAt = request.Now;
            var updated = current with
            {
                ClaimOwnerId = request.OwnerId,
                ClaimToken = checked(current.ClaimToken + 1),
                ClaimedAt = claimedAt,
                VisibleAfter = claimedAt.Add(request.VisibilityTimeout)
            };
            var result = await SaveDocumentAsync(head.Id, updated, cancellationToken, head.Version);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return NewClaim(updated, result.Document?.Version ?? checked(head.Version + 1));
            if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
                continue;

            throw new InvalidOperationException($"Groundwork rejected scheduler work claim for '{current.Item.WorkItemId}' with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("claim", request.WorkflowExecutionId);
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Scheduler work visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var updated = existing.Value.Envelope with { VisibleAfter = now.Add(visibilityTimeout) };
        var result = await SaveDocumentAsync(existing.Value.Document.Id, updated, cancellationToken, claim.Revision);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => RuntimeSchedulerWorkClaimTransitionResult.Applied(
                NewClaim(updated, result.Document?.Version ?? checked(claim.Revision + 1))),
            DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected scheduler work claim renewal for '{claim.Item.WorkItemId}' with status '{result.Status}'.")
        };
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var result = await Store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, existing.Value.Document.Id, claim.Revision),
            cancellationToken);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Deleted => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            DocumentStoreWriteStatus.NotFound => RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied,
            DocumentStoreWriteStatus.ConcurrencyConflict => RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected scheduler work claim completion for '{claim.Item.WorkItemId}' with status '{result.Status}'.")
        };
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await LoadClaimDocumentAsync(claim, cancellationToken);
        if (existing is null)
            return RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied;
        if (!Matches(existing.Value.Document, existing.Value.Envelope, claim))
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var updated = existing.Value.Envelope with
        {
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleAfter = visibleAt
        };
        var result = await SaveDocumentAsync(existing.Value.Document.Id, updated, cancellationToken, claim.Revision);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Saved => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected scheduler work claim release for '{claim.Item.WorkItemId}' with status '{result.Status}'.")
        };
    }

    public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = PhysicalDocumentId(consumed.WorkflowExecutionId, consumed.WorkItemId);
        var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
        if (existing is null)
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var envelope = Deserialize(existing);
        EnsureLogicalIdentity(envelope.Item, consumed.WorkflowExecutionId, consumed.WorkItemId);
        // Fence on owner + fencing token only (renewal-stable): a renewal advances the document version but keeps these,
        // so the consume survives an in-flight renewal while a successor reclaim (token advanced) is claim-lost.
        if (envelope.ClaimOwnerId is null ||
            !StringComparer.Ordinal.Equals(envelope.ClaimOwnerId, consumed.ClaimOwnerId) ||
            envelope.ClaimToken != consumed.FencingToken)
            return RuntimeSchedulerWorkClaimTransitionResult.Stale;

        var result = await Store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, documentId, existing.Version),
            cancellationToken);
        return result.Status switch
        {
            DocumentStoreWriteStatus.Deleted => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            DocumentStoreWriteStatus.NotFound or DocumentStoreWriteStatus.ConcurrencyConflict =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork rejected scheduler work claim consumption for '{consumed.WorkItemId}' with status '{result.Status}'.")
        };
    }

    private async ValueTask<(DocumentEnvelope Document, WorkQueueEnvelope Envelope)?> LoadClaimDocumentAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken)
    {
        var document = await Store.LoadAsync(
            DocumentKind,
            PhysicalDocumentId(claim.Item.WorkflowExecutionId, claim.Item.WorkItemId),
            cancellationToken);
        if (document is null)
            return null;

        var envelope = Deserialize(document);
        EnsureLogicalIdentity(envelope.Item, claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        return (document, envelope);
    }

    private async ValueTask<DocumentEnvelope?> FirstOrderedAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var result = await QueryOrderedAsync(
            new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: 1),
            cancellationToken);
        return result.Documents.FirstOrDefault();
    }

    private async ValueTask<DocumentQueryResult> QueryOrderedAsync(
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken)
    {
        var documentQuery = new DocumentQuery(
            DocumentKind,
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            [
                DocumentQueryClause.Of(
                    DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                        query.WorkflowExecutionId))
            ],
            [new DocumentQueryOrder(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)],
            take: query.Limit,
            continuation: query.ContinuationToken);

        return await BoundedStore.QueryAsync(documentQuery, cancellationToken);
    }

    private WorkQueueEnvelope Deserialize(DocumentEnvelope document) =>
        Serializer.Deserialize<WorkQueueEnvelope>(document);

    private static WorkQueueEnvelope NewEnvelope(RuntimeSchedulerWorkItem item) =>
        new(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            item.WorkflowExecutionId,
            item.ExecutionScopeId,
            item.Attempt,
            OrderKey(item),
            null,
            0,
            null,
            null,
            item);

    private static RuntimeSchedulerWorkClaim NewClaim(WorkQueueEnvelope envelope, long revision) =>
        new(
            envelope.Item,
            envelope.ClaimOwnerId!,
            envelope.ClaimToken,
            revision,
            envelope.ClaimedAt!.Value,
            envelope.VisibleAfter!.Value);

    private static bool Matches(
        DocumentEnvelope document,
        WorkQueueEnvelope envelope,
        RuntimeSchedulerWorkClaim claim) =>
        document.Version == claim.Revision &&
        envelope.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(envelope.ClaimOwnerId, claim.OwnerId);

    private static string PhysicalDocumentId(string workflowExecutionId, string workItemId) =>
        GroundworkPhysicalDocumentId.FromLogicalId(GroundworkCompositeDocumentId.From(workflowExecutionId, workItemId));

    private static string OrderKey(RuntimeSchedulerWorkItem item) =>
        $"{OrderPrefix(item.WorkflowExecutionId)}{item.RecordedAt.UtcTicks:D19}.{item.Sequence ?? long.MaxValue:D20}.{StableHash(item.WorkItemId)}";

    private static string OrderPrefix(string workflowExecutionId) => $"{StableHash(workflowExecutionId)}.";

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureLogicalIdentity(RuntimeSchedulerWorkItem item, string workflowExecutionId, string workItemId)
    {
        if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(item.WorkItemId, workItemId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for scheduler work item '{workItemId}' in workflow execution '{workflowExecutionId}'.");
        }
    }

    private static InvalidOperationException TransitionDidNotSettle(
        string transition,
        string workflowExecutionId,
        string? workItemId = null) =>
        new(
            $"Scheduler work {transition} for workflow execution '{workflowExecutionId}'" +
            (workItemId is null ? "" : $" and work item '{workItemId}'") +
            $" did not settle after {MaxTransitionAttempts} compare-and-swap attempts.");

    // Fixed hashes keep the lifted order key under every provider's keyword-index bound;
    // physical/logical identity verification fails closed on a collision.
    private sealed record WorkQueueEnvelope(
        string Collection,
        string WorkflowExecutionId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        string OrderKey,
        string? ClaimOwnerId,
        long ClaimToken,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? VisibleAfter,
        RuntimeSchedulerWorkItem Item);
}
