using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for inspection-derived activity execution hierarchy evidence.</summary>
public sealed class EfActivityExecutionHierarchyStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IActivityExecutionHierarchyCursorCodec cursorCodec) : IActivityExecutionHierarchyStore
{
    public async ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        ActivityExecutionEfSupport.Validate(record);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("hierarchy", scope, record.WorkflowExecutionId, record.ActivityExecutionId);
        context.ChangeTracker.Clear();
        try
        {
            var row = await context.ActivityExecutionHierarchies.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null)
                context.ActivityExecutionHierarchies.Add(ToEntity(record, scope, id, ActivityExecutionEfSupport.NewRevision()));
            else
                CopyToEntity(row, record, scope, id, checked(row.Revision + 1), ReadChecked(row, scope));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The activity execution hierarchy changed concurrently; retry the operation.", exception);
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The activity execution hierarchy could not be saved; retry the operation if the conflict is transient.", exception);
        }
    }

    public async ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        EnsureTenantScope(query, scope);
        var include = query.Include.Order().ToArray();
        var cursor = query.Cursor is null ? null : DecodeCursor(query.Cursor);
        var effectiveLimit = cursor?.EffectiveLimit ?? Math.Min(query.Limit ?? ActivityExecutionHierarchyPager.DefaultLimit, ActivityExecutionHierarchyPager.MaximumLimit);
        if (cursor is not null)
            ValidateCursorBinding(cursor, query, include, effectiveLimit);
        if (cursor?.ProviderContinuation is not null)
            throw new InvalidDataException("The hierarchy cursor contains an unsupported provider continuation for EF persistence.");

        var workflowHash = ActivityExecutionEfSupport.Hash(query.WorkflowExecutionId);
        var scopeHash = ActivityExecutionEfSupport.Hash(scope);
        var rootRow = await context.ActivityExecutionHierarchies.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == ActivityExecutionEfSupport.CreateId("hierarchy", scope, query.WorkflowExecutionId, query.RootActivityExecutionId), cancellationToken);
        if (rootRow is null)
        {
            if (cursor is not null)
                throw ExpiredCursor("The committed hierarchy snapshot root is no longer available.");
            return null;
        }
        var rootRecord = ReadChecked(rootRow, scope, query.WorkflowExecutionId, query.RootActivityExecutionId);
        if (!rootRow.IsScopeRoot)
            throw new InvalidDataException("The requested activity execution is not a committed hierarchy scope root.");
        var fingerprint = SnapshotFingerprint(rootRecord);
        if (cursor is not null && !StringComparer.Ordinal.Equals(cursor.RootSnapshotFingerprint, fingerprint))
            throw ExpiredCursor("The committed hierarchy snapshot root is no longer available.");

        var watermarkRow = await context.ActivityExecutionHierarchies.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash)
            .OrderByDescending(row => row.ExecutionSequence)
            .ThenByDescending(row => row.ActivityExecutionIdOrderKey)
            .FirstOrDefaultAsync(cancellationToken);
        var currentWatermark = watermarkRow is null ? 0 : ReadChecked(watermarkRow, scope).ExecutionSequence;
        var watermark = cursor?.CommittedThroughSequence ?? currentWatermark;
        if (cursor is not null && currentWatermark < watermark)
            throw ExpiredCursor("The committed hierarchy snapshot is no longer available.");

        var root = ActivityExecutionHierarchyProjector.FindRoot([rootRecord], query.WorkflowExecutionId, query.RootActivityExecutionId);
        if (root is null)
            return null;

        var source = context.ActivityExecutionHierarchies.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash &&
                          row.ExecutionScopeIdHash == ActivityExecutionEfSupport.Hash(query.RootActivityExecutionId) &&
                          !row.IsScopeRoot && row.ExecutionSequence <= watermark);
        if (cursor is not null)
            source = source.Where(row => row.ExecutionSequence > cursor.LastExecutionSequence ||
                                         row.ExecutionSequence == cursor.LastExecutionSequence &&
                                         row.ActivityExecutionIdOrderKey.CompareTo(ActivityExecutionEfSupport.OrderKey(cursor.LastActivityExecutionId)) > 0);
        var rows = await source.OrderBy(row => row.ExecutionSequence).ThenBy(row => row.ActivityExecutionIdOrderKey)
            .Take(effectiveLimit + 1).ToArrayAsync(cancellationToken);
        EnsureProviderPageProgress(rows, effectiveLimit);
        var hasMore = rows.Length > effectiveLimit;
        if (hasMore)
            rows = rows[..effectiveLimit];
        var records = rows.Select(row => ReadChecked(row, scope, query.WorkflowExecutionId, null)).ToArray();
        EnsureLogicalPageProgress(records, cursor, watermark);

        var cache = records.ToDictionary(record => record.ActivityExecutionId, StringComparer.Ordinal);
        cache[rootRecord.ActivityExecutionId] = rootRecord;
        var depths = new Dictionary<string, int>(StringComparer.Ordinal) { [rootRecord.ActivityExecutionId] = 0 };
        var items = new List<ActivityExecutionHierarchyItem>(records.Length);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(await ProjectItemAsync(record, query.RootActivityExecutionId, query.Include, watermark, cache, depths, cancellationToken));
        }
        var last = records.LastOrDefault();
        var next = hasMore && last is not null
            ? cursorCodec.Encode(new ActivityExecutionHierarchyCursorState(query.TenantScope, query.AuthorizationProfile,
                query.WorkflowExecutionId, query.RootActivityExecutionId, include, effectiveLimit, watermark,
                last.ExecutionSequence, last.ActivityExecutionId, RootSnapshotFingerprint: fingerprint))
            : null;
        return new ActivityExecutionHierarchyPage(root, watermark, effectiveLimit, items, next);
    }

    public async ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var row = await context.ActivityExecutionHierarchies.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == ActivityExecutionEfSupport.CreateId("hierarchy", scope, workflowExecutionId, activityExecutionId), cancellationToken);
        if (row is null)
            return null;
        var record = ReadChecked(row, scope, workflowExecutionId, activityExecutionId);
        var watermark = await CurrentWatermark(workflowExecutionId, scope, cancellationToken);
        return await BuildBoundary(record, watermark, scope, cancellationToken);
    }

    public async ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var records = await ListWorkflow(workflowExecutionId, scope, cancellationToken);
        return ActivityExecutionHierarchyProjector.FindAttemptNavigation(records, activityExecutionId);
    }

    private async ValueTask<ActivityExecutionBoundary?> BuildBoundary(ActivityExecutionHierarchyRecord record, long watermark, string scope, CancellationToken cancellationToken)
    {
        if (!record.Item.Metadata.ContainsKey("activity.definitionId"))
            return null;
        var descendants = new List<ActivityExecutionHierarchyRecord>();
        long? lastSequence = null;
        string? lastOrderKey = null;
        string? lastActivityId = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = context.ActivityExecutionHierarchies.AsNoTracking()
                .Where(row => row.ScopeKeyHash == ActivityExecutionEfSupport.Hash(scope) &&
                              row.WorkflowExecutionIdHash == ActivityExecutionEfSupport.Hash(record.WorkflowExecutionId) &&
                              row.ExecutionScopeIdHash == ActivityExecutionEfSupport.Hash(record.ActivityExecutionId) &&
                              !row.IsScopeRoot && row.ExecutionSequence <= watermark);
            if (lastSequence is not null)
                source = source.Where(row => row.ExecutionSequence > lastSequence.Value ||
                                             row.ExecutionSequence == lastSequence.Value &&
                                             row.ActivityExecutionIdOrderKey.CompareTo(lastOrderKey!) > 0);
            var rows = await source.OrderBy(row => row.ExecutionSequence).ThenBy(row => row.ActivityExecutionIdOrderKey)
                .Take(ActivityExecutionHierarchyPager.MaximumLimit).ToArrayAsync(cancellationToken);
            EnsureProviderPageProgress(rows, ActivityExecutionHierarchyPager.MaximumLimit);
            if (rows.Length == 0)
                break;
            foreach (var row in rows)
            {
                var current = ReadChecked(row, scope, record.WorkflowExecutionId, null);
                if (lastSequence is not null && Compare(current, lastSequence.Value, lastActivityId!) <= 0)
                    throw new InvalidDataException("The activity execution hierarchy descendant page did not advance.");
                descendants.Add(current);
                lastSequence = current.ExecutionSequence;
                lastOrderKey = ActivityExecutionEfSupport.OrderKey(current.ActivityExecutionId);
                lastActivityId = current.ActivityExecutionId;
            }
            if (rows.Length < ActivityExecutionHierarchyPager.MaximumLimit)
                break;
        }
        return ActivityExecutionHierarchyProjector.FindBoundary([record, .. descendants], record.ActivityExecutionId);
    }

    private async ValueTask<long> CurrentWatermark(string workflowExecutionId, string scope, CancellationToken cancellationToken)
    {
        var row = await context.ActivityExecutionHierarchies.AsNoTracking()
            .Where(candidate => candidate.ScopeKeyHash == ActivityExecutionEfSupport.Hash(scope) && candidate.WorkflowExecutionIdHash == ActivityExecutionEfSupport.Hash(workflowExecutionId))
            .OrderByDescending(candidate => candidate.ExecutionSequence).ThenByDescending(candidate => candidate.ActivityExecutionIdOrderKey)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? 0 : ReadChecked(row, scope, workflowExecutionId, null).ExecutionSequence;
    }

    private async ValueTask<IReadOnlyCollection<ActivityExecutionHierarchyRecord>> ListWorkflow(string workflowExecutionId, string scope, CancellationToken cancellationToken)
    {
        var records = new List<ActivityExecutionHierarchyRecord>();
        long? lastSequence = null;
        string? lastOrderKey = null;
        string? lastActivityId = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = context.ActivityExecutionHierarchies.AsNoTracking()
                .Where(row => row.ScopeKeyHash == ActivityExecutionEfSupport.Hash(scope) && row.WorkflowExecutionIdHash == ActivityExecutionEfSupport.Hash(workflowExecutionId));
            if (lastSequence is not null)
                source = source.Where(row => row.ExecutionSequence < lastSequence.Value ||
                                             row.ExecutionSequence == lastSequence.Value &&
                                             row.ActivityExecutionIdOrderKey.CompareTo(lastOrderKey!) < 0);
            var rows = await source.OrderByDescending(row => row.ExecutionSequence).ThenByDescending(row => row.ActivityExecutionIdOrderKey)
                .Take(ActivityExecutionHierarchyPager.MaximumLimit).ToArrayAsync(cancellationToken);
            if (rows.Length == 0)
                break;
            foreach (var row in rows)
            {
                var current = ReadChecked(row, scope, workflowExecutionId, null);
                if (lastSequence is not null && Compare(current, lastSequence.Value, lastActivityId!) >= 0)
                    throw new InvalidDataException("The activity execution hierarchy workflow page did not advance.");
                records.Add(current);
                lastSequence = current.ExecutionSequence;
                lastOrderKey = ActivityExecutionEfSupport.OrderKey(current.ActivityExecutionId);
                lastActivityId = current.ActivityExecutionId;
            }
            if (rows.Length < ActivityExecutionHierarchyPager.MaximumLimit)
                break;
        }
        return records;
    }

    private async ValueTask<ActivityExecutionHierarchyItem> ProjectItemAsync(ActivityExecutionHierarchyRecord record, string rootActivityExecutionId, IReadOnlySet<ActivityExecutionHierarchyInclude> include, long watermark,
        IDictionary<string, ActivityExecutionHierarchyRecord> cache, IDictionary<string, int> depths, CancellationToken cancellationToken)
    {
        var depth = await ResolveDepthAsync(record, rootActivityExecutionId, cache, depths, cancellationToken);
        var boundary = await BuildBoundary(record, watermark, ActivityExecutionEfSupport.RequireScope(accessContextAccessor), cancellationToken);
        return record.Item with
        {
            RelativeDepth = depth,
            OutcomeNames = include.Contains(ActivityExecutionHierarchyInclude.Outcomes) ? record.Item.OutcomeNames : [],
            BookmarkCount = include.Contains(ActivityExecutionHierarchyInclude.Bookmarks) ? record.Item.BookmarkCount : 0,
            IncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents) ? record.Item.IncidentCount : 0,
            BlockingIncidentCount = include.Contains(ActivityExecutionHierarchyInclude.Incidents) ? record.Item.BlockingIncidentCount : 0,
            Boundary = boundary
        };
    }

    private async ValueTask<int> ResolveDepthAsync(ActivityExecutionHierarchyRecord record, string rootActivityExecutionId, IDictionary<string, ActivityExecutionHierarchyRecord> cache,
        IDictionary<string, int> depths, CancellationToken cancellationToken)
    {
        if (depths.TryGetValue(record.ActivityExecutionId, out var known))
            return known;
        var path = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = record;
        var baseDepth = 0;
        while (true)
        {
            if (!seen.Add(current.ActivityExecutionId))
                throw new InvalidDataException("Committed activity execution hierarchy contains a parent cycle.");
            path.Add(current.ActivityExecutionId);
            var parentId = current.ParentActivityExecutionId;
            if (parentId is null || StringComparer.Ordinal.Equals(parentId, rootActivityExecutionId))
                break;
            if (depths.TryGetValue(parentId, out baseDepth))
                break;
            if (!cache.TryGetValue(parentId, out var parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parentRow = await context.ActivityExecutionHierarchies.AsNoTracking().SingleOrDefaultAsync(candidate =>
                    candidate.Id == ActivityExecutionEfSupport.CreateId("hierarchy", ActivityExecutionEfSupport.RequireScope(accessContextAccessor), current.WorkflowExecutionId, parentId), cancellationToken);
                if (parentRow is null)
                    break;
                parent = ReadChecked(parentRow, ActivityExecutionEfSupport.RequireScope(accessContextAccessor), current.WorkflowExecutionId, parentId);
                cache[parentId] = parent;
            }
            current = parent;
        }
        for (var index = path.Count - 1; index >= 0; index--)
            depths[path[index]] = ++baseDepth;
        return depths[record.ActivityExecutionId];
    }

    private ActivityExecutionHierarchyEntity ToEntity(ActivityExecutionHierarchyRecord record, string scope, string id, long revision)
    {
        var row = new ActivityExecutionHierarchyEntity();
        CopyToEntity(row, record, scope, id, revision, null);
        return row;
    }

    private static void CopyToEntity(ActivityExecutionHierarchyEntity row, ActivityExecutionHierarchyRecord record, string scope, string id, long revision, ActivityExecutionHierarchyRecord? previous)
    {
        if (previous is not null && (!StringComparer.Ordinal.Equals(previous.WorkflowExecutionId, record.WorkflowExecutionId) || !StringComparer.Ordinal.Equals(previous.ActivityExecutionId, record.ActivityExecutionId)))
            throw new InvalidDataException("The persisted activity execution hierarchy identity changed during replacement.");
        row.Id = id;
        row.ScopeKey = ActivityExecutionEfSupport.Encode(scope);
        row.ScopeKeyHash = ActivityExecutionEfSupport.Hash(scope);
        row.WorkflowExecutionId = ActivityExecutionEfSupport.Encode(record.WorkflowExecutionId);
        row.WorkflowExecutionIdHash = ActivityExecutionEfSupport.Hash(record.WorkflowExecutionId);
        row.ActivityExecutionId = ActivityExecutionEfSupport.Encode(record.ActivityExecutionId);
        row.ActivityExecutionIdHash = ActivityExecutionEfSupport.Hash(record.ActivityExecutionId);
        row.ActivityExecutionIdOrderKey = ActivityExecutionEfSupport.OrderKey(record.ActivityExecutionId);
        row.ExecutionScopeId = ActivityExecutionEfSupport.Encode(record.ExecutionScopeId);
        row.ExecutionScopeIdHash = ActivityExecutionEfSupport.Hash(record.ExecutionScopeId);
        row.ParentActivityExecutionId = record.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Encode(record.ParentActivityExecutionId);
        row.ParentActivityExecutionIdHash = record.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Hash(record.ParentActivityExecutionId);
        row.IsScopeRoot = StringComparer.Ordinal.Equals(record.ExecutionScopeId, record.ActivityExecutionId);
        row.ExecutionSequence = record.ExecutionSequence;
        row.ContentJson = RuntimeArtifactJson.Serialize(record);
        row.SchemaVersion = RuntimeActivityExecutionEfModule.SchemaVersion;
        row.Revision = revision;
    }

    private static ActivityExecutionHierarchyRecord ReadChecked(ActivityExecutionHierarchyEntity row, string scope, string? expectedWorkflow = null, string? expectedActivity = null)
    {
        try
        {
            return ReadCheckedCore(row, scope, expectedWorkflow, expectedActivity);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The persisted EF activity execution hierarchy row is not valid current JSON or projections.", exception);
        }
    }

    private static ActivityExecutionHierarchyRecord ReadCheckedCore(ActivityExecutionHierarchyEntity row, string scope, string? expectedWorkflow, string? expectedActivity)
    {
        var workflow = ActivityExecutionEfSupport.Decode(row.WorkflowExecutionId);
        var activity = ActivityExecutionEfSupport.Decode(row.ActivityExecutionId);
        ActivityExecutionEfSupport.EnsureRowEnvelope(row.SchemaVersion, row.ScopeKey, scope, row.ScopeKeyHash,
            ActivityExecutionEfSupport.CreateId("hierarchy", scope, workflow, activity), row.Id, row.Revision);
        if (expectedWorkflow is not null && !StringComparer.Ordinal.Equals(workflow, expectedWorkflow))
            throw new InvalidDataException("The persisted activity execution hierarchy workflow projection does not match its query.");
        if (expectedActivity is not null && !StringComparer.Ordinal.Equals(activity, expectedActivity))
            throw new InvalidDataException("The persisted activity execution hierarchy identity does not match its query.");
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.WorkflowExecutionId, row.WorkflowExecutionIdHash, ActivityExecutionEfSupport.OrderKey(workflow), workflow);
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.ActivityExecutionId, row.ActivityExecutionIdHash, row.ActivityExecutionIdOrderKey, activity);
        var executionScope = ActivityExecutionEfSupport.Decode(row.ExecutionScopeId);
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.ExecutionScopeId, row.ExecutionScopeIdHash, ActivityExecutionEfSupport.OrderKey(executionScope), executionScope);
        var parent = row.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Decode(row.ParentActivityExecutionId);
        if (row.ParentActivityExecutionId is null != row.ParentActivityExecutionIdHash is null)
            throw new InvalidDataException("The persisted activity execution hierarchy parent projection is incomplete.");
        if (parent is not null && row.ParentActivityExecutionIdHash != ActivityExecutionEfSupport.Hash(parent))
            throw new InvalidDataException("The persisted activity execution hierarchy parent projection is corrupt.");
        var record = RuntimeArtifactJson.Deserialize<ActivityExecutionHierarchyRecord>(row.ContentJson);
        ActivityExecutionEfSupport.Validate(record);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflow) || !StringComparer.Ordinal.Equals(record.ActivityExecutionId, activity) ||
            !StringComparer.Ordinal.Equals(record.ExecutionScopeId, executionScope) || !StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, parent) ||
            record.ExecutionSequence != row.ExecutionSequence || row.IsScopeRoot != StringComparer.Ordinal.Equals(record.ExecutionScopeId, record.ActivityExecutionId))
            throw new InvalidDataException("The persisted activity execution hierarchy projection does not match its content.");
        return record;
    }

    private ActivityExecutionHierarchyCursorState DecodeCursor(string value)
    {
        try
        {
            var cursor = cursorCodec.Decode(value);
            if (cursor.SchemaVersion != 1 || cursor.EffectiveLimit is <= 0 or > ActivityExecutionHierarchyPager.MaximumLimit ||
                cursor.CommittedThroughSequence < 0 || cursor.LastExecutionSequence < 0 || cursor.LastExecutionSequence > cursor.CommittedThroughSequence ||
                string.IsNullOrWhiteSpace(cursor.TenantScope) || string.IsNullOrWhiteSpace(cursor.AuthorizationProfile) ||
                string.IsNullOrWhiteSpace(cursor.WorkflowExecutionId) || string.IsNullOrWhiteSpace(cursor.RootActivityExecutionId) ||
                string.IsNullOrWhiteSpace(cursor.LastActivityExecutionId) || cursor.Include is null ||
                string.IsNullOrWhiteSpace(cursor.RootSnapshotFingerprint))
                throw new FormatException();
            return cursor;
        }
        catch (ActivityExecutionHierarchyCursorException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidDataException or JsonException)
        {
            throw new ActivityExecutionHierarchyCursorException(ActivityExecutionHierarchyCursorFailure.Invalid, "The activity execution hierarchy cursor is invalid.", exception);
        }
    }

    private static void ValidateCursorBinding(ActivityExecutionHierarchyCursorState cursor, ActivityExecutionHierarchyQuery query, ActivityExecutionHierarchyInclude[] include, int effectiveLimit)
    {
        var accessMatches = StringComparer.Ordinal.Equals(cursor.TenantScope, query.TenantScope) && StringComparer.Ordinal.Equals(cursor.AuthorizationProfile, query.AuthorizationProfile);
        var boundaryMatches = StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, query.WorkflowExecutionId) && StringComparer.Ordinal.Equals(cursor.RootActivityExecutionId, query.RootActivityExecutionId);
        var queryMatches = cursor.EffectiveLimit == effectiveLimit && (query.Limit is null || Math.Min(query.Limit.Value, ActivityExecutionHierarchyPager.MaximumLimit) == effectiveLimit) && cursor.Include.SequenceEqual(include);
        if (!accessMatches || !boundaryMatches || !queryMatches)
            throw new ActivityExecutionHierarchyCursorException(ActivityExecutionHierarchyCursorFailure.BindingMismatch,
                "The hierarchy cursor belongs to another query or authorization scope.", metadata: new ActivityExecutionCursorFailureMetadata(
                    "activity-execution-hierarchy", ToBinding(boundaryMatches), ToBinding(queryMatches), ToBinding(accessMatches), true, "restart-from-first-page"));
    }

    private static ActivityExecutionCursorBindingState ToBinding(bool matches) => matches ? ActivityExecutionCursorBindingState.Matched : ActivityExecutionCursorBindingState.Mismatched;

    private static ActivityExecutionHierarchyCursorException ExpiredCursor(string message) => new(ActivityExecutionHierarchyCursorFailure.Expired, message,
        metadata: new ActivityExecutionCursorFailureMetadata("activity-execution-hierarchy", ActivityExecutionCursorBindingState.Matched, ActivityExecutionCursorBindingState.Matched, ActivityExecutionCursorBindingState.Matched, true, "restart-from-first-page"));

    private static string SnapshotFingerprint(ActivityExecutionHierarchyRecord record) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(record))));

    private static void EnsureProviderPageProgress(IReadOnlyCollection<ActivityExecutionHierarchyEntity> rows, int limit)
    {
        // EF deliberately over-fetches one row to discover a continuation. A provider
        // returning more than that is outside the adapter's bounded read contract.
        if (rows.Count > limit + 1)
            throw new InvalidDataException($"The EF activity execution hierarchy provider returned more than {limit} rows for a bounded page.");
    }

    private static void EnsureLogicalPageProgress(IReadOnlyList<ActivityExecutionHierarchyRecord> records, ActivityExecutionHierarchyCursorState? cursor, long watermark)
    {
        (long Sequence, string Id)? previous = cursor is null ? null : (cursor.LastExecutionSequence, cursor.LastActivityExecutionId);
        foreach (var record in records)
        {
            if (record.ExecutionSequence > watermark || previous is { } prior && Compare(record, prior.Sequence, prior.Id) <= 0)
                throw new InvalidDataException("The EF activity execution hierarchy page did not advance within its committed watermark.");
            previous = (record.ExecutionSequence, record.ActivityExecutionId);
        }
    }

    private static int Compare(ActivityExecutionHierarchyRecord record, long sequence, string activityId) => record.ExecutionSequence != sequence ? record.ExecutionSequence.CompareTo(sequence) : StringComparer.Ordinal.Compare(record.ActivityExecutionId, activityId);

    private static void ValidateQuery(ActivityExecutionHierarchyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentity(query.WorkflowExecutionId, query.RootActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.AuthorizationProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantScope);
        ArgumentNullException.ThrowIfNull(query.Include);
        if (query.Limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "The hierarchy page limit must be positive.");
    }

    private static void EnsureTenantScope(ActivityExecutionHierarchyQuery query, string scope)
    {
        if (!StringComparer.Ordinal.Equals(query.TenantScope, $"tenant:{scope}"))
            throw new InvalidOperationException("The requested hierarchy tenant scope does not match the current persistence scope.");
    }

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ActivityExecutionEfSupport.ValidateIdentityLength(workflowExecutionId, nameof(workflowExecutionId));
        ActivityExecutionEfSupport.ValidateIdentityLength(activityExecutionId, nameof(activityExecutionId));
    }
}
