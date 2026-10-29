using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using Elsa.Persistence.EntityFramework;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for durable activity execution inspection projections and summaries.</summary>
/// <remarks>
/// Summary count and page reads share a short Serializable transaction. A caller-owned Serializable or Snapshot
/// transaction is reused without committing or disposing it; weaker, ambient and externally enlisted transactions
/// are refused. Owned read transactions finish before projection decoding. Lock or serialization failures surface
/// through the persistence boundary without retrying the page or changing its exact-count invariant.
/// </remarks>
public sealed partial class EfActivityExecutionInspectionStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
{
    private const string ContinuationPurpose = "elsa-runtime-activity-execution-inspection-summary-page-v1";

    public async ValueTask<bool> TryAppendObservationAsync(string workflowExecutionId, string activityExecutionId,
        ActivityExecutionObservation observation, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("inspection", scope, workflowExecutionId, activityExecutionId);
        // Bounded CAS retry affects only the observation append, never activity execution or HTTP delivery.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var row = await context.ActivityExecutionInspections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null) return false;
            var previous = ReadChecked(row, scope, workflowExecutionId, activityExecutionId);
            var merged = ActivityExecutionObservation.Merge(previous.Observations, [observation]);
            if (merged.Count == previous.Observations.Count) return true;
            var content = RuntimeArtifactJson.Serialize(previous with { Observations = merged });
            var updated = await context.ActivityExecutionInspections.Where(candidate => candidate.Id == id && candidate.Revision == row.Revision)
                .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.ContentJson, content)
                    .SetProperty(candidate => candidate.Revision, checked(row.Revision + 1)), cancellationToken);
            if (updated == 1) return true;
        }
        throw new DbUpdateConcurrencyException("The activity inspection changed during observation capture.");
    }

    public async ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
    {
        ActivityExecutionEfSupport.Validate(projection);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("inspection", scope, projection.WorkflowExecutionId, projection.ActivityExecutionId);
        context.ChangeTracker.Clear();
        try
        {
            var row = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "reading", id,
                () => context.ActivityExecutionInspections.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken));
            if (row is null)
                context.ActivityExecutionInspections.Add(ToEntity(projection, scope, id, ActivityExecutionEfSupport.NewRevision()));
            else
                CopyToEntity(row, projection, scope, id, checked(row.Revision + 1), ReadChecked(row, scope));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (RuntimeActivityExecutionEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (InvalidDataException)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The activity execution inspection projection changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw RuntimeActivityExecutionEfPersistenceBoundary.Normalize("saving", projection.ActivityExecutionId, exception);
        }
        catch (InvalidOperationException exception)
        {
            context.ChangeTracker.Clear();
            throw RuntimeActivityExecutionEfPersistenceBoundary.Normalize("saving", projection.ActivityExecutionId, exception);
        }
    }

    public async ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("inspection", scope, workflowExecutionId, activityExecutionId);
        var row = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "reading", id,
            () => context.ActivityExecutionInspections.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken));
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, activityExecutionId);
    }

    public async ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(ActivityExecutionInspectionSummaryPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWorkflow(query.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var cursor = DecodeCursor(query.ContinuationToken, scope, query.WorkflowExecutionId);
        var callerTransaction = context.Database.CurrentTransaction;
        if (System.Transactions.Transaction.Current is not null || context.Database.GetEnlistedTransaction() is not null)
            throw new InvalidOperationException("Activity summary paging does not support ambient or externally enlisted transactions.");
        if (callerTransaction is not null && callerTransaction.GetDbTransaction().IsolationLevel is not (IsolationLevel.Serializable or IsolationLevel.Snapshot))
            throw new InvalidOperationException("Activity summary paging requires a Serializable or Snapshot caller transaction.");

        // Count and bounded page must observe the same predicate, including rows inserted or deleted between reads.
        // A single statement is insufficient on providers using lock-based ReadCommitted isolation.
        var (rows, totalCount) = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "listing", query.WorkflowExecutionId, async () =>
        {
            await using var transaction = callerTransaction is null
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var result = await ReadSummaryRowsAsync(query, cursor, scope, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        });
        var hasMore = rows.Length > query.Limit;
        if (hasMore)
            rows = rows[..query.Limit];
        var projections = rows.Select(row => ReadChecked(row, scope, query.WorkflowExecutionId, null)).ToArray();
        var items = projections.Select(ActivityExecutionInspectionSummaryProjection.FromProjection).ToArray();
        var next = hasMore && rows.Length > 0
            ? EncodeCursor(new InspectionCursor(1, ActivityExecutionEfSupport.Hash(scope), query.WorkflowExecutionId,
                items[^1].ExecutionSequence, items[^1].ScheduledAt.UtcTicks, items[^1].ActivityExecutionId, rows[^1].ActivityExecutionIdOrderKey, rows[^1].Id))
            : null;
        return new ActivityExecutionInspectionSummaryPage(query, items, totalCount, next);
    }

    private async Task<(ActivityExecutionInspectionEntity[] Rows, long TotalCount)> ReadSummaryRowsAsync(
        ActivityExecutionInspectionSummaryPageQuery query, InspectionCursor? cursor, string scope, CancellationToken cancellationToken)
    {
        var scopeHash = ActivityExecutionEfSupport.Hash(scope);
        var workflowHash = ActivityExecutionEfSupport.Hash(query.WorkflowExecutionId);
        var source = context.ActivityExecutionInspections.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash);
        var totalCount = await source.LongCountAsync(cancellationToken);
        if (cursor is not null)
        {
            source = source.Where(row => row.SummaryExecutionSequence > cursor.ExecutionSequence ||
                                         row.SummaryExecutionSequence == cursor.ExecutionSequence && (row.SummaryScheduledAtUtcTicks > cursor.ScheduledAtUtcTicks ||
                                         row.SummaryScheduledAtUtcTicks == cursor.ScheduledAtUtcTicks && (row.ActivityExecutionIdOrderKey.CompareTo(cursor.OrderKey) > 0 ||
                                         row.ActivityExecutionIdOrderKey == cursor.OrderKey && row.Id.CompareTo(cursor.Id) > 0)));
        }
        var rows = await source
            .OrderBy(row => row.SummaryExecutionSequence)
            .ThenBy(row => row.SummaryScheduledAtUtcTicks)
            .ThenBy(row => row.ActivityExecutionIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(query.Limit + 1)
            .ToArrayAsync(cancellationToken);
        return (rows, totalCount);
    }

    internal static ActivityExecutionInspectionEntity ToEntity(ActivityExecutionInspectionProjection projection, string scope, string id, long revision)
    {
        var row = new ActivityExecutionInspectionEntity();
        CopyToEntity(row, projection, scope, id, revision, null);
        return row;
    }

    internal static void CopyToEntity(ActivityExecutionInspectionEntity row, ActivityExecutionInspectionProjection projection, string scope, string id, long revision, ActivityExecutionInspectionProjection? previous)
    {
        if (previous is not null && (!StringComparer.Ordinal.Equals(previous.WorkflowExecutionId, projection.WorkflowExecutionId) ||
                                     !StringComparer.Ordinal.Equals(previous.ActivityExecutionId, projection.ActivityExecutionId)))
            throw new InvalidDataException("The persisted activity execution inspection identity changed during replacement.");
        projection = projection with { Observations = ActivityExecutionObservation.Merge(previous?.Observations ?? [], projection.Observations) };
        var executionScope = ActivityExecutionEfSupport.EffectiveExecutionScope(projection);
        if (executionScope is not null)
            ActivityExecutionEfSupport.ValidateIdentityLength(executionScope, nameof(projection.ExecutionScopeId));
        row.Id = id;
        row.ScopeKey = ActivityExecutionEfSupport.Encode(scope);
        row.ScopeKeyHash = ActivityExecutionEfSupport.Hash(scope);
        row.WorkflowExecutionId = ActivityExecutionEfSupport.Encode(projection.WorkflowExecutionId);
        row.WorkflowExecutionIdHash = ActivityExecutionEfSupport.Hash(projection.WorkflowExecutionId);
        row.ActivityExecutionId = ActivityExecutionEfSupport.Encode(projection.ActivityExecutionId);
        row.ActivityExecutionIdHash = ActivityExecutionEfSupport.Hash(projection.ActivityExecutionId);
        row.ActivityExecutionIdOrderKey = ActivityExecutionEfSupport.OrderKey(projection.ActivityExecutionId);
        row.ExecutionScopeId = executionScope is null ? null : ActivityExecutionEfSupport.Encode(executionScope);
        row.ExecutionScopeIdHash = executionScope is null ? null : ActivityExecutionEfSupport.Hash(executionScope);
        row.Status = projection.Status.ToString();
        row.SummaryExecutionSequence = projection.ExecutionSequence;
        row.SummaryScheduledAtUtcTicks = projection.ScheduledAt.UtcTicks;
        row.SummaryScheduledAtOffsetMinutes = EfRuntimeOperationalStoreSupport.OffsetMinutes(projection.ScheduledAt);
        row.ContentJson = RuntimeArtifactJson.Serialize(projection);
        row.SchemaVersion = RuntimeActivityExecutionEfModule.SchemaVersion;
        row.Revision = revision;
    }

    internal static ActivityExecutionInspectionProjection ReadChecked(ActivityExecutionInspectionEntity row, string scope, string? expectedWorkflow = null, string? expectedActivity = null)
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
            throw new InvalidDataException("The persisted EF activity execution inspection row is not valid current JSON or projections.", exception);
        }
    }

    private static ActivityExecutionInspectionProjection ReadCheckedCore(ActivityExecutionInspectionEntity row, string scope, string? expectedWorkflow, string? expectedActivity)
    {
        EfSchemaVersion.EnsureReadable(RuntimeActivityExecutionEfModule.Chain, row.SchemaVersion);
        var workflow = ActivityExecutionEfSupport.Decode(row.WorkflowExecutionId);
        var activity = ActivityExecutionEfSupport.Decode(row.ActivityExecutionId);
        ActivityExecutionEfSupport.EnsureRowEnvelope(row.SchemaVersion, row.ScopeKey, scope, row.ScopeKeyHash,
            ActivityExecutionEfSupport.CreateId("inspection", scope, workflow, activity), row.Id, row.Revision);
        if (expectedWorkflow is not null && !StringComparer.Ordinal.Equals(workflow, expectedWorkflow))
            throw new InvalidDataException("The persisted activity execution inspection workflow projection does not match its query.");
        if (expectedActivity is not null && !StringComparer.Ordinal.Equals(activity, expectedActivity))
            throw new InvalidDataException("The persisted activity execution inspection identity does not match its query.");
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.WorkflowExecutionId, row.WorkflowExecutionIdHash, ActivityExecutionEfSupport.OrderKey(workflow), workflow);
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.ActivityExecutionId, row.ActivityExecutionIdHash, row.ActivityExecutionIdOrderKey, activity);
        if (row.ExecutionScopeId is null != row.ExecutionScopeIdHash is null)
            throw new InvalidDataException("The persisted activity execution inspection execution-scope projection is incomplete.");
        var projection = RuntimeArtifactJson.Deserialize<ActivityExecutionInspectionProjection>(RuntimeActivityExecutionEfModule.Chain.Upcast<ActivityExecutionInspectionEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!);
        ActivityExecutionEfSupport.Validate(projection);
        var executionScope = row.ExecutionScopeId is null ? null : ActivityExecutionEfSupport.Decode(row.ExecutionScopeId);
        if (executionScope is not null && row.ExecutionScopeIdHash != ActivityExecutionEfSupport.Hash(executionScope))
            throw new InvalidDataException("The persisted activity execution inspection execution-scope projection is corrupt.");
        if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, workflow) ||
            !StringComparer.Ordinal.Equals(projection.ActivityExecutionId, activity) ||
            projection.Status.ToString() != row.Status || projection.ExecutionSequence != row.SummaryExecutionSequence ||
            projection.ScheduledAt.UtcTicks != row.SummaryScheduledAtUtcTicks || EfRuntimeOperationalStoreSupport.OffsetMinutes(projection.ScheduledAt) != row.SummaryScheduledAtOffsetMinutes ||
            !StringComparer.Ordinal.Equals(ActivityExecutionEfSupport.EffectiveExecutionScope(projection), executionScope))
            throw new InvalidDataException("The persisted activity execution inspection projection does not match its content.");
        return projection;
    }

    private InspectionCursor? DecodeCursor(string? token, string scope, string workflow)
    {
        if (token is null)
            return null;
        try
        {
            var cursor = RuntimeArtifactJson.Deserialize<InspectionCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(ContinuationPurpose, token)));
            if (cursor.Version != 1 || cursor.ScopeHash != ActivityExecutionEfSupport.Hash(scope) ||
                !StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, workflow) || string.IsNullOrWhiteSpace(cursor.ActivityExecutionId) ||
                string.IsNullOrWhiteSpace(cursor.OrderKey) || string.IsNullOrWhiteSpace(cursor.Id) ||
                cursor.OrderKey != ActivityExecutionEfSupport.OrderKey(cursor.ActivityExecutionId))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        {
            throw new ArgumentException("The activity execution inspection continuation token is invalid or does not belong to this query.", nameof(token), exception);
        }
    }

    private string EncodeCursor(InspectionCursor cursor) => continuationCodec.Encode(ContinuationPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(cursor)));

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ValidateWorkflow(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ActivityExecutionEfSupport.ValidateIdentityLength(activityExecutionId, nameof(activityExecutionId));
    }

    private static void ValidateWorkflow(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ActivityExecutionEfSupport.ValidateIdentityLength(workflowExecutionId, nameof(workflowExecutionId));
    }

    private sealed record InspectionCursor(int Version, string ScopeHash, string WorkflowExecutionId, long ExecutionSequence, long ScheduledAtUtcTicks, string ActivityExecutionId, string OrderKey, string Id);
}
