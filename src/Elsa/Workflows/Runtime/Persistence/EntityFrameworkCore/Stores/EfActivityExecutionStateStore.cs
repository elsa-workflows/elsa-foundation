using Elsa.Persistence.EntityFramework;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for split activity execution state.</summary>
public sealed class EfActivityExecutionStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IActivityExecutionStateStore
{
    private const string ContinuationPurpose = "elsa-runtime-activity-execution-state-page-v1";

    public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ActivityExecutionEfSupport.Validate(state);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("state", scope, state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId);
        context.ChangeTracker.Clear();
        try
        {
            var row = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "reading", id,
                () => context.ActivityExecutionStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken));
            if (row is null)
                context.ActivityExecutionStates.Add(ToEntity(state, scope, id, ActivityExecutionEfSupport.NewRevision()));
            else
                CopyToEntity(row, state, scope, id, checked(row.Revision + 1), ReadChecked(row, scope));

            await context.SaveChangesAsync(cancellationToken);
            return state;
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
            throw new InvalidOperationException("The activity execution state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsProviderFailure(exception))
        {
            context.ChangeTracker.Clear();
            throw RuntimeActivityExecutionEfPersistenceBoundary.Normalize("saving", state.Execution.ActivityExecutionId, exception);
        }
        catch (InvalidOperationException exception)
        {
            context.ChangeTracker.Clear();
            throw RuntimeActivityExecutionEfPersistenceBoundary.Normalize("saving", state.Execution.ActivityExecutionId, exception);
        }
    }

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var id = ActivityExecutionEfSupport.CreateId("state", scope, workflowExecutionId, activityExecutionId);
        var row = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "reading", id,
            () => context.ActivityExecutionStates.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken));
        return row is null ? null : ReadChecked(row, scope, workflowExecutionId, activityExecutionId);
    }

    public async ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ValidateWorkflow(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        return await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "counting", workflowExecutionId,
            () => context.ActivityExecutionStates.LongCountAsync(row =>
                row.ScopeKeyHash == ActivityExecutionEfSupport.Hash(scope) &&
                row.WorkflowExecutionIdHash == ActivityExecutionEfSupport.Hash(workflowExecutionId), cancellationToken));
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ListAsync(query, query.WorkflowExecutionId, null, cancellationToken);
    }

    public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ListAsync(query, query.WorkflowExecutionId, query.ParentActivityExecutionId, cancellationToken);
    }

    private async ValueTask<RuntimeStorePage<ActivityExecutionState>> ListAsync(
        RuntimeStorePageRequest query,
        string workflowExecutionId,
        string? parentActivityExecutionId,
        CancellationToken cancellationToken)
    {
        ValidateWorkflow(workflowExecutionId);
        if (parentActivityExecutionId is not null)
            ValidateIdentity(workflowExecutionId, parentActivityExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var cursor = DecodeCursor(query.ContinuationToken, scope, workflowExecutionId, parentActivityExecutionId);
        var scopeHash = ActivityExecutionEfSupport.Hash(scope);
        var workflowHash = ActivityExecutionEfSupport.Hash(workflowExecutionId);
        var source = context.ActivityExecutionStates.AsNoTracking()
            .Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash);
        if (parentActivityExecutionId is not null)
        {
            var parentHash = ActivityExecutionEfSupport.Hash(parentActivityExecutionId);
            source = source.Where(row => row.ParentActivityExecutionIdHash == parentHash);
        }
        if (cursor is not null)
            source = source.Where(row => row.ActivityExecutionIdOrderKey.CompareTo(cursor.OrderKey) > 0 ||
                                         row.ActivityExecutionIdOrderKey == cursor.OrderKey && row.Id.CompareTo(cursor.Id) > 0);

        var rows = await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "listing", workflowExecutionId,
            () => source
                .OrderBy(row => row.ActivityExecutionIdOrderKey)
                .ThenBy(row => row.Id)
                .Take(query.Limit + 1)
                .ToArrayAsync(cancellationToken));
        var hasMore = rows.Length > query.Limit;
        if (hasMore)
            rows = rows[..query.Limit];
        var items = rows.Select(row => ReadChecked(row, scope, workflowExecutionId, parentActivityExecutionId, parentActivityExecutionId is not null)).ToArray();
        var next = hasMore && rows.Length > 0
            ? EncodeCursor(new StateCursor(1, ActivityExecutionEfSupport.Hash(scope), workflowExecutionId, parentActivityExecutionId,
                items[^1].Execution.ActivityExecutionId, rows[^1].ActivityExecutionIdOrderKey, rows[^1].Id))
            : null;
        return new RuntimeStorePage<ActivityExecutionState>(query, items, next);
    }

    internal static ActivityExecutionStateEntity ToEntity(ActivityExecutionState state, string scope, string id, long revision)
    {
        var row = new ActivityExecutionStateEntity();
        CopyToEntity(row, state, scope, id, revision, null);
        return row;
    }

    internal static void CopyToEntity(ActivityExecutionStateEntity row, ActivityExecutionState state, string scope, string id, long revision, ActivityExecutionState? previous)
    {
        if (previous is not null && (!StringComparer.Ordinal.Equals(previous.Execution.WorkflowExecutionId, state.Execution.WorkflowExecutionId) ||
                                     !StringComparer.Ordinal.Equals(previous.Execution.ActivityExecutionId, state.Execution.ActivityExecutionId)))
            throw new InvalidDataException("The persisted activity execution state identity changed during replacement.");
        var executionScope = ActivityExecutionEfSupport.EffectiveExecutionScope(state);
        if (executionScope is not null)
            ActivityExecutionEfSupport.ValidateIdentityLength(executionScope, nameof(state.ExecutionScopeId));
        row.Id = id;
        row.ScopeKey = ActivityExecutionEfSupport.Encode(scope);
        row.ScopeKeyHash = ActivityExecutionEfSupport.Hash(scope);
        row.WorkflowExecutionId = ActivityExecutionEfSupport.Encode(state.Execution.WorkflowExecutionId);
        row.WorkflowExecutionIdHash = ActivityExecutionEfSupport.Hash(state.Execution.WorkflowExecutionId);
        row.ActivityExecutionId = ActivityExecutionEfSupport.Encode(state.Execution.ActivityExecutionId);
        row.ActivityExecutionIdHash = ActivityExecutionEfSupport.Hash(state.Execution.ActivityExecutionId);
        row.ActivityExecutionIdOrderKey = ActivityExecutionEfSupport.OrderKey(state.Execution.ActivityExecutionId);
        row.ParentActivityExecutionId = state.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Encode(state.ParentActivityExecutionId);
        row.ParentActivityExecutionIdHash = state.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Hash(state.ParentActivityExecutionId);
        row.ExecutionScopeId = executionScope is null ? null : ActivityExecutionEfSupport.Encode(executionScope);
        row.ExecutionScopeIdHash = executionScope is null ? null : ActivityExecutionEfSupport.Hash(executionScope);
        row.Status = state.Status.ToString();
        row.ExecutionSequence = state.ExecutionSequence;
        row.ScheduledAtUtcTicks = state.ScheduledAt.UtcTicks;
        row.ScheduledAtOffsetMinutes = ActivityExecutionEfSupport.OffsetMinutes(state.ScheduledAt);
        row.ContentJson = RuntimeArtifactJson.Serialize(state);
        row.SchemaVersion = RuntimeActivityExecutionEfModule.SchemaVersion;
        row.Revision = revision;
    }

    internal static ActivityExecutionState ReadChecked(ActivityExecutionStateEntity row, string scope, string? expectedWorkflow = null, string? expectedActivityOrParent = null, bool expectedParent = false)
    {
        try
        {
            return ReadCheckedCore(row, scope, expectedWorkflow, expectedActivityOrParent, expectedParent);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The persisted EF activity execution state row is not valid current JSON or projections.", exception);
        }
    }

    private static ActivityExecutionState ReadCheckedCore(ActivityExecutionStateEntity row, string scope, string? expectedWorkflow, string? expectedActivityOrParent, bool expectedParent)
    {
        ActivityExecutionEfSupport.EnsureRowEnvelope(row.SchemaVersion, row.ScopeKey, scope, row.ScopeKeyHash,
            ActivityExecutionEfSupport.CreateId("state", scope, Decode(row.WorkflowExecutionId), Decode(row.ActivityExecutionId)), row.Id, row.Revision);
        var workflow = Decode(row.WorkflowExecutionId);
        var activity = Decode(row.ActivityExecutionId);
        if (expectedWorkflow is not null && !StringComparer.Ordinal.Equals(workflow, expectedWorkflow))
            throw new InvalidDataException("The persisted activity execution state workflow projection does not match its query.");
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.WorkflowExecutionId, row.WorkflowExecutionIdHash, ActivityExecutionEfSupport.OrderKey(workflow), workflow);
        ActivityExecutionEfSupport.EnsureIdentityProjection(row.ActivityExecutionId, row.ActivityExecutionIdHash, row.ActivityExecutionIdOrderKey, activity);
        var parent = row.ParentActivityExecutionId is null ? null : Decode(row.ParentActivityExecutionId);
        if (expectedParent && expectedActivityOrParent is not null && parent is not null && !StringComparer.Ordinal.Equals(parent, expectedActivityOrParent))
            throw new InvalidDataException("The persisted activity execution state parent projection does not match its query.");
        if (row.ParentActivityExecutionId is null != row.ParentActivityExecutionIdHash is null)
            throw new InvalidDataException("The persisted activity execution state parent projection is incomplete.");
        if (parent is not null && row.ParentActivityExecutionIdHash != ActivityExecutionEfSupport.Hash(parent))
            throw new InvalidDataException("The persisted activity execution state parent projection is corrupt.");
        if (row.ExecutionScopeId is null != row.ExecutionScopeIdHash is null)
            throw new InvalidDataException("The persisted activity execution state execution-scope projection is incomplete.");
        if (row.ExecutionScopeId is not null && ActivityExecutionEfSupport.Decode(row.ExecutionScopeId) is { } executionScope &&
            row.ExecutionScopeIdHash != ActivityExecutionEfSupport.Hash(executionScope))
            throw new InvalidDataException("The persisted activity execution state execution-scope projection is corrupt.");
        var state = RuntimeArtifactJson.Deserialize<ActivityExecutionState>(row.ContentJson);
        ActivityExecutionEfSupport.Validate(state);
        if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflow) ||
            !StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, activity) ||
            state.Status.ToString() != row.Status || state.ExecutionSequence != row.ExecutionSequence ||
            state.ScheduledAt.UtcTicks != row.ScheduledAtUtcTicks || ActivityExecutionEfSupport.OffsetMinutes(state.ScheduledAt) != row.ScheduledAtOffsetMinutes ||
            !StringComparer.Ordinal.Equals(ActivityExecutionEfSupport.EffectiveExecutionScope(state), row.ExecutionScopeId is null ? null : Decode(row.ExecutionScopeId)) ||
            !StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, parent))
            throw new InvalidDataException("The persisted activity execution state projection does not match its content.");
        if (expectedParent && expectedActivityOrParent is not null && parent is not null && !StringComparer.Ordinal.Equals(parent, expectedActivityOrParent))
            throw new InvalidDataException("The persisted activity execution state parent projection does not match its query.");
        return state;
    }

    private ActivityExecutionState ReadChecked(ActivityExecutionStateEntity row, string scope) => ReadChecked(row, scope, null, null);

    private StateCursor? DecodeCursor(string? token, string scope, string workflow, string? parent)
    {
        if (token is null)
            return null;
        try
        {
            var cursor = RuntimeArtifactJson.Deserialize<StateCursor>(Encoding.UTF8.GetString(continuationCodec.Decode(ContinuationPurpose, token)));
            if (cursor.Version != 1 || cursor.ScopeHash != ActivityExecutionEfSupport.Hash(scope) ||
                !StringComparer.Ordinal.Equals(cursor.WorkflowExecutionId, workflow) ||
                !StringComparer.Ordinal.Equals(cursor.ParentActivityExecutionId, parent) ||
                string.IsNullOrWhiteSpace(cursor.ActivityExecutionId) || string.IsNullOrWhiteSpace(cursor.OrderKey) || string.IsNullOrWhiteSpace(cursor.Id) ||
                cursor.OrderKey != ActivityExecutionEfSupport.OrderKey(cursor.ActivityExecutionId))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        {
            throw new ArgumentException("The activity execution state continuation token is invalid or does not belong to this query.", nameof(token), exception);
        }
    }

    private string EncodeCursor(StateCursor cursor) => continuationCodec.Encode(ContinuationPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(cursor)));

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

    private static string Decode(string encoded) => ActivityExecutionEfSupport.Decode(encoded);

    private sealed record StateCursor(int Version, string ScopeHash, string WorkflowExecutionId, string? ParentActivityExecutionId, string ActivityExecutionId, string OrderKey, string Id);
}
