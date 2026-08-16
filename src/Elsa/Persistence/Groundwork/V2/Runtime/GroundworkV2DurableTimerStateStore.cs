using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 durable-timer store and fenced claim implementation.</summary>
/// <remarks>
/// Timer rows use the shared injective (workflow execution ID, timer ID) physical identity. Logical timer
/// identity, due time, and claim order remain projected from one validated envelope, so direct operations,
/// claim transitions, and checkpoint cleanup address the same row. All transitions use provider CAS.
/// </remarks>
public sealed class GroundworkV2DurableTimerStateStore : IDurableTimerStore
{
    private const int MaxTransitionAttempts = 16;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2DurableTimerStateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind, targetName);
    }

    public bool SupportsClaimTransitions => true;

    public ValueTask<DurableTimer> SaveAsync(
        DurableTimer timer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var key = GroundworkRuntimeRowStore.Key(PhysicalId(timer.WorkflowExecutionId, timer.TimerId));
        if (session.Read(key) is { } existing)
            return ValueTask.FromResult(ExistingTimer(existing, timer.WorkflowExecutionId, timer.TimerId));

        var result = session.Insert(
            GroundworkV2DurableTimerStorageConventions.Values(timer),
            WriteOptions.CreateOnly);
        if (IsSaved(result.Status))
            return ValueTask.FromResult(timer);
        if (result.Status != WriteOutcomeStatus.ConcurrencyConflict)
        {
            throw new InvalidOperationException(
                "Groundwork durable-timer save failed; retry the operation.");
        }

        var winner = session.Read(key)
            ?? throw new InvalidOperationException(
                "Groundwork durable-timer save lost a concurrent write; retry the operation.");
        return ValueTask.FromResult(ExistingTimer(winner, timer.WorkflowExecutionId, timer.TimerId));
    }

    public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var dueTime = Column(table, ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField);
        var timerId = Column(table, ElsaRuntimeV2StorageManifest.DurableTimerIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Due(dueTime, asOf),
            [
                new OrderTerm(dueTime, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(timerId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.All,
            Paging.Keyset(limit)));
        return ValueTask.FromResult<IReadOnlyCollection<DurableTimer>>(
            result.Rows.Select(Deserialize).Select(envelope => envelope.Timer).ToArray());
    }

    public ValueTask<DurableTimer?> FindAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(PhysicalId(workflowExecutionId, timerId)));
        return ValueTask.FromResult(entry is null
            ? null
            : ExistingTimer(entry, workflowExecutionId, timerId));
    }

    public ValueTask<RuntimeStorePage<DurableTimer>> ListPageAsync(
        DurableTimerPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var timerId = Column(table, ElsaRuntimeV2StorageManifest.DurableTimerIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(workflow, query.WorkflowExecutionId),
            [new OrderTerm(timerId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(query.Limit, query.ContinuationToken)));
        return ValueTask.FromResult(new RuntimeStorePage<DurableTimer>(
            query,
            result.Rows.Select(Deserialize).Select(envelope => envelope.Timer).ToArray(),
            result.NextContinuationToken));
    }

    public ValueTask DeleteAsync(
        string workflowExecutionId,
        string timerId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, timerId);
        cancellationToken.ThrowIfCancellationRequested();
        var key = GroundworkRuntimeRowStore.Key(PhysicalId(workflowExecutionId, timerId));
        var session = Open();
        for (var attempt = 0; attempt < MaxTransitionAttempts; attempt++)
        {
            var existing = session.Read(key);
            if (existing is null)
                return ValueTask.CompletedTask;
            _ = ExistingTimer(existing, workflowExecutionId, timerId);
            var revision = existing.Version ?? throw new InvalidDataException(
                "Groundwork durable-timer row did not return an optimistic revision.");
            var result = session.Delete(key, WriteOptions.IfVersion(revision));
            if (result.Status is WriteOutcomeStatus.Deleted or WriteOutcomeStatus.NotFound)
                return ValueTask.CompletedTask;
            if (result.Status == WriteOutcomeStatus.ConcurrencyConflict)
                continue;
            throw new InvalidOperationException("Groundwork durable-timer delete failed; retry the operation.");
        }

        throw TransitionDidNotSettle("delete", workflowExecutionId, timerId);
    }

    public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
        RuntimeDurableTimerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var table = new TableId(unit.Name);
        var claimOrder = Column(table, ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField);
        var result = session.Query(new QueryRequest(
            table,
            Due(claimOrder, GroundworkV2DurableTimerStorageConventions.ClaimOrderUpperBound(request.Now)),
            [new OrderTerm(claimOrder, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(request.Limit)));
        var claims = new List<RuntimeDurableTimerClaim>(Math.Min(request.Limit, result.Rows.Count));
        foreach (var candidate in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claims.Count == request.Limit)
                break;
            var id = RequiredId(candidate);
            var existing = session.Read(GroundworkRuntimeRowStore.Key(id));
            if (existing is null)
                continue;
            var envelope = Deserialize(existing);
            if (!CanClaim(envelope, request.Now))
                continue;
            var updated = envelope with
            {
                ClaimOrderKey = GroundworkV2DurableTimerStorageConventions.ClaimOrderKey(
                    request.Now.Add(request.VisibilityTimeout),
                    envelope.Timer),
                ClaimOwnerId = request.OwnerId,
                ClaimToken = checked(envelope.ClaimToken + 1),
                ClaimedAt = request.Now,
                VisibleAfter = request.Now.Add(request.VisibilityTimeout)
            };
            var revision = existing.Version ?? throw new InvalidDataException(
                "Groundwork durable-timer row did not return an optimistic revision.");
            var write = ConditionalUpsert(session, GroundworkV2DurableTimerStorageConventions.Values(updated), revision);
            if (write.Status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound)
                continue;
            if (!IsSaved(write.Status))
                throw new InvalidOperationException("Groundwork durable-timer claim failed; retry the operation.");
            claims.Add(NewClaim(updated, write.Version ?? checked(revision + 1)));
        }

        return ValueTask.FromResult<IReadOnlyCollection<RuntimeDurableTimerClaim>>(claims);
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Durable timer visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value, claim))
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Stale);
        var updated = current.Value.Envelope with
        {
            ClaimOrderKey = GroundworkV2DurableTimerStorageConventions.ClaimOrderKey(now.Add(visibilityTimeout), claim.Timer),
            VisibleAfter = now.Add(visibilityTimeout)
        };
        var result = ConditionalUpsert(session, GroundworkV2DurableTimerStorageConventions.Values(updated), claim.Revision);
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound => RuntimeDurableTimerClaimTransitionResult.Stale,
            _ when IsSaved(result.Status) => RuntimeDurableTimerClaimTransitionResult.Applied(
                NewClaim(updated, result.Version ?? checked(claim.Revision + 1))),
            _ => throw new InvalidOperationException("Groundwork durable-timer claim renewal failed; retry the operation.")
        });
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value, claim))
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Stale);
        var result = session.Delete(
            GroundworkRuntimeRowStore.Key(PhysicalId(claim.Timer.WorkflowExecutionId, claim.Timer.TimerId)),
            WriteOptions.IfVersion(claim.Revision));
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.Deleted => RuntimeDurableTimerClaimTransitionResult.Applied(),
            WriteOutcomeStatus.NotFound => RuntimeDurableTimerClaimTransitionResult.AlreadyApplied,
            WriteOutcomeStatus.ConcurrencyConflict => RuntimeDurableTimerClaimTransitionResult.Stale,
            _ => throw new InvalidOperationException("Groundwork durable-timer claim completion failed; retry the operation.")
        });
    }

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();
        var session = Open();
        var current = LoadClaim(session, claim);
        if (current is null)
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.AlreadyApplied);
        if (!Matches(current.Value, claim))
            return ValueTask.FromResult(RuntimeDurableTimerClaimTransitionResult.Stale);
        var updated = current.Value.Envelope with
        {
            ClaimOrderKey = GroundworkV2DurableTimerStorageConventions.ClaimOrderKey(visibleAt, claim.Timer),
            ClaimOwnerId = null,
            ClaimedAt = null,
            VisibleAfter = visibleAt,
            FailureCount = checked(current.Value.Envelope.FailureCount + 1)
        };
        var result = ConditionalUpsert(session, GroundworkV2DurableTimerStorageConventions.Values(updated), claim.Revision);
        return ValueTask.FromResult(result.Status switch
        {
            WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.NotFound => RuntimeDurableTimerClaimTransitionResult.Stale,
            _ when IsSaved(result.Status) => RuntimeDurableTimerClaimTransitionResult.Applied(),
            _ => throw new InvalidOperationException("Groundwork durable-timer claim release failed; retry the operation.")
        });
    }

    private IStorageSession Open()
    {
        var context = accessContextAccessor.Current;
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork durable-timer state requires one explicit persistence scope; global and across-scope access are refused.");
        }

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope.Value)),
            targetName);
    }

    private static GroundworkV2DurableTimerEnvelope Deserialize(StoredEntry entry) =>
        GroundworkV2DurableTimerStorageConventions.Deserialize(entry.Values.Values);

    private static GroundworkV2DurableTimerEnvelope Deserialize(IReadOnlyDictionary<string, object?> values) =>
        GroundworkV2DurableTimerStorageConventions.Deserialize(values);

    private static DurableTimer ExistingTimer(
        StoredEntry entry,
        string workflowExecutionId,
        string timerId)
    {
        var envelope = Deserialize(entry);
        EnsureLogicalIdentity(envelope, workflowExecutionId, timerId);
        return envelope.Timer;
    }

    private (StoredEntry Entry, GroundworkV2DurableTimerEnvelope Envelope)? LoadClaim(
        IStorageSession session,
        RuntimeDurableTimerClaim claim)
    {
        ValidateIdentity(claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        var entry = session.Read(GroundworkRuntimeRowStore.Key(PhysicalId(claim.Timer.WorkflowExecutionId, claim.Timer.TimerId)));
        if (entry is null)
            return null;

        var envelope = Deserialize(entry);
        EnsureLogicalIdentity(envelope, claim.Timer.WorkflowExecutionId, claim.Timer.TimerId);
        return (entry, envelope);
    }

    private static void EnsureLogicalIdentity(
        GroundworkV2DurableTimerEnvelope envelope,
        string workflowExecutionId,
        string timerId)
    {
        if (!StringComparer.Ordinal.Equals(envelope.Timer.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(envelope.Timer.TimerId, timerId))
        {
            throw new InvalidDataException("Groundwork durable-timer row identity does not match its requested key.");
        }
    }

    private static bool Matches(
        (StoredEntry Entry, GroundworkV2DurableTimerEnvelope Envelope) current,
        RuntimeDurableTimerClaim claim) =>
        current.Entry.Version == claim.Revision &&
        current.Envelope.ClaimToken == claim.FencingToken &&
        StringComparer.Ordinal.Equals(current.Envelope.ClaimOwnerId, claim.OwnerId);

    private static RuntimeDurableTimerClaim NewClaim(
        GroundworkV2DurableTimerEnvelope envelope,
        long revision) =>
        new(
            envelope.Timer,
            envelope.ClaimOwnerId!,
            envelope.ClaimToken,
            revision,
            envelope.ClaimedAt!.Value,
            envelope.VisibleAfter!.Value,
            envelope.FailureCount);

    private static bool CanClaim(GroundworkV2DurableTimerEnvelope envelope, DateTimeOffset now) =>
        envelope.Timer.DueTime <= now &&
        (envelope.VisibleAfter is null || envelope.VisibleAfter <= now);

    private static WriteOutcome ConditionalUpsert(
        IStorageSession session,
        StorageValues values,
        long revision)
    {
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic durable-timer concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(revision));
    }

    private static string PhysicalId(string workflowExecutionId, string timerId) =>
        GroundworkV2DurableTimerStorageConventions.PhysicalId(workflowExecutionId, timerId);

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted or WriteOutcomeStatus.Replayed;

    private static void ValidateIdentity(string workflowExecutionId, string timerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        _ = PhysicalId(workflowExecutionId, timerId);
    }

    private static string RequiredId(IReadOnlyDictionary<string, object?> values)
    {
        if (values.TryGetValue(ElsaRuntimeV2StorageManifest.IdField, out var value) && value is string id && !string.IsNullOrWhiteSpace(id))
            return id;
        throw new InvalidDataException("Groundwork durable-timer query row did not contain a valid physical ID.");
    }

    private static Predicate Equal(ColumnRef column, object value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Due(ColumnRef column, object value) =>
        new Predicate.Range(column, null, Bound.Inclusive(QueryConstant.Of(column, value)));

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork durable-timer unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork durable-timer query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);

    private static InvalidOperationException TransitionDidNotSettle(
        string transition,
        string workflowExecutionId,
        string timerId) =>
        new(
            $"Groundwork durable-timer {transition} for workflow execution '{workflowExecutionId}' and timer '{timerId}' " +
            $"did not settle after {MaxTransitionAttempts} compare-and-swap attempts.");
}
