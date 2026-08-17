using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 scheduler-work queue.</summary>
/// <remarks>
/// Scheduler work is scoped to one persistence scope and uses the public Groundwork row/session APIs.
/// Create-only enqueue, keyset FIFO reads, and optimistic claim transitions preserve the durable queue
/// contract without importing the v1 document-store bridge. Long logical work-item identities use the
/// shared hashed physical alias and are validated against the complete JSON envelope on every access.
/// </remarks>
public sealed class GroundworkV2WorkflowSchedulerWorkQueue :
    IWorkflowSchedulerWorkQueue,
    IWorkflowSchedulerWorkClaimInspection
{
    private const int MaxTransitionAttempts = 16;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowSchedulerWorkQueue(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind, targetName);
    }

    public bool SupportsClaimTransitions => true;

    public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(
        RuntimeSchedulerWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var envelope = GroundworkV2SchedulerWorkStorageConventions.NewEnvelope(workItem);
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                workItem.WorkflowExecutionId,
                workItem.WorkItemId));
        if (session.Read(key) is { } existing)
            return ValueTask.FromResult(ExistingItem(existing, workItem.WorkflowExecutionId, workItem.WorkItemId));

        var result = session.Insert(
            GroundworkV2SchedulerWorkStorageConventions.Values(envelope),
            WriteOptions.CreateOnly);
        if (IsSaved(result.Status))
            return ValueTask.FromResult(workItem);
        if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                $"Groundwork scheduler-work enqueue failed with status '{result.Status}'.");
        }

        var winner = session.Read(key)
                      ?? throw new InvalidOperationException(
                          "Groundwork scheduler-work enqueue lost a concurrent write; retry the operation.");
        return ValueTask.FromResult(ExistingItem(winner, workItem.WorkflowExecutionId, workItem.WorkItemId));
    }

    public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(
        RuntimeSchedulerWorkQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorkflowExecutionId(query.WorkflowExecutionId);

        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var order = Column(table, ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(order, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));

        return ValueTask.FromResult(new RuntimeStorePage<RuntimeSchedulerWorkItem>(
            query,
            result.Rows.Select(row => Deserialize(row).Item).ToArray(),
            result.NextContinuationToken));
    }

    public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var head = FirstOrdered(session, workflowExecutionId);
            if (head is null)
                return ValueTask.FromResult<RuntimeSchedulerWorkItem?>(null);

            var envelope = Deserialize(head.Values.Values);
            GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
                envelope,
                workflowExecutionId,
                envelope.Item.WorkItemId);
            var revision = Revision(head);
            var result = session.Delete(
                GroundworkRuntimeRowStore.Key(RequiredId(head.Values.Values)),
                WriteOptions.IfVersion(revision));
            if (result.Status == WriteOutcomeStatus.Deleted)
                return ValueTask.FromResult<RuntimeSchedulerWorkItem?>(envelope.Item);
            if (result.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
                continue;
            throw new InvalidOperationException(
                $"Groundwork scheduler-work dequeue failed with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("dequeue", workflowExecutionId);
    }

    public ValueTask<bool> DeleteAsync(
        string workflowExecutionId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, workItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2SchedulerWorkStorageConventions.PhysicalId(workflowExecutionId, workItemId));

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var existing = session.Read(key);
            if (existing is null)
                return ValueTask.FromResult(false);
            _ = ExistingItem(existing, workflowExecutionId, workItemId);

            var result = session.Delete(key, WriteOptions.IfVersion(Revision(existing)));
            if (result.Status == WriteOutcomeStatus.Deleted)
                return ValueTask.FromResult(true);
            if (result.Status == WriteOutcomeStatus.NotFound)
                return ValueTask.FromResult(false);
            if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
                continue;
            throw new InvalidOperationException(
                $"Groundwork scheduler-work delete failed with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("delete", workflowExecutionId, workItemId);
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();

        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var recordedAt = Column(table, ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField);
        var order = Column(table, ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField);
        var result = Open().Query(new QueryRequest(
            table,
            Predicate.AlwaysTrue.Instance,
            [
                new OrderTerm(workflow, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(recordedAt, OrderDirection.Descending, NullOrder.First),
                new OrderTerm(order, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(limit),
            new LatestPerKey(workflow, recordedAt)));

        return ValueTask.FromResult<IReadOnlyCollection<string>>(
            result.Rows.Select(row => Deserialize(row).Item.WorkflowExecutionId).ToArray());
    }

    public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(
        string workflowExecutionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var claims = new List<RuntimeSchedulerWorkClaim>();
        var query = new RuntimeSchedulerWorkQuery(workflowExecutionId);
        do
        {
            var page = Query(session, query);
            foreach (var row in page.Rows)
            {
                var envelope = Deserialize(row);
                if (envelope.ClaimedAt is null || envelope.VisibleAfter is not { } visibleAfter || visibleAfter <= now)
                    continue;
                var entry = session.Read(GroundworkRuntimeRowStore.Key(RequiredId(row)));
                if (entry is null)
                    continue;
                envelope = Deserialize(entry.Values.Values);
                if (envelope.ClaimedAt is not null && envelope.VisibleAfter is { } currentVisible && currentVisible > now)
                    claims.Add(NewClaim(envelope, Revision(entry)));
            }

            query = new RuntimeSchedulerWorkQuery(
                workflowExecutionId,
                query.Limit,
                page.NextContinuationToken);
        } while (query.ContinuationToken is not null);

        return ValueTask.FromResult<IReadOnlyCollection<RuntimeSchedulerWorkClaim>>(claims);
    }

    public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorkflowExecutionId(request.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();

        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var head = FirstOrdered(session, request.WorkflowExecutionId);
            if (head is null)
                return ValueTask.FromResult<RuntimeSchedulerWorkClaim?>(null);

            var current = Deserialize(head.Values.Values);
            GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
                current,
                request.WorkflowExecutionId,
                current.Item.WorkItemId);
            if (current.VisibleAfter is { } visibleAfter && visibleAfter > request.Now)
                return ValueTask.FromResult<RuntimeSchedulerWorkClaim?>(null);

            var updated = current with
            {
                ClaimOwnerId = request.OwnerId,
                ClaimToken = checked(current.ClaimToken + 1),
                ClaimedAt = request.Now,
                VisibleAfter = request.Now.Add(request.VisibilityTimeout)
            };
            var revision = Revision(head);
            var result = ConditionalUpsert(session, updated, revision);
            if (result.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
                continue;
            if (IsSaved(result.Status))
            {
                return ValueTask.FromResult<RuntimeSchedulerWorkClaim?>(
                    NewClaim(updated, result.Version ?? checked(revision + 1)));
            }

            throw new InvalidOperationException(
                $"Groundwork scheduler-work claim failed with status '{result.Status}'.");
        }

        throw TransitionDidNotSettle("claim", request.WorkflowExecutionId);
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Scheduler work visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value.Entry, current.Value.Envelope, claim))
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.Stale);

        var updated = current.Value.Envelope with { VisibleAfter = now.Add(visibilityTimeout) };
        var result = ConditionalUpsert(session, updated, claim.Revision);
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ when IsSaved(result.Status) => RuntimeSchedulerWorkClaimTransitionResult.Applied(
                NewClaim(updated, result.Version ?? checked(claim.Revision + 1))),
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-work claim renewal failed with status '{result.Status}'.")
        });
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value.Entry, current.Value.Envelope, claim))
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.Stale);

        var result = session.Delete(
            GroundworkRuntimeRowStore.Key(RequiredId(current.Value.Entry.Values.Values)),
            WriteOptions.IfVersion(claim.Revision));
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.Deleted => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            WriteOutcomeStatus.NotFound => RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied,
            WriteOutcomeStatus.ConcurrencyConflict => RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-work claim completion failed with status '{result.Status}'.")
        });
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value.Entry, current.Value.Envelope, claim))
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.Stale);

        var updated = current.Value.Envelope with
        {
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleAfter = visibleAt
        };
        var result = ConditionalUpsert(session, updated, claim.Revision);
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ when IsSaved(result.Status) => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-work claim release failed with status '{result.Status}'.")
        });
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        ValidateIdentity(consumed.WorkflowExecutionId, consumed.WorkItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                consumed.WorkflowExecutionId,
                consumed.WorkItemId));
        var existing = session.Read(key);
        if (existing is null)
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.Stale);

        var envelope = GroundworkV2SchedulerWorkStorageConventions.Deserialize(existing.Values.Values);
        GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
            envelope,
            consumed.WorkflowExecutionId,
            consumed.WorkItemId);
        GroundworkV2SchedulerWorkStorageConventions.EnsurePhysicalIdentity(
            existing.Values.Values,
            envelope);
        // Consumption deliberately fences on owner + token, not the provider revision: a renewal advances the
        // revision but preserves this fence, while a successor reclaim advances the token.
        if (envelope.ClaimOwnerId is null ||
            !StringComparer.Ordinal.Equals(envelope.ClaimOwnerId, consumed.ClaimOwnerId) ||
            envelope.ClaimToken != consumed.FencingToken)
        {
            return ValueTask.FromResult(RuntimeSchedulerWorkClaimTransitionResult.Stale);
        }

        var result = session.Delete(key, WriteOptions.IfVersion(Revision(existing)));
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.Deleted => RuntimeSchedulerWorkClaimTransitionResult.Applied(),
            WriteOutcomeStatus.NotFound or WriteOutcomeStatus.ConcurrencyConflict =>
                RuntimeSchedulerWorkClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-work claim consumption failed with status '{result.Status}'.")
        });
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork scheduler work requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static RuntimeSchedulerWorkItem ExistingItem(
        StoredEntry entry,
        string workflowExecutionId,
        string workItemId)
    {
        // Decode the logical content before checking the physical alias so a row occupying a
        // hashed key can report a collision-specific refusal rather than a generic projection
        // mismatch. Normal query paths still validate the alias before returning an item.
        var envelope = GroundworkV2SchedulerWorkStorageConventions.Deserialize(entry.Values.Values);
        GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
            envelope,
            workflowExecutionId,
            workItemId);
        GroundworkV2SchedulerWorkStorageConventions.EnsurePhysicalIdentity(
            entry.Values.Values,
            envelope);
        return envelope.Item;
    }

    private static GroundworkV2SchedulerWorkEnvelope Deserialize(
        IReadOnlyDictionary<string, object?> values)
    {
        var envelope = GroundworkV2SchedulerWorkStorageConventions.Deserialize(values);
        GroundworkV2SchedulerWorkStorageConventions.EnsurePhysicalIdentity(values, envelope);
        return envelope;
    }

    private static RuntimeSchedulerWorkClaim NewClaim(
        GroundworkV2SchedulerWorkEnvelope envelope,
        long revision) =>
        new(
            envelope.Item,
            envelope.ClaimOwnerId!,
            envelope.ClaimToken,
            revision,
            envelope.ClaimedAt!.Value,
            envelope.VisibleAfter!.Value);

    private static bool Matches(
        StoredEntry entry,
        GroundworkV2SchedulerWorkEnvelope envelope,
        RuntimeSchedulerWorkClaim claim) =>
        Revision(entry) == claim.Revision &&
        envelope.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(envelope.ClaimOwnerId, claim.OwnerId);

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        GroundworkV2SchedulerWorkEnvelope envelope,
        long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic scheduler-work concurrency.");
        }

        return concurrency.ConditionalUpsert(
            GroundworkV2SchedulerWorkStorageConventions.Values(envelope),
            WriteOptions.IfVersion(revision));
    }

    private StoredEntry? FirstOrdered(IStorageSession session, string workflowExecutionId)
    {
        var table = new TableId(session.Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var order = Column(table, ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField);
        var result = session.Query(new QueryRequest(
            table,
            Equal(workflow, workflowExecutionId),
            [new OrderTerm(order, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(1)));
        var row = result.Rows.FirstOrDefault();
        return row is null ? null : session.Read(GroundworkRuntimeRowStore.Key(RequiredId(row)));
    }

    private QueryMaterializedResult Query(
        IStorageSession session,
        RuntimeSchedulerWorkQuery query)
    {
        var table = new TableId(session.Unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var order = Column(table, ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField);
        return session.Query(new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(order, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));
    }

    private (StoredEntry Entry, GroundworkV2SchedulerWorkEnvelope Envelope)? LoadClaim(
        IStorageSession session,
        RuntimeSchedulerWorkClaim claim)
    {
        ValidateIdentity(claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);
        var key = GroundworkRuntimeRowStore.Key(
            GroundworkV2SchedulerWorkStorageConventions.PhysicalId(
                claim.Item.WorkflowExecutionId,
                claim.Item.WorkItemId));
        var entry = session.Read(key);
        if (entry is null)
            return null;
        var envelope = Deserialize(entry.Values.Values);
        GroundworkV2SchedulerWorkStorageConventions.EnsureLogicalIdentity(
            envelope,
            claim.Item.WorkflowExecutionId,
            claim.Item.WorkItemId);
        return (entry, envelope);
    }

    private static string RequiredId(IReadOnlyDictionary<string, object?> values) =>
        values.TryGetValue(ElsaRuntimeV2StorageManifest.IdField, out var raw)
            ? raw switch
            {
                string text when !string.IsNullOrWhiteSpace(text) => text,
                System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element
                    when !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
                _ => throw new InvalidDataException("Groundwork scheduler-work query row has no valid physical identity.")
            }
            : throw new InvalidDataException("Groundwork scheduler-work query row omitted its physical identity.");

    private static long Revision(StoredEntry entry) =>
        entry.Version ?? throw new InvalidDataException(
            "Groundwork scheduler-work row did not return an optimistic revision.");

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static void ValidateIdentity(string workflowExecutionId, string workItemId)
    {
        ValidateWorkflowExecutionId(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
    }

    private static void ValidateWorkflowExecutionId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (workflowExecutionId.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workflowExecutionId),
                workflowExecutionId,
                $"Groundwork scheduler-work workflow execution IDs cannot exceed " +
                $"{ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }

    private static InvalidOperationException TransitionDidNotSettle(
        string transition,
        string workflowExecutionId,
        string? workItemId = null) =>
        new(
            $"Groundwork scheduler-work {transition} for workflow execution '{workflowExecutionId}'" +
            (workItemId is null ? string.Empty : $" and work item '{workItemId}'") +
            $" did not settle after {MaxTransitionAttempts} compare-and-swap attempts.");

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork scheduler-work unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork scheduler-work query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
