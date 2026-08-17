using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using System.Globalization;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionHierarchyStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IActivityExecutionHierarchyCursorCodec? cursorCodec = null,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind, boundedStore),
        IActivityExecutionHierarchyStore
{
    public async ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        Validate(record);
        var documentId = DocumentId.Compose(record.WorkflowExecutionId, record.ActivityExecutionId);
        var existing = await LoadByLogicalIdentityAsync(record.WorkflowExecutionId, record.ActivityExecutionId, cancellationToken);
        var result = await SaveDocumentAsync(
            documentId,
            new HierarchyDocument(
                record.WorkflowExecutionId,
                record.ExecutionScopeId,
                StringComparer.Ordinal.Equals(record.ExecutionScopeId, record.ActivityExecutionId),
                record.ActivityExecutionId,
                record.ExecutionSequence,
                record),
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(record.WorkflowExecutionId, record.ActivityExecutionId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected activity execution hierarchy record '{record.ActivityExecutionId}' in workflow execution '{record.WorkflowExecutionId}' with status '{result.Status}'.");
        }
    }

    public async ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        var codec = cursorCodec ??
                    throw new InvalidOperationException("A hierarchy cursor codec is required for hierarchy reads.");
        var include = query.Include;
        var includeOrder = include.Order().ToArray();
        var cursor = query.Cursor is null ? null : codec.Decode(query.Cursor);
        var effectiveLimit = cursor?.EffectiveLimit ??
                             Math.Min(
                                 query.Limit ?? ActivityExecutionHierarchyPager.DefaultLimit,
                                 ActivityExecutionHierarchyPager.MaximumLimit);
        if (cursor is not null)
            ValidateCursorBinding(cursor, query, includeOrder, effectiveLimit);

        var rootRecord = (await LoadByLogicalIdentityAsync(
            query.WorkflowExecutionId,
            query.RootActivityExecutionId,
            cancellationToken))?.Record;
        if (rootRecord is null)
            return null;
        var root = ActivityExecutionHierarchyProjector.FindRoot(
            [rootRecord],
            query.WorkflowExecutionId,
            query.RootActivityExecutionId);
        if (root is null)
            return null;

        var currentWatermark = await ReadCurrentWatermarkAsync(
            query.WorkflowExecutionId,
            cancellationToken);
        var watermark = cursor?.CommittedThroughSequence ?? currentWatermark;
        if (cursor is not null && currentWatermark < watermark)
        {
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.Expired,
                "The committed hierarchy snapshot is no longer available.");
        }

        var result = await QueryScopePageAsync(
            query.WorkflowExecutionId,
            query.RootActivityExecutionId,
            watermark,
            effectiveLimit,
            cursor?.ProviderContinuation,
            cancellationToken);
        var records = result.Documents
            .Select(Serializer.Deserialize<HierarchyDocument>)
            .Select(document => document.Record)
            .ToArray();
        var recordCache = records.ToDictionary(
            record => record.ActivityExecutionId,
            StringComparer.Ordinal);
        recordCache[rootRecord.ActivityExecutionId] = rootRecord;
        var depthCache = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [rootRecord.ActivityExecutionId] = 0
        };
        var items = new List<ActivityExecutionHierarchyItem>(records.Length);
        foreach (var record in records)
        {
            items.Add(await ProjectItemAsync(
                record,
                query.RootActivityExecutionId,
                include,
                watermark,
                recordCache,
                depthCache,
                cancellationToken));
        }

        var last = records.LastOrDefault();
        var next = result.NextContinuation is not null && last is not null
            ? codec.Encode(new ActivityExecutionHierarchyCursorState(
                query.TenantScope,
                query.AuthorizationProfile,
                query.WorkflowExecutionId,
                query.RootActivityExecutionId,
                includeOrder,
                effectiveLimit,
                watermark,
                last.ExecutionSequence,
                last.ActivityExecutionId,
                ProviderContinuation: result.NextContinuation))
            : null;
        return new ActivityExecutionHierarchyPage(root, watermark, effectiveLimit, items, next);
    }

    public async ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        var record = (await LoadByLogicalIdentityAsync(
            workflowExecutionId,
            activityExecutionId,
            cancellationToken))?.Record;
        if (record is null)
            return null;

        var watermark = await ReadCurrentWatermarkAsync(workflowExecutionId, cancellationToken);
        return await BuildBoundaryAsync(record, watermark, cancellationToken);
    }

    public async ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        var records = await ListWorkflowAsync(workflowExecutionId, cancellationToken);
        return ActivityExecutionHierarchyProjector.FindAttemptNavigation(records, activityExecutionId);
    }

    private async ValueTask<long> ReadCurrentWatermarkAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.FindLatestActivityExecutionHierarchyByWorkflowQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId)],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PhysicalSortDirection.Descending),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PhysicalSortDirection.Descending)
                ],
                take: 1),
            cancellationToken);
        return result.Documents.Count == 0
            ? 0
            : Serializer.Deserialize<HierarchyDocument>(result.Documents[0]).ExecutionSequence;
    }

    private async ValueTask<IReadOnlyCollection<ActivityExecutionHierarchyRecord>> ListWorkflowAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var records = new List<ActivityExecutionHierarchyRecord>();
        string? continuation = null;
        do
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    DocumentKind,
                    ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByWorkflowQuery,
                    [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId)],
                    [
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                            PhysicalSortDirection.Descending),
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                            PhysicalSortDirection.Descending)
                    ],
                    take: ActivityExecutionHierarchyPager.MaximumLimit,
                    continuation: continuation),
                cancellationToken);
            records.AddRange(
                result.Documents
                    .Select(Serializer.Deserialize<HierarchyDocument>)
                    .Select(document => document.Record));
            continuation = result.NextContinuation;
        } while (continuation is not null);

        return records;
    }

    private ValueTask<DocumentQueryResult> QueryScopePageAsync(
        string workflowExecutionId,
        string executionScopeId,
        long committedThroughSequence,
        int limit,
        string? continuation,
        CancellationToken cancellationToken) =>
        new(BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId),
                    Equal(ElsaRuntimeStorageManifest.ExecutionScopeIdField, executionScopeId),
                    Equal(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
                        bool.FalseString.ToLowerInvariant()),
                    LessThanOrEqual(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        committedThroughSequence)
                ],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField)
                ],
                take: limit,
                continuation: continuation),
            cancellationToken));

    private async ValueTask<ActivityExecutionHierarchyItem> ProjectItemAsync(
        ActivityExecutionHierarchyRecord record,
        string rootActivityExecutionId,
        IReadOnlySet<ActivityExecutionHierarchyInclude> include,
        long watermark,
        IDictionary<string, ActivityExecutionHierarchyRecord> recordCache,
        IDictionary<string, int> depthCache,
        CancellationToken cancellationToken)
    {
        var depth = await ResolveDepthAsync(
            record,
            rootActivityExecutionId,
            recordCache,
            depthCache,
            cancellationToken);
        var boundary = await BuildBoundaryAsync(record, watermark, cancellationToken);
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

    private async ValueTask<int> ResolveDepthAsync(
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
                throw new InvalidOperationException("Committed activity execution hierarchy contains a parent cycle.");
            path.Add(current.ActivityExecutionId);
            var parentId = current.ParentActivityExecutionId;
            if (parentId is null || StringComparer.Ordinal.Equals(parentId, rootActivityExecutionId))
                break;
            if (depthCache.TryGetValue(parentId, out baseDepth))
                break;
            if (!recordCache.TryGetValue(parentId, out var parent))
            {
                parent = (await LoadByLogicalIdentityAsync(
                    current.WorkflowExecutionId,
                    parentId,
                    cancellationToken))?.Record;
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

    private async ValueTask<ActivityExecutionBoundary?> BuildBoundaryAsync(
        ActivityExecutionHierarchyRecord record,
        long watermark,
        CancellationToken cancellationToken)
    {
        if (!record.Item.Metadata.ContainsKey("activity.definitionId"))
            return null;

        var descendants = new List<ActivityExecutionHierarchyRecord>();
        string? continuation = null;
        do
        {
            var result = await QueryScopePageAsync(
                record.WorkflowExecutionId,
                record.ActivityExecutionId,
                watermark,
                ActivityExecutionHierarchyPager.MaximumLimit,
                continuation,
                cancellationToken);
            descendants.AddRange(
                result.Documents
                    .Select(Serializer.Deserialize<HierarchyDocument>)
                    .Select(document => document.Record));
            continuation = result.NextContinuation;
        } while (continuation is not null);

        return ActivityExecutionHierarchyProjector.FindBoundary(
            [record, .. descendants],
            record.ActivityExecutionId);
    }

    private static void ValidateQuery(ActivityExecutionHierarchyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.RootActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.AuthorizationProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantScope);
        if (query.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "The hierarchy page limit must be positive.");
    }

    private static void ValidateCursorBinding(
        ActivityExecutionHierarchyCursorState cursor,
        ActivityExecutionHierarchyQuery query,
        ActivityExecutionHierarchyInclude[] include,
        int effectiveLimit)
    {
        if (!StringComparer.Ordinal.Equals(cursor.TenantScope, query.TenantScope) ||
            !StringComparer.Ordinal.Equals(cursor.AuthorizationProfile, query.AuthorizationProfile) ||
            !StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, query.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(cursor.RootActivityExecutionId, query.RootActivityExecutionId) ||
            cursor.EffectiveLimit != effectiveLimit ||
            query.Limit is not null &&
            Math.Min(query.Limit.Value, ActivityExecutionHierarchyPager.MaximumLimit) != effectiveLimit ||
            !cursor.Include.SequenceEqual(include))
        {
            throw new ActivityExecutionHierarchyCursorException(
                ActivityExecutionHierarchyCursorFailure.BindingMismatch,
                "The hierarchy cursor belongs to another query or authorization scope.");
        }
    }

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause GreaterThan(string path, long value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(
            path,
            value.ToString(CultureInfo.InvariantCulture)));

    private static DocumentQueryClause LessThanOrEqual(string path, long value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
            path,
            value.ToString(CultureInfo.InvariantCulture)));

    private async ValueTask<LoadedActivityExecutionHierarchyRecord?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, activityExecutionId), cancellationToken);
        if (envelope is null)
            return null;

        var record = Serializer.Deserialize<HierarchyDocument>(envelope).Record;
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(record.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for activity execution hierarchy record '{activityExecutionId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedActivityExecutionHierarchyRecord(record, envelope.Version);
    }

    private static void Validate(ActivityExecutionHierarchyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId) ||
            record.ExecutionSequence != record.Item.ExecutionSequence)
            throw new ArgumentException("Hierarchy record envelope fields must match the item.", nameof(record));
    }

    private sealed record HierarchyDocument(
        string WorkflowExecutionId,
        string ExecutionScopeId,
        bool IsScopeRoot,
        string ActivityExecutionId,
        long ExecutionSequence,
        ActivityExecutionHierarchyRecord Record);

    private sealed record LoadedActivityExecutionHierarchyRecord(ActivityExecutionHierarchyRecord Record, long Version);
}
