using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using IsolationLevel = System.Data.IsolationLevel;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// The owner's selected read side. Registration, the actual native writer/observation/retention
// binding, schema model and generated migrations are supplied together by that owner.
internal sealed partial class EfRuntimeHistoryStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    IRuntimeRecoveryContinuationCodec codec, EfSchemaChain chain) : IRuntimeHistoryDetailsStore, IRuntimeHistoryOccurrenceStore
{
    private readonly Mapping mapping = new(chain);
    private RuntimeDbContext Context => context;
    private sealed record Bound(PersistenceAccessContext Access, string Scope);

    internal bool Uses(RuntimeDbContext ownerContext, IPersistenceAccessContextAccessor ownerAccess, EfSchemaChain ownerChain) =>
        ReferenceEquals(context, ownerContext) && ReferenceEquals(access, ownerAccess) && ReferenceEquals(chain, ownerChain);

    public ValueTask<RuntimeHistoryHead?> FindHeadAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var bound = Capture(workflowExecutionId, cancellationToken);
        return Read(bound, workflowExecutionId, "find-head", policy => Head(bound.Scope, workflowExecutionId, policy, cancellationToken), static () => null, cancellationToken);
    }
    public ValueTask<RuntimeHistoryEvent?> FindEventAsync(string workflowExecutionId, string historyEventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId); var bound = Capture(workflowExecutionId, cancellationToken);
        return Read(bound, workflowExecutionId, "find-event", async policy => (await EventDetails(bound.Scope, workflowExecutionId, historyEventId, policy, cancellationToken)).Event, static () => null, cancellationToken);
    }
    public ValueTask<(RuntimeHistoryHead? Head, RuntimeHistoryEvent? Event)> FindEventDetailsAsync(string workflowExecutionId,
        string historyEventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEventId); var bound = Capture(workflowExecutionId, cancellationToken);
        return Read(bound, workflowExecutionId, "find-event-details", policy => EventDetails(bound.Scope, workflowExecutionId, historyEventId, policy, cancellationToken),
            static () => (Head: (RuntimeHistoryHead?)null, Event: (RuntimeHistoryEvent?)null), cancellationToken);
    }
    public ValueTask<RuntimeHistoryBookmark?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId); var bound = Capture(workflowExecutionId, cancellationToken);
        return Read(bound, workflowExecutionId, "find-bookmark", async policy => (await BookmarkDetails(bound.Scope, workflowExecutionId, bookmarkId, policy, cancellationToken)).Bookmark, static () => null, cancellationToken);
    }
    public ValueTask<(RuntimeHistoryHead? Head, RuntimeHistoryBookmark? Bookmark)> FindBookmarkDetailsAsync(string workflowExecutionId,
        string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId); var bound = Capture(workflowExecutionId, cancellationToken);
        return Read(bound, workflowExecutionId, "find-bookmark-details", policy => BookmarkDetails(bound.Scope, workflowExecutionId, bookmarkId, policy, cancellationToken),
            static () => (Head: (RuntimeHistoryHead?)null, Bookmark: (RuntimeHistoryBookmark?)null), cancellationToken);
    }
    public ValueTask<RuntimeHistoryOccurrencePage> ReadOccurrencesAsync(RuntimeHistoryOccurrenceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query); var bound = Capture(query.WorkflowExecutionId, cancellationToken);
        return Read(bound, query.WorkflowExecutionId, "read-occurrences", async policy =>
        {
            var page = await EfRuntimeHistoryOccurrenceReader.ReadAsync(context, bound.Scope, chain, codec, query, cancellationToken);
            CheckPolicy(query.WorkflowExecutionId, page.Head, policy); return page;
        }, static () => new RuntimeHistoryOccurrencePage(null, RuntimeHistoryOccurrenceAvailability.Unavailable,
            "history-unavailable", 0, 0, null, null, [], null, false), cancellationToken);
    }

    private Bound Capture(string run, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run); token.ThrowIfCancellationRequested();
        var captured = access.Current; var scope = captured.RequireScope().Value;
        if (!context.Database.IsRelational() || System.Transactions.Transaction.Current is not null || context.Database.GetEnlistedTransaction() is not null ||
            context.ChangeTracker.HasChanges()) throw new InvalidOperationException("History reads require a clean relational context without ambient or enlisted transactions.");
        var transaction = context.Database.CurrentTransaction;
        if (transaction is not null && transaction.GetDbTransaction().IsolationLevel is not (IsolationLevel.Serializable or IsolationLevel.Snapshot))
            throw new InvalidOperationException("History reads require a Serializable or Snapshot caller transaction.");
        return new(captured, scope);
    }
    private void Recheck(Bound bound)
    {
        if (access.Current != bound.Access)
            throw new InvalidOperationException("The selected persistence access changed during the history read.");
        _ = bound.Access.RequireScope();
    }
    private async ValueTask<T> Read<T>(Bound bound, string run, string operation, Func<RuntimeHistoryCapturePolicy, Task<T>> read, Func<T> missing, CancellationToken token)
    {
        Recheck(bound); var caller = context.Database.CurrentTransaction;
        try
        {
            var owned = caller is null ? await EfRuntimeHistoryReadTransaction.BeginAsync(context, token) : null;
            T result;
            try
            {
                var nativeId = EfWorkflowExecutionStateStore.CreateId(bound.Scope, run);
                var nativeRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == nativeId, token);
                var native = nativeRow is null ? null : EfWorkflowExecutionStateStore.ReadChecked(nativeRow, bound.Scope, run);
                if (native is not null)
                {
                    bound.Access.EnsureTenantScope(native.TenantId);
                    if (native.HistoryCaptureEnabledAtAdmission is { } enabled && enabled != (native.HistoryCapturePolicy is not null))
                        throw Invalid(run, "native history admission provenance is contradictory");
                }
                token.ThrowIfCancellationRequested(); Recheck(bound);
                result = native?.HistoryCapturePolicy is { } policy ? await read(policy) : missing();
                token.ThrowIfCancellationRequested(); Recheck(bound);
                if (caller is not null && !ReferenceEquals(context.Database.CurrentTransaction, caller))
                    throw new InvalidOperationException("The caller transaction changed during the history read.");
                if (owned is not null) await owned.CommitAsync(token);
            }
            finally { if (owned is not null) await owned.DisposeAsync(); }
            token.ThrowIfCancellationRequested(); Recheck(bound); return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (InvalidDataException error) { throw new RuntimeHistoryIntegrityException(run, "retained history data could not be interpreted", error); }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or IOException || EfRelationalExceptionClassifier.IsProviderFailure(error))
        { throw new RuntimeHistoryUnavailableException(run, operation, error); }
    }
    private async Task<RuntimeHistoryHead?> Head(string scope, string run, RuntimeHistoryCapturePolicy policy, CancellationToken token)
    {
        var id = Mapping.Id("head", scope, run);
        var row = await context.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        var head = row is null ? null : mapping.Read(row, scope, run); CheckPolicy(run, head, policy); return head;
    }
    private async Task<(RuntimeHistoryHead? Head, RuntimeHistoryEvent? Event)> EventDetails(string scope, string run, string eventId, RuntimeHistoryCapturePolicy policy, CancellationToken token)
    {
        var head = await Head(scope, run, policy, token); var id = Mapping.Id("event", scope, run, eventId);
        var row = await context.Set<RuntimeHistoryEventEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (row is null) return (head, null);
        var receipt = mapping.ReadReceipt(row, scope, run, head);
        if (receipt.HistoryEventId != eventId) throw Invalid(run, "the exact event receipt disagrees with the requested resource");
        return (head, receipt.IsPurged ? null : mapping.Read(row, scope, run, head));
    }
    private async Task<(RuntimeHistoryHead? Head, RuntimeHistoryBookmark? Bookmark)> BookmarkDetails(string scope, string run, string bookmarkId, RuntimeHistoryCapturePolicy policy, CancellationToken token)
    {
        var head = await Head(scope, run, policy, token); var id = Mapping.Id("bookmark", scope, run, bookmarkId);
        var row = await context.Set<RuntimeHistoryBookmarkEntity>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (row is null) return (head, null);
        var item = mapping.Read(row, scope, run, head);
        if (item.Bookmark.Identity.BookmarkId != bookmarkId) throw Invalid(run, "the exact bookmark disagrees with the requested resource");
        return (head, item);
    }
    private IQueryable<RuntimeHistoryEventEntity> Events(string scope, string run)
    {
        var scopeHash = EfRelationalIdentity.Hash(scope); var runHash = EfRelationalIdentity.Hash(run);
        return context.Set<RuntimeHistoryEventEntity>().AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash);
    }
    private IQueryable<RuntimeHistoryBookmarkEntity> Bookmarks(string scope, string run)
    {
        var scopeHash = EfRelationalIdentity.Hash(scope); var runHash = EfRelationalIdentity.Hash(run);
        return context.Set<RuntimeHistoryBookmarkEntity>().AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == runHash);
    }
    private static void CheckPolicy(string run, RuntimeHistoryHead? head, RuntimeHistoryCapturePolicy policy)
    { if (head is not null && !policy.IsEquivalentTo(head.Policy)) throw Invalid(run, "history disagrees with the native admission policy"); }
    private static RuntimeHistoryIntegrityException Invalid(string run, string reason) => new(run, reason);
}
