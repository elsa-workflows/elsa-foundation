using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core durable-value state store (R14).</summary>
public sealed class EfDurableValueStateStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IDurableValueStateStore
{
    private const string CursorPurpose = "ef-runtime-durable-values-v1";

    public async ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.WorkflowExecutionId, nameof(state.WorkflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(state.DurableValueId, nameof(state.DurableValueId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.DurableValueId);
        context.ChangeTracker.Clear();
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId);
        var valueKey = EfRuntimeOperationalStoreSupport.Encode(state.DurableValueId);
        var valueHash = EfRuntimeOperationalStoreSupport.Hash(state.DurableValueId);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var row = await context.DurableValueStates.SingleOrDefaultAsync(x =>
            x.Id == id &&
            x.ScopeKey == scopeKey && x.ScopeKeyHash == scopeHash &&
            x.WorkflowExecutionId == workflowKey && x.WorkflowExecutionIdHash == workflowHash &&
            x.DurableValueId == valueKey && x.DurableValueIdHash == valueHash,
            cancellationToken);
        if (row is null)
        {
            context.DurableValueStates.Add(ToEntity(state, scope, id, 1));
        }
        else
        {
            var current = Read(row, scope, state.WorkflowExecutionId, state.DurableValueId);
            Copy(row, state, scope, checked(row.Revision + 1));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return state;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The durable-value state changed concurrently; retry the operation.", exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            context.ChangeTracker.Clear();
            throw new InvalidOperationException("The durable-value state changed concurrently; retry the operation.", exception);
        }
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(durableValueId, nameof(durableValueId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await context.DurableValueStates.SingleOrDefaultAsync(x =>
            x.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, durableValueId) &&
            x.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            x.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            x.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            x.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId) &&
            x.DurableValueIdHash == EfRuntimeOperationalStoreSupport.Hash(durableValueId) &&
            x.DurableValueId == EfRuntimeOperationalStoreSupport.Encode(durableValueId),
            cancellationToken);
        if (row is null) return false;
        _ = Read(row, scope, workflowExecutionId, durableValueId);
        context.DurableValueStates.Remove(row);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
    }

    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        EfRuntimeOperationalStoreSupport.ValidateIdentity(workflowExecutionId, nameof(workflowExecutionId));
        EfRuntimeOperationalStoreSupport.ValidateIdentity(durableValueId, nameof(durableValueId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var row = await context.DurableValueStates.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, durableValueId) &&
            x.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope) &&
            x.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
            x.WorkflowExecutionIdHash == EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId) &&
            x.WorkflowExecutionId == EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId) &&
            x.DurableValueIdHash == EfRuntimeOperationalStoreSupport.Hash(durableValueId) &&
            x.DurableValueId == EfRuntimeOperationalStoreSupport.Encode(durableValueId),
            cancellationToken);
        return row is null ? null : Read(row, scope, workflowExecutionId, durableValueId);
    }

    public async ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(DurableValueStatePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        EfRuntimeOperationalStoreSupport.ValidateIdentity(query.WorkflowExecutionId, nameof(query.WorkflowExecutionId));
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var workflow = EfRuntimeOperationalStoreSupport.Encode(query.WorkflowExecutionId);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(query.WorkflowExecutionId);
        var source = context.DurableValueStates.AsNoTracking().Where(x => x.ScopeKeyHash == scopeHash && x.ScopeKey == scopeKey && x.WorkflowExecutionIdHash == workflowHash && x.WorkflowExecutionId == workflow);
        if (query.ContinuationToken is not null)
        {
            var cursor = EfRuntimeOperationalStoreSupport.DecodeCursor(continuationCodec, CursorPurpose, query.ContinuationToken);
            EfRuntimeOperationalStoreSupport.ValidateCursor(query, scope, query.WorkflowExecutionId, cursor);
            source = source.Where(x => string.Compare(x.DurableValueIdOrderKey, EfRuntimeOperationalStoreSupport.Order(cursor.Last)) > 0);
        }
        var rows = await source.OrderBy(x => x.DurableValueIdOrderKey).Take(checked(query.Limit + 1)).ToArrayAsync(cancellationToken);
        var hasNext = rows.Length > query.Limit;
        if (hasNext) rows = rows[..query.Limit];
        var items = rows.Select(x => Read(x, scope, query.WorkflowExecutionId)).ToArray();
        var next = hasNext ? EfRuntimeOperationalStoreSupport.Cursor(continuationCodec, CursorPurpose, scope, query.WorkflowExecutionId, items[^1].DurableValueId) : null;
        return new RuntimeStorePage<DurableValueState>(query, items, next);
    }

    // Internal checkpoint staging reuses the direct store's provider-neutral projection.
    internal static DurableValueStateEntity ToEntity(DurableValueState state, string scope, string id, long revision) => new()
    {
        Id = id, ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId), WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId), WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId),
        DurableValueId = EfRuntimeOperationalStoreSupport.Encode(state.DurableValueId), DurableValueIdHash = EfRuntimeOperationalStoreSupport.Hash(state.DurableValueId), DurableValueIdOrderKey = EfRuntimeOperationalStoreSupport.Order(state.DurableValueId),
        ContentJson = RuntimeArtifactJson.Serialize(state), SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = revision
    };

    internal static void Copy(DurableValueStateEntity row, DurableValueState state, string scope, long revision)
    {
        var replacement = ToEntity(state, scope, row.Id, revision);
        row.ScopeKey = replacement.ScopeKey; row.ScopeKeyHash = replacement.ScopeKeyHash; row.WorkflowExecutionId = replacement.WorkflowExecutionId; row.WorkflowExecutionIdHash = replacement.WorkflowExecutionIdHash; row.WorkflowExecutionIdOrderKey = replacement.WorkflowExecutionIdOrderKey; row.DurableValueId = replacement.DurableValueId; row.DurableValueIdHash = replacement.DurableValueIdHash; row.DurableValueIdOrderKey = replacement.DurableValueIdOrderKey; row.ContentJson = replacement.ContentJson; row.SchemaVersion = replacement.SchemaVersion; row.Revision = revision;
    }

    internal static DurableValueState Read(DurableValueStateEntity row, string scope, string? expectedWorkflow = null, string? expectedValue = null)
    {
        if (row.Revision <= 0 ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope) ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope))
            throw new InvalidDataException("The durable-value row scope or revision projection is corrupt.");
        var state = RuntimeArtifactJson.Deserialize<DurableValueState>(row.ContentJson);
        if ((expectedWorkflow is not null && !StringComparer.Ordinal.Equals(expectedWorkflow, state.WorkflowExecutionId)) ||
            (expectedValue is not null && !StringComparer.Ordinal.Equals(expectedValue, state.DurableValueId)) ||
            row.WorkflowExecutionId != EfRuntimeOperationalStoreSupport.Encode(state.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(state.WorkflowExecutionId) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(state.WorkflowExecutionId) ||
            row.DurableValueId != EfRuntimeOperationalStoreSupport.Encode(state.DurableValueId) ||
            row.DurableValueIdHash != EfRuntimeOperationalStoreSupport.Hash(state.DurableValueId) ||
            row.DurableValueIdOrderKey != EfRuntimeOperationalStoreSupport.Order(state.DurableValueId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.DurableValueId) ||
            row.SchemaVersion != RuntimeOperationalStateEfModule.SchemaVersion)
            throw new InvalidDataException("The durable-value row identity or projection is corrupt.");
        return state;
    }
}
