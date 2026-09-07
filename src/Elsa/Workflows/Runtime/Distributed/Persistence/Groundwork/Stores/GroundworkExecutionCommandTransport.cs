using Elsa.Workflows.Runtime.Core.Contracts;
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
            var headKey = Key(DistributedGroundworkStorageManifest.CommandStreamHeadIdField, workflowExecutionId);
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
            var pending = head is null
                ? PendingSummary.For(item)
                : PendingSummary.ForSend(head, item);
            var nextHead = new StreamHead(workflowExecutionId, sequence, pending.Count, pending.VisibleAt, pending.Sequence);
            var headValues = Values(nextHead);
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

    public async ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
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
                Equal(Columns.WorkflowExecutionId, workflowExecutionId),
                [new OrderTerm(Columns.Sequence, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                continuation is null ? Paging.Keyset(maxItems) : Paging.Continuation(continuation, maxItems)),
                sessions.Unit(DistributedGroundworkStorageManifest.CommandTransportUnitId, targetName)
                    .CreateQueryRenderOptions(DistributedGroundworkStorageManifest.CommandByExecutionSequenceIndex));
            foreach (var row in result.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var itemId = StringValue(row, DistributedGroundworkStorageManifest.TransportItemIdField);
                if (!seenItemIds.Add(itemId))
                    continue;
                var candidate = Deserialize<ExecutionCommandTransportItem>(row);
                if (!candidate.IsVisible(now))
                    continue;
                var next = await LeaseItemAsync(workflowExecutionId, itemId, ownerId, now, leaseDuration, cancellationToken);
                if (next is not null)
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

        return leased;
    }

    public async ValueTask<bool> AckAsync(
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
        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var session = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId);
            var entry = session.Read(Key(DistributedGroundworkStorageManifest.TransportItemIdField, transportItemId));
            if (entry is null)
                return false;
            var item = Deserialize<ExecutionCommandTransportItem>(entry.Values.Values);
            if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId) ||
                !StringComparer.Ordinal.Equals(item.LeasedByOwnerId, ownerId) ||
                item.LeaseToken != leaseToken || item.IsVisible(now))
                return false;

            var headSession = Session(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
            var headKey = Key(DistributedGroundworkStorageManifest.CommandStreamHeadIdField, workflowExecutionId);
            var headEntry = headSession.Read(headKey) ?? throw MissingHead(workflowExecutionId);
            var head = ReadHead(headEntry, workflowExecutionId);
            if (head.PendingCount <= 0)
                throw new InvalidOperationException($"Command stream head '{workflowExecutionId}' has no pending commands while acknowledging '{transportItemId}'.");
            var pending = PendingSummary.Load(session, workflowExecutionId, transportItemId, null, head.PendingCount - 1);
            var nextHead = head with { PendingCount = pending.Count, PendingVisibleAt = pending.VisibleAt, PendingSequence = pending.Sequence };
            var headWrite = RowWrite.ConditionalUpsert(
                sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName),
                Values(nextHead),
                WriteOptions.IfVersion(headEntry.Version ?? throw new InvalidOperationException("The command stream head has no optimistic revision.")));
            var itemWrite = RowWrite.Delete(
                sessions.Unit(DistributedGroundworkStorageManifest.CommandTransportUnitId, targetName),
                Key(DistributedGroundworkStorageManifest.TransportItemIdField, transportItemId),
                WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException("The command row has no optimistic revision.")));

            if (await CommitMutationAsync(headWrite, itemWrite, $"acknowledging command '{transportItemId}'", cancellationToken))
                return true;
        }

        return false;
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        cancellationToken.ThrowIfCancellationRequested();
        var unit = sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName);
        var result = Session(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId).Query(new QueryRequest(
            new TableId(DistributedGroundworkStorageManifest.CommandStreamHeadUnitName),
            new Predicate.Range(HeadColumns.PendingVisibleAt, null, Bound.Inclusive(QueryConstant.Of(HeadColumns.PendingVisibleAt, now))),
            [
                new OrderTerm(HeadColumns.PendingVisibleAt, OrderDirection.Ascending, NullOrder.Last),
                new OrderTerm(HeadColumns.WorkflowExecutionId, OrderDirection.Ascending, NullOrder.Last)
            ],
            Projection.ColumnsOnly(HeadColumns.WorkflowExecutionId),
            Paging.Keyset(maxItems)),
            unit.CreateQueryRenderOptions(DistributedGroundworkStorageManifest.PendingCommandHeadByExecutionIndex));
        IReadOnlyCollection<string> ids = result.Rows.Select(row => StringValue(row, DistributedGroundworkStorageManifest.WorkflowExecutionIdField)).ToArray();
        return ValueTask.FromResult(ids);
    }

    public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();
        var session = Session(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
        var unit = sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName);
        var result = session.Query(new QueryRequest(
            new TableId(DistributedGroundworkStorageManifest.CommandStreamHeadUnitName),
            Equal(HeadColumns.WorkflowExecutionId, workflowExecutionId),
            [new OrderTerm(HeadColumns.WorkflowExecutionId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly(HeadColumns.PendingCount),
            Paging.None,
            ResultShape.FirstOrDefault.Instance),
            unit.CreateQueryRenderOptions(DistributedGroundworkStorageManifest.CommandHeadCountByExecutionIndex));
        var count = result.Rows.Count == 0
            ? 0L
            : Int64Value(result.Rows[0], DistributedGroundworkStorageManifest.PendingCountField);
        return ValueTask.FromResult(checked((int)count));
    }

    private async ValueTask<ExecutionCommandTransportItem?> LeaseItemAsync(
        string workflowExecutionId,
        string transportItemId,
        string ownerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = Session(DistributedGroundworkStorageManifest.CommandTransportUnitId);
            var entry = session.Read(Key(DistributedGroundworkStorageManifest.TransportItemIdField, transportItemId));
            if (entry is null)
                return null;
            var item = Deserialize<ExecutionCommandTransportItem>(entry.Values.Values);
            if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId) || !item.IsVisible(now))
                return null;

            var next = item.Lease(ownerId, now + leaseDuration);
            var headSession = Session(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
            var headKey = Key(DistributedGroundworkStorageManifest.CommandStreamHeadIdField, workflowExecutionId);
            var headEntry = headSession.Read(headKey) ?? throw MissingHead(workflowExecutionId);
            var head = ReadHead(headEntry, workflowExecutionId);
            var pending = PendingSummary.Load(session, workflowExecutionId, transportItemId, next, head.PendingCount);
            var nextHead = head with { PendingCount = pending.Count, PendingVisibleAt = pending.VisibleAt, PendingSequence = pending.Sequence };
            var headWrite = RowWrite.ConditionalUpsert(
                sessions.Unit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, targetName),
                Values(nextHead),
                WriteOptions.IfVersion(headEntry.Version ?? throw new InvalidOperationException("The command stream head has no optimistic revision.")));
            var itemWrite = RowWrite.ConditionalUpsert(
                sessions.Unit(DistributedGroundworkStorageManifest.CommandTransportUnitId, targetName),
                Values(next),
                WriteOptions.IfVersion(entry.Version ?? throw new InvalidOperationException("The command row has no optimistic revision.")));

            if (await CommitMutationAsync(headWrite, itemWrite, $"leasing command '{transportItemId}'", cancellationToken))
                return next;
        }

        return null;
    }

    private async ValueTask<bool> CommitMutationAsync(
        RowWrite headWrite,
        RowWrite commandWrite,
        string description,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = sessions.BeginUnitOfWork(
            Access(),
            BatchWriteOptions.Exact,
            [DistributedGroundworkStorageManifest.CommandStreamHeadUnitId, DistributedGroundworkStorageManifest.CommandTransportUnitId],
            targetName);
        unitOfWork.Stage(headWrite);
        unitOfWork.Stage(commandWrite);
        BatchWriteReport report;
        try
        {
            report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
        }
        catch (BatchWriteException exception)
        {
            if (exception.Outcomes.Any(outcome => IsContention(outcome.Outcome.Status)))
                return false;
            throw;
        }

        if (report.IsSuccessful)
            return true;
        if (report.Outcomes.Any(outcome => IsContention(outcome.Outcome.Status)))
            return false;
        throw new InvalidOperationException($"{description} failed in the atomic Groundwork unit of work.");
    }

    private IStorageSession Session(string unitId) => sessions.Open(unitId, Access(), targetName);

    private StorageAccess Access() => StorageAccess.Scoped(new StorageScope(
        accessContextAccessor.Current.Scope?.Value ?? throw new InvalidOperationException("Groundwork distributed stores require a scoped persistence access context.")));

    private static StorageKey Key(string field, string value) => new(new Dictionary<string, object?> { [field] = value });

    private static StorageValues Values(StreamHead head) => new(new Dictionary<string, object?>
    {
        [DistributedGroundworkStorageManifest.CommandStreamHeadIdField] = head.WorkflowExecutionId,
        [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = head.WorkflowExecutionId,
        [DistributedGroundworkStorageManifest.LastSequenceField] = head.LastSequence,
        [DistributedGroundworkStorageManifest.PendingCountField] = head.PendingCount,
        [DistributedGroundworkStorageManifest.PendingVisibleAtField] = head.PendingVisibleAt,
        [DistributedGroundworkStorageManifest.PendingSequenceField] = head.PendingSequence,
        [DistributedGroundworkStorageManifest.PayloadField] = DistributedGroundworkDocuments.Serialize(head)
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

    private static long Int64Value(IReadOnlyDictionary<string, object?> row, string field) => row[field] switch
    {
        long value => value,
        int value => value,
        _ => throw new InvalidOperationException($"The Groundwork row field '{field}' is not an Int64.")
    };

    private static Predicate Equal(ColumnRef column, object value) => new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static bool IsContention(WriteOutcomeStatus? status) => status is WriteOutcomeStatus.ConcurrencyConflict or WriteOutcomeStatus.UniqueViolation or WriteOutcomeStatus.NotFound or WriteOutcomeStatus.Superseded;

    private static StreamHead ReadHead(StoredEntry entry, string workflowExecutionId)
    {
        var head = Deserialize<StreamHead>(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(head.WorkflowExecutionId, workflowExecutionId))
            throw new InvalidOperationException(
                $"Command stream head '{workflowExecutionId}' belongs to workflow execution '{head.WorkflowExecutionId}', not '{workflowExecutionId}'.");
        return head;
    }

    private static InvalidOperationException MissingHead(string workflowExecutionId) =>
        new($"Command stream head '{workflowExecutionId}' is missing for an existing command.");

    private static string ComposeTransportItemId(string workflowExecutionId, long sequence) => $"transport:{Escape(workflowExecutionId)}:{sequence}";
    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");

    private sealed record StreamHead(
        string WorkflowExecutionId,
        long LastSequence,
        long PendingCount = 0,
        DateTimeOffset PendingVisibleAt = default,
        long PendingSequence = 0);

    private readonly record struct PendingSummary(long Count, DateTimeOffset VisibleAt, long Sequence)
    {
        public static PendingSummary For(ExecutionCommandTransportItem item) =>
            new(1, ItemVisibleAt(item), item.Sequence);

        public static PendingSummary ForSend(StreamHead head, ExecutionCommandTransportItem item)
        {
            if (head.PendingCount <= 0)
                return For(item);

            var itemVisibleAt = ItemVisibleAt(item);
            return Compare(itemVisibleAt, item.Sequence, head.PendingVisibleAt, head.PendingSequence) < 0
                ? new(head.PendingCount + 1, itemVisibleAt, item.Sequence)
                : new(head.PendingCount + 1, head.PendingVisibleAt, head.PendingSequence);
        }

        public static PendingSummary Load(
            IStorageSession session,
            string workflowExecutionId,
            string? replacedItemId,
            ExecutionCommandTransportItem? replacement,
            long count)
        {
            var result = session.Query(new QueryRequest(
                new TableId(DistributedGroundworkStorageManifest.CommandTransportUnitName),
                Equal(Columns.WorkflowExecutionId, workflowExecutionId),
                [new OrderTerm(Columns.VisibleAt, OrderDirection.Ascending, NullOrder.Last), new OrderTerm(Columns.Sequence, OrderDirection.Ascending, NullOrder.Last)],
                Projection.All,
                Paging.Keyset(2)));
            var visibleAt = DateTimeOffset.MaxValue;
            var sequence = 0L;
            foreach (var row in result.Rows)
            {
                var itemId = StringValue(row, DistributedGroundworkStorageManifest.TransportItemIdField);
                if (StringComparer.Ordinal.Equals(itemId, replacedItemId))
                    continue;
                var item = Deserialize<ExecutionCommandTransportItem>(row);
                var candidateVisibleAt = ItemVisibleAt(item);
                if (Compare(candidateVisibleAt, item.Sequence, visibleAt, sequence) < 0)
                {
                    visibleAt = candidateVisibleAt;
                    sequence = item.Sequence;
                }
            }

            if (replacement is not null)
            {
                var replacementVisibleAt = ItemVisibleAt(replacement);
                if (Compare(replacementVisibleAt, replacement.Sequence, visibleAt, sequence) < 0)
                {
                    visibleAt = replacementVisibleAt;
                    sequence = replacement.Sequence;
                }
            }

            return new(count, count == 0 ? DateTimeOffset.MaxValue : visibleAt, count == 0 ? 0 : sequence);
        }

        private static DateTimeOffset ItemVisibleAt(ExecutionCommandTransportItem item) =>
            item.LeaseExpiresAt ?? DateTimeOffset.MinValue;

        private static int Compare(DateTimeOffset leftTime, long leftSequence, DateTimeOffset rightTime, long rightSequence)
        {
            var timeComparison = leftTime.CompareTo(rightTime);
            return timeComparison != 0 ? timeComparison : leftSequence.CompareTo(rightSequence);
        }
    }

    private static class Columns
    {
        private static readonly TableId Table = new(DistributedGroundworkStorageManifest.CommandTransportUnitName);
        internal static ColumnRef WorkflowExecutionId { get; } = String(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, false);
        internal static ColumnRef VisibleAt { get; } = new(Table, DistributedGroundworkStorageManifest.VisibleAtField, QueryType.DateTimeOffset, false);
        internal static ColumnRef Sequence { get; } = new(Table, DistributedGroundworkStorageManifest.SequenceField, QueryType.Int64, false);
        internal static ColumnRef EnqueuedAt { get; } = new(Table, DistributedGroundworkStorageManifest.EnqueuedAtField, QueryType.DateTimeOffset, false);
        private static ColumnRef String(string name, bool nullable) => new(Table, name, QueryType.String, nullable, DistributedRuntimeIdentityConstraints.MaximumLength);
    }

    private static class HeadColumns
    {
        private static readonly TableId Table = new(DistributedGroundworkStorageManifest.CommandStreamHeadUnitName);
        internal static ColumnRef WorkflowExecutionId { get; } = String(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, false);
        internal static ColumnRef PendingCount { get; } = new(Table, DistributedGroundworkStorageManifest.PendingCountField, QueryType.Int64, false);
        internal static ColumnRef PendingVisibleAt { get; } = new(Table, DistributedGroundworkStorageManifest.PendingVisibleAtField, QueryType.DateTimeOffset, false);
        private static ColumnRef String(string name, bool nullable) => new(Table, name, QueryType.String, nullable, DistributedRuntimeIdentityConstraints.MaximumLength);
    }
}
