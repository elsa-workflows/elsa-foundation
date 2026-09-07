using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 activity-execution hierarchy store.</summary>
/// <remarks>
/// Hierarchy pages are read from one committed execution-scope watermark. Cursor binding remains part of
/// the public query contract, while the provider supplies bounded continuation and the adapter verifies the
/// signed logical boundary and page ordering.
/// </remarks>
public sealed class GroundworkV2ActivityExecutionHierarchyStore : IActivityExecutionHierarchyStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit unit;
    private readonly IActivityExecutionHierarchyCursorCodec? cursorCodec;

    public GroundworkV2ActivityExecutionHierarchyStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IActivityExecutionHierarchyCursorCodec? cursorCodec = null,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.cursorCodec = cursorCodec;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyDocumentKind, targetName);
    }

    public ValueTask SaveAsync(
        ActivityExecutionHierarchyRecord record,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ActivityExecutionHierarchyStorageConventions.Validate(record);
        cancellationToken.ThrowIfCancellationRequested();

        var session = Open();
        var physicalId = GroundworkV2ActivityExecutionHierarchyStorageConventions.PhysicalId(
            record.WorkflowExecutionId,
            record.ActivityExecutionId);
        var key = GroundworkRuntimeRowStore.Key(physicalId);
        var values = GroundworkV2ActivityExecutionHierarchyStorageConventions.Values(record);
        var result = session.Read(key) is { } existing
            ? UpdateExisting(session, values, existing, record)
            : session.Insert(values, WriteOptions.CreateOnly);
        if (!IsSaved(result.Status))
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution hierarchy save lost a concurrent write; retry the operation.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(
        ActivityExecutionHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        EnsureQueryScope(query);
        cancellationToken.ThrowIfCancellationRequested();
        var codec = cursorCodec ?? throw new InvalidOperationException(
            "A hierarchy cursor codec is required for hierarchy reads.");
        var include = query.Include.Order().ToArray();
        var cursor = query.Cursor is null ? null : codec.Decode(query.Cursor);
        var effectiveLimit = cursor?.EffectiveLimit ??
                             Math.Min(query.Limit ?? ActivityExecutionHierarchyPager.DefaultLimit,
                                 ActivityExecutionHierarchyPager.MaximumLimit);
        if (cursor is not null)
            ValidateCursorBinding(cursor, query, include, effectiveLimit);

        var currentWatermark = ReadCurrentWatermark(query.WorkflowExecutionId, cancellationToken);
        var watermark = cursor?.CommittedThroughSequence ?? currentWatermark;
        if (cursor is not null && currentWatermark < watermark)
            throw ExpiredCursor("The committed hierarchy snapshot is no longer available.");

        cancellationToken.ThrowIfCancellationRequested();
        var rootRecord = LoadByLogicalIdentity(
            query.WorkflowExecutionId,
            query.RootActivityExecutionId);
        if (rootRecord is null)
        {
            if (cursor is not null)
                throw ExpiredCursor("The committed hierarchy snapshot root is no longer available.");
            return ValueTask.FromResult<ActivityExecutionHierarchyPage?>(null);
        }

        var rootSnapshotFingerprint = SnapshotFingerprint(rootRecord);
        if (cursor is not null &&
            !StringComparer.Ordinal.Equals(cursor.RootSnapshotFingerprint, rootSnapshotFingerprint))
        {
            throw ExpiredCursor("The committed hierarchy snapshot root is no longer available.");
        }

        var root = ActivityExecutionHierarchyProjector.FindRoot(
            [rootRecord],
            query.WorkflowExecutionId,
            query.RootActivityExecutionId);
        if (root is null)
            return ValueTask.FromResult<ActivityExecutionHierarchyPage?>(null);

        var result = QueryScopePage(
            query.WorkflowExecutionId,
            query.RootActivityExecutionId,
            watermark,
            effectiveLimit,
            cursor?.ProviderContinuation,
            cancellationToken);
        EnsureProviderPageProgress(result, effectiveLimit);
        if (result.NextContinuationToken is { } nextContinuation &&
            cursor?.ProviderContinuation is { } previousContinuation &&
            StringComparer.Ordinal.Equals(nextContinuation, previousContinuation))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution hierarchy continuation did not advance.");
        }

        var records = result.Rows
            .Select(GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize)
            .ToArray();
        EnsureLogicalPageProgress(records, cursor, watermark);
        var recordCache = records.ToDictionary(record => record.ActivityExecutionId, StringComparer.Ordinal);
        recordCache[rootRecord.ActivityExecutionId] = rootRecord;
        var depthCache = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [rootRecord.ActivityExecutionId] = 0
        };
        var items = new List<ActivityExecutionHierarchyItem>(records.Length);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(ProjectItem(
                record,
                query.RootActivityExecutionId,
                query.Include,
                watermark,
                recordCache,
                depthCache,
                cancellationToken));
        }

        var last = records.LastOrDefault();
        var next = result.NextContinuationToken is not null && last is not null
            ? codec.Encode(new ActivityExecutionHierarchyCursorState(
                query.TenantScope,
                query.AuthorizationProfile,
                query.WorkflowExecutionId,
                query.RootActivityExecutionId,
                include,
                effectiveLimit,
                watermark,
                last.ExecutionSequence,
                last.ActivityExecutionId,
                ProviderContinuation: result.NextContinuationToken,
                RootSnapshotFingerprint: rootSnapshotFingerprint))
            : null;
        return ValueTask.FromResult<ActivityExecutionHierarchyPage?>(
            new(root, watermark, effectiveLimit, items, next));
    }

    public ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var record = LoadByLogicalIdentity(workflowExecutionId, activityExecutionId);
        if (record is null)
            return ValueTask.FromResult<ActivityExecutionBoundary?>(null);

        var watermark = ReadCurrentWatermark(workflowExecutionId, cancellationToken);
        return ValueTask.FromResult(BuildBoundary(record, watermark, cancellationToken));
    }

    public ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var records = ListWorkflow(workflowExecutionId, cancellationToken);
        return ValueTask.FromResult(ActivityExecutionHierarchyProjector.FindAttemptNavigation(
            records,
            activityExecutionId));
    }

    private ActivityExecutionHierarchyItem ProjectItem(
        ActivityExecutionHierarchyRecord record,
        string rootActivityExecutionId,
        IReadOnlySet<ActivityExecutionHierarchyInclude> include,
        long watermark,
        IDictionary<string, ActivityExecutionHierarchyRecord> recordCache,
        IDictionary<string, int> depthCache,
        CancellationToken cancellationToken)
    {
        var depth = ResolveDepth(
            record,
            rootActivityExecutionId,
            recordCache,
            depthCache,
            cancellationToken);
        var boundary = BuildBoundary(record, watermark, cancellationToken);
        return record.Item with
        {
            RelativeDepth = depth,
            OutcomeNames = include.Contains(ActivityExecutionHierarchyInclude.Outcomes)
                ? record.Item.OutcomeNames
                : [],
            BookmarkCount = include.Contains(ActivityExecutionHierarchyInclude.Bookmarks)
                ? record.Item.BookmarkCount
                : 0,
            IncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents)
                ? record.Item.IncidentCount
                : 0,
            BlockingIncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents)
                ? record.Item.BlockingIncidentCount
                : 0,
            Boundary = boundary
        };
    }

    private int ResolveDepth(
        ActivityExecutionHierarchyRecord record,
        string rootActivityExecutionId,
        IDictionary<string, ActivityExecutionHierarchyRecord> recordCache,
        IDictionary<string, int> depthCache,
        CancellationToken cancellationToken)
    {
        if (depthCache.TryGetValue(record.ActivityExecutionId, out var knownDepth))
            return knownDepth;

        var path = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = record;
        var baseDepth = 0;
        while (true)
        {
            if (!seen.Add(current.ActivityExecutionId))
                throw new InvalidDataException(
                    "Committed activity execution hierarchy contains a parent cycle.");
            path.Add(current.ActivityExecutionId);
            var parentId = current.ParentActivityExecutionId;
            if (parentId is null || StringComparer.Ordinal.Equals(parentId, rootActivityExecutionId))
                break;
            if (depthCache.TryGetValue(parentId, out baseDepth))
                break;
            if (!recordCache.TryGetValue(parentId, out var parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                parent = LoadByLogicalIdentity(current.WorkflowExecutionId, parentId);
                if (parent is null)
                    break;
                recordCache[parentId] = parent;
            }

            current = parent;
        }

        for (var index = path.Count - 1; index >= 0; index--)
            depthCache[path[index]] = ++baseDepth;
        return depthCache[record.ActivityExecutionId];
    }

    private ActivityExecutionBoundary? BuildBoundary(
        ActivityExecutionHierarchyRecord record,
        long watermark,
        CancellationToken cancellationToken)
    {
        if (!record.Item.Metadata.ContainsKey("activity.definitionId"))
            return null;

        var descendants = new List<ActivityExecutionHierarchyRecord>();
        string? continuation = null;
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = QueryScopePage(
                record.WorkflowExecutionId,
                record.ActivityExecutionId,
                watermark,
                ActivityExecutionHierarchyPager.MaximumLimit,
                continuation,
                cancellationToken);
            EnsureProviderPageProgress(result, ActivityExecutionHierarchyPager.MaximumLimit);
            descendants.AddRange(result.Rows.Select(
                GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize));
            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution hierarchy continuation repeated or cycled.");
            }

            continuation = result.NextContinuationToken;
        } while (continuation is not null);

        return ActivityExecutionHierarchyProjector.FindBoundary(
            [record, .. descendants],
            record.ActivityExecutionId);
    }

    private long ReadCurrentWatermark(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var table = new TableId(unit.Name);
        var workflow = Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField);
        var sequence = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyExecutionSequenceField);
        var activityId = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyActivityExecutionIdField);
        var result = QueryWithBoundCursor(
            new QueryRequest(
                table,
                Equal(workflow, workflowExecutionId),
                [
                    new OrderTerm(sequence, OrderDirection.Descending, NullOrder.Last),
                    new OrderTerm(activityId, OrderDirection.Descending, NullOrder.Last)
                ],
                Projection.All,
                Paging.Keyset(1)),
            cursor: null);
        EnsureProviderPageProgress(result, 1);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Rows.Count == 0
            ? 0
            : GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize(result.Rows[0])
                .ExecutionSequence;
    }

    private IReadOnlyCollection<ActivityExecutionHierarchyRecord> ListWorkflow(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var records = new List<ActivityExecutionHierarchyRecord>();
        string? continuation = null;
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = QueryWorkflowPage(
                workflowExecutionId,
                ActivityExecutionHierarchyPager.MaximumLimit,
                continuation,
                cancellationToken);
            EnsureProviderPageProgress(result, ActivityExecutionHierarchyPager.MaximumLimit);
            records.AddRange(result.Rows.Select(
                GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize));
            if (result.NextContinuationToken is { } next && !seenContinuations.Add(next))
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution hierarchy continuation repeated or cycled.");
            }

            continuation = result.NextContinuationToken;
        } while (continuation is not null);

        return records;
    }

    private QueryMaterializedResult QueryWorkflowPage(
        string workflowExecutionId,
        int limit,
        string? continuation,
        CancellationToken cancellationToken)
    {
        var table = new TableId(unit.Name);
        var sequence = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyExecutionSequenceField);
        var activityId = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyActivityExecutionIdField);
        var result = QueryWithBoundCursor(
            new QueryRequest(
                table,
                Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField), workflowExecutionId),
                [
                    new OrderTerm(sequence, OrderDirection.Descending, NullOrder.Last),
                    new OrderTerm(activityId, OrderDirection.Descending, NullOrder.Last)
                ],
                Projection.All,
                PagingFor(limit, continuation)),
            continuation);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private QueryMaterializedResult QueryScopePage(
        string workflowExecutionId,
        string executionScopeId,
        long committedThroughSequence,
        int limit,
        string? continuation,
        CancellationToken cancellationToken)
    {
        var table = new TableId(unit.Name);
        var sequence = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyExecutionSequenceField);
        var activityId = Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyActivityExecutionIdField);
        var predicates = new List<Predicate>
        {
            Equal(Column(table, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField), workflowExecutionId),
            Equal(Column(table, ElsaRuntimeV2StorageManifest.ExecutionScopeIdField), executionScopeId),
            Equal(Column(table, ElsaRuntimeV2StorageManifest.ActivityExecutionHierarchyIsScopeRootField), false),
            new Predicate.Range(
                sequence,
                null,
                Bound.Inclusive(QueryConstant.Of(sequence, committedThroughSequence)))
        };
        var result = QueryWithBoundCursor(
            new QueryRequest(
                table,
                Combine(predicates),
                [
                    new OrderTerm(sequence, OrderDirection.Ascending, NullOrder.Last),
                    new OrderTerm(activityId, OrderDirection.Ascending, NullOrder.Last)
                ],
                Projection.All,
                PagingFor(limit, continuation)),
            continuation);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private ActivityExecutionHierarchyRecord? LoadByLogicalIdentity(
        string workflowExecutionId,
        string activityExecutionId)
    {
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(
            GroundworkV2ActivityExecutionHierarchyStorageConventions.PhysicalId(
                workflowExecutionId,
                activityExecutionId)));
        if (entry is null)
            return null;

        var record = GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution hierarchy row identity does not match its requested key.");
        }

        return record;
    }

    private IStorageSession Open()
    {
        var context = RequireScopedContext();

        return sessions.Open(
            unit.Id.Value,
            StorageAccess.Scoped(new StorageScope(context.Scope!.Value)),
            targetName);
    }

    private void EnsureQueryScope(ActivityExecutionHierarchyQuery query)
    {
        var context = RequireScopedContext();
        var expectedTenantScope = $"tenant:{context.Scope!.Value}";
        if (!StringComparer.Ordinal.Equals(query.TenantScope, expectedTenantScope))
        {
            throw new InvalidOperationException(
                "The requested hierarchy tenant scope does not match the current persistence scope.");
        }
    }

    private PersistenceAccessContext RequireScopedContext()
    {
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException(
                          "Groundwork activity-execution hierarchy persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork activity-execution hierarchy requires one explicit persistence scope; " +
                "global and across-scope access are refused.");
        }

        return context;
    }

    private static void EnsureProviderPageProgress(QueryMaterializedResult result, int limit)
    {
        if (result.Rows.Count > limit)
        {
            throw new InvalidDataException(
                $"Groundwork activity-execution hierarchy provider returned {result.Rows.Count} rows for a page limited to {limit}.");
        }

        if (result.Rows.Count == 0 && result.NextContinuationToken is not null)
        {
            throw new InvalidDataException(
                "Groundwork activity-execution hierarchy provider returned a continuation after an empty page.");
        }
    }

    private static void EnsureLogicalPageProgress(
        IReadOnlyList<ActivityExecutionHierarchyRecord> records,
        ActivityExecutionHierarchyCursorState? cursor,
        long watermark)
    {
        // Provider continuation tokens are opaque and intentionally carry no history. The signed logical boundary is
        // the stateless cycle guard: a non-adjacent token cycle must eventually replay or regress a committed tuple.
        (long Sequence, string ActivityId)? previous = cursor is null
            ? null
            : (cursor.LastExecutionSequence, cursor.LastActivityExecutionId);

        foreach (var record in records)
        {
            if (record.ExecutionSequence > watermark)
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution hierarchy provider returned a row beyond the committed watermark.");
            }

            if (previous is { } prior && CompareLogicalPosition(record, prior) <= 0)
            {
                throw new InvalidDataException(
                    "Groundwork activity-execution hierarchy provider returned a row that did not advance past the signed cursor boundary or previous row.");
            }

            previous = (record.ExecutionSequence, record.ActivityExecutionId);
        }
    }

    private static int CompareLogicalPosition(
        ActivityExecutionHierarchyRecord record,
        (long Sequence, string ActivityId) previous) =>
        record.ExecutionSequence != previous.Sequence
            ? record.ExecutionSequence.CompareTo(previous.Sequence)
            : StringComparer.Ordinal.Compare(record.ActivityExecutionId, previous.ActivityId);

    private static string SnapshotFingerprint(ActivityExecutionHierarchyRecord record) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(GroundworkV2RuntimeJson.Serialize(record))));

    private static ActivityExecutionHierarchyCursorException ExpiredCursor(string message) =>
        new(
            ActivityExecutionHierarchyCursorFailure.Expired,
            message,
            metadata: RestartMetadata());

    private static ActivityExecutionCursorFailureMetadata RestartMetadata() =>
        new(
            "activity-execution-hierarchy",
            ActivityExecutionCursorBindingState.Matched,
            ActivityExecutionCursorBindingState.Matched,
            ActivityExecutionCursorBindingState.Matched,
            Recoverable: true,
            RecoveryAction: "restart-from-first-page");

    private static WriteOutcome UpdateExisting(
        IStorageSession session,
        StorageValues values,
        StoredEntry existing,
        ActivityExecutionHierarchyRecord record)
    {
        var previous = GroundworkV2ActivityExecutionHierarchyStorageConventions.Deserialize(existing.Values.Values);
        EnsureIdentity(previous, record.WorkflowExecutionId, record.ActivityExecutionId);
        var version = existing.Version ?? throw new InvalidDataException(
            "Groundwork activity-execution hierarchy row did not return an optimistic revision.");
        if (session is not IConcurrencyStorageSession concurrency)
        {
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic activity-execution hierarchy concurrency.");
        }

        return concurrency.ConditionalUpsert(values, WriteOptions.IfVersion(version));
    }

    private QueryMaterializedResult QueryWithBoundCursor(QueryRequest request, string? cursor)
    {
        try
        {
            return Open().Query(request);
        }
        catch (Exception exception) when (
            cursor is not null &&
            (exception is QueryRenderException { Code: "GW-QUERY-013" } ||
             exception is FormatException ||
             exception.InnerException is FormatException))
        {
            throw new ArgumentException(
                "The activity-execution hierarchy continuation token is invalid or does not belong to this query.",
                "cursor",
                exception);
        }
    }

    private static void ValidateQuery(ActivityExecutionHierarchyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.RootActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.AuthorizationProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantScope);
        ArgumentNullException.ThrowIfNull(query.Include);
        if (query.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "The hierarchy page limit must be positive.");
    }

    private static void ValidateCursorBinding(
        ActivityExecutionHierarchyCursorState cursor,
        ActivityExecutionHierarchyQuery query,
        ActivityExecutionHierarchyInclude[] include,
        int effectiveLimit)
    {
        var accessMatches =
            StringComparer.Ordinal.Equals(cursor.TenantScope, query.TenantScope) &&
            StringComparer.Ordinal.Equals(cursor.AuthorizationProfile, query.AuthorizationProfile);
        var boundaryMatches =
            StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, query.WorkflowExecutionId) &&
            StringComparer.Ordinal.Equals(cursor.RootActivityExecutionId, query.RootActivityExecutionId);
        var queryMatches =
            cursor.EffectiveLimit == effectiveLimit &&
            (query.Limit is null ||
             Math.Min(query.Limit.Value, ActivityExecutionHierarchyPager.MaximumLimit) == effectiveLimit) &&
            cursor.Include is not null &&
            cursor.Include.SequenceEqual(include);
        if (!accessMatches || !boundaryMatches || !queryMatches)
        {
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.BindingMismatch,
                "The hierarchy cursor belongs to another query or authorization scope.",
                metadata: new ActivityExecutionCursorFailureMetadata(
                    "activity-execution-hierarchy",
                    BoundaryBinding: ToBindingState(boundaryMatches),
                    QueryBinding: ToBindingState(queryMatches),
                    AccessBinding: ToBindingState(accessMatches),
                    Recoverable: true,
                    RecoveryAction: "restart-from-first-page"));
        }
    }

    private static ActivityExecutionCursorBindingState ToBindingState(bool matches) =>
        matches
            ? ActivityExecutionCursorBindingState.Matched
            : ActivityExecutionCursorBindingState.Mismatched;

    private ColumnRef Column(TableId table, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork activity-execution hierarchy unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork activity-execution hierarchy query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, object value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Predicate Combine(IReadOnlyList<Predicate> predicates) => predicates.Count switch
    {
        0 => Predicate.AlwaysTrue.Instance,
        1 => predicates[0],
        _ => new Predicate.And(predicates)
    };

    private static Paging PagingFor(int limit, string? continuation) =>
        continuation is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuation, limit);

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        _ = GroundworkV2ActivityExecutionHierarchyStorageConventions.PhysicalId(
            workflowExecutionId,
            activityExecutionId);
    }

    private static void EnsureIdentity(
        ActivityExecutionHierarchyRecord record,
        string workflowExecutionId,
        string activityExecutionId)
    {
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidDataException(
                "Groundwork activity-execution hierarchy row identity does not match its requested key.");
        }
    }

    private static bool IsSaved(WriteOutcomeStatus status) =>
        status is WriteOutcomeStatus.Inserted or
        WriteOutcomeStatus.Updated or
        WriteOutcomeStatus.Upserted or
        WriteOutcomeStatus.Replayed;
}
