using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

/// <summary>Durable, scoped command inbox backed solely by Groundwork v2 sessions and queries.</summary>
public sealed class GroundworkExecutionCommandTransport(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IExecutionCommandTransport
{
    private const int MaxCreateAttempts = 16;

    public async ValueTask<ExecutionCommandTransportItem> SendAsync(
        string workflowExecutionId,
        WorkflowExecutionCommandEnvelope envelope,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        accessContextAccessor.Current.EnsureScope(new PersistenceScope(envelope.Partition.Value));

        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var headSession = Session(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
            var headKey = Key(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, workflowExecutionId);
            var currentHead = headSession.Read(headKey);
            var head = currentHead is null ? null : Deserialize<StreamHead>(currentHead.Values.Values);
            if (head is not null && !StringComparer.Ordinal.Equals(head.WorkflowExecutionId, workflowExecutionId))
            {
                throw new InvalidOperationException(
                    $"Command stream head '{workflowExecutionId}' belongs to workflow execution '{head.WorkflowExecutionId}', not '{workflowExecutionId}'.");
            }
            var currentSequence = head?.LastSequence ?? 0;
            var sequence = checked(currentSequence + 1);
            var item = new ExecutionCommandTransportItem(
                ComposeTransportItemId(workflowExecutionId, sequence),
                workflowExecutionId,
                envelope,
                sequence,
                now);
            var headValues = Values(workflowExecutionId, sequence, DistributedGroundworkDocuments.Serialize(new StreamHead(workflowExecutionId, sequence)));
            var itemValues = Values(item);
            var headWrite = currentHead?.Version is { } version
                ? RowWrite.ConditionalUpsert(sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName), headValues, WriteOptions.IfVersion(version))
                : RowWrite.ConditionalUpsert(sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName), headValues, WriteOptions.CreateOnly);
            var itemWrite = RowWrite.Insert(
                sessions.Unit(DistributedGroundworkStorageManifest.CommandTransportUnitId, targetName),
                itemValues,
                WriteOptions.CreateOnly);

            using var unitOfWork = sessions.BeginUnitOfWork(
                Access(),
                BatchWriteOptions.Exact,
                [DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, DistributedGroundworkStorageManifest.CommandTransportUnitId],
                targetName);
            unitOfWork.Stage(headWrite);
            unitOfWork.Stage(itemWrite);
            BatchWriteReport report;
            try
            {
                report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            }
            catch (BatchWriteException exception)
            {
                if (IsContention(exception.Outcomes.FirstOrDefault(outcome => ReferenceEquals(outcome.Write, headWrite))?.Outcome.Status))
                    continue;
                throw;
            }

            if (report.IsSuccessful)
                return item;
            var headOutcome = report.Outcomes.FirstOrDefault(outcome => ReferenceEquals(outcome.Write, headWrite));
            if (IsContention(headOutcome?.Outcome.Status))
                continue;
            throw new InvalidOperationException($"Appending command '{item.TransportItemId}' failed in the atomic Groundwork unit of work.");
        }

        throw new InvalidOperationException($"Sending a command to workflow execution '{workflowExecutionId}' did not settle after {MaxCreateAttempts} stream-head compare-and-swap attempts.");
    }

    public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        cancellationToken.ThrowIfCancellationRequested();
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));

        var session = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId);
        var leased = new List<ExecutionCommandTransportItem>(maxItems);
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Query(new QueryRequest(
                new TableId(DistributedGroundworkStorageManifest.CommandTransportUnitName),
                new Predicate.And([
                    Equal(Columns.WorkflowExecutionId, workflowExecutionId),
                    new Predicate.Range(Columns.VisibleAt, null, Bound.Inclusive(QueryConstant.Of(Columns.VisibleAt, now)))
                ]),
                [new OrderTerm(Columns.Sequence, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                continuation is null ? Paging.Keyset(maxItems) : Paging.Continuation(continuation, maxItems)));
            foreach (var row in result.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var itemId = StringValue(row, DistributedGroundworkStorageManifest.TransportItemIdField);
                if (!seenItemIds.Add(itemId))
                    continue;
                var entry = session.Read(Key(DistributedGroundworkStorageManifest.TransportItemIdField, itemId));
                if (entry is null)
                    continue;
                var item = Deserialize<ExecutionCommandTransportItem>(entry.Values.Values);
                if (!item.IsVisible(now))
                    continue;
                var next = item.Lease(ownerId, now + leaseDuration);
                var options = WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException("The command row has no optimistic revision."));
                var outcome = ConditionalUpsert(session, Values(next), options);
                if (outcome.Succeeded)
                    leased.Add(next);
                if (leased.Count == maxItems)
                    break;
            }

            if (leased.Count == maxItems)
                break;
            continuation = result.NextContinuationToken;
            if (continuation is not null && !seenContinuations.Add(continuation))
                throw new InvalidOperationException("The command transport provider returned a non-advancing lease continuation.");
        }
        while (continuation is not null);

        return ValueTask.FromResult<IReadOnlyList<ExecutionCommandTransportItem>>(leased);
    }

    public ValueTask<bool> AckAsync(
        string workflowExecutionId,
        string transportItemId,
        string ownerId,
        long leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        if (leaseToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(leaseToken));
        cancellationToken.ThrowIfCancellationRequested();
        var session = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId);
        var entry = session.Read(Key(DistributedGroundworkStorageManifest.TransportItemIdField, transportItemId));
        if (entry is null)
            return ValueTask.FromResult(false);
        var item = Deserialize<ExecutionCommandTransportItem>(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(item.LeasedByOwnerId, ownerId) ||
            item.LeaseToken != leaseToken || item.IsVisible(now))
            return ValueTask.FromResult(false);
        var outcome = session.Delete(
            Key(DistributedGroundworkStorageManifest.TransportItemIdField, transportItemId),
            WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException("The command row has no optimistic revision.")));
        return ValueTask.FromResult(outcome.Status == WriteOutcomeStatus.Deleted);
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        cancellationToken.ThrowIfCancellationRequested();
        var result = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId).Query(new QueryRequest(
            new TableId(DistributedGroundworkStorageManifest.CommandTransportUnitName),
            new Predicate.Range(Columns.VisibleAt, null, Bound.Inclusive(QueryConstant.Of(Columns.VisibleAt, now))),
            [new OrderTerm(Columns.WorkflowExecutionId, OrderDirection.Ascending, NullOrder.Last), new OrderTerm(Columns.Sequence, OrderDirection.Descending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(maxItems),
            new LatestPerKey(Columns.WorkflowExecutionId, Columns.EnqueuedAt)));
        IReadOnlyCollection<string> ids = result.Rows.Select(row => StringValue(row, DistributedGroundworkStorageManifest.WorkflowExecutionIdField)).Distinct(StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(ids);
    }

    public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var result = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId).Query(new QueryRequest(
            new TableId(DistributedGroundworkStorageManifest.CommandTransportUnitName),
            Equal(Columns.WorkflowExecutionId, workflowExecutionId),
            [],
            Projection.All,
            Paging.None,
            ResultShape.TotalCount.Instance));
        return ValueTask.FromResult(checked((int)(result.TotalCount ?? result.Rows.Count)));
    }

    private IStorageSession Session(string unitId) => sessions.Open(unitId, Access(), targetName);

    private StorageAccess Access() => StorageAccess.Scoped(new StorageScope(
        accessContextAccessor.Current.Scope?.Value ?? throw new InvalidOperationException("Groundwork distributed stores require a scoped persistence access context.")));

    private static StorageKey Key(string field, string value) => new(new Dictionary<string, object?> { [field] = value });

    private static StorageValues Values(string workflowExecutionId, long sequence, string payload) => new(new Dictionary<string, object?>
    {
        [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = workflowExecutionId,
        [DistributedGroundworkStorageManifest.LastSequenceField] = sequence,
        [DistributedGroundworkStorageManifest.PayloadField] = payload
    });

    private static StorageValues Values(ExecutionCommandTransportItem item) => new(new Dictionary<string, object?>
    {
        [DistributedGroundworkStorageManifest.TransportItemIdField] = item.TransportItemId,
        [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = item.WorkflowExecutionId,
        [DistributedGroundworkStorageManifest.SequenceField] = item.Sequence,
        [DistributedGroundworkStorageManifest.EnqueuedAtField] = item.EnqueuedAt,
        [DistributedGroundworkStorageManifest.VisibleAtField] = item.LeaseExpiresAt ?? DateTimeOffset.MinValue,
        [DistributedGroundworkStorageManifest.LeaseOwnerIdField] = item.LeasedByOwnerId,
        [DistributedGroundworkStorageManifest.LeaseTokenField] = item.LeaseToken ?? 0,
        [DistributedGroundworkStorageManifest.PayloadField] = DistributedGroundworkDocuments.Serialize(item)
    });

    private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values) =>
        DistributedGroundworkDocuments.Deserialize<T>(values, DistributedGroundworkStorageManifest.PayloadField);

    private static string StringValue(IReadOnlyDictionary<string, object?> row, string field) => row[field] switch
    {
        string value => value,
        _ => throw new InvalidOperationException($"The Groundwork row field '{field}' is not a string.")
    };

    private static Predicate Equal(ColumnRef column, object value) => new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static WriteOutcome ConditionalUpsert(IStorageSession session, StorageValues values, WriteOptions options)
    {
        if (session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException("The selected Groundwork provider does not support command compare-and-swap.");
        return concurrency.ConditionalUpsert(values, options);
    }

    private static bool IsContention(WriteOutcomeStatus? status) => status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.Superseded;

    private static string ComposeTransportItemId(string workflowExecutionId, long sequence) => $"transport:{Escape(workflowExecutionId)}:{sequence}";
    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");

    private sealed record StreamHead(string WorkflowExecutionId, long LastSequence);

    private static class Columns
    {
        private static readonly TableId Table = new(DistributedGroundworkStorageManifest.CommandTransportUnitName);
        internal static ColumnRef WorkflowExecutionId { get; } = String(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, false);
        internal static ColumnRef VisibleAt { get; } = new(Table, DistributedGroundworkStorageManifest.VisibleAtField, QueryType.DateTimeOffset, false);
        internal static ColumnRef Sequence { get; } = new(Table, DistributedGroundworkStorageManifest.SequenceField, QueryType.Int64, false);
        internal static ColumnRef EnqueuedAt { get; } = new(Table, DistributedGroundworkStorageManifest.EnqueuedAtField, QueryType.DateTimeOffset, false);
        private static ColumnRef String(string name, bool nullable) => new(Table, name, QueryType.String, nullable, DistributedRuntimeIdentityConstraints.MaximumLength);
    }
}
