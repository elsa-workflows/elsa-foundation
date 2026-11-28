using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.TestScopes;

/// <summary>Process-local scope lifecycle shared by standalone callers and checkpoint-backed storage.</summary>
internal sealed class InMemoryWorkflowTestScopeLifecycleStore(
    object syncRoot,
    SemaphoreSlim writeGate,
    Dictionary<string, WorkflowTestScopeRecord> scopes) : IWorkflowTestScopeStore
{
    public InMemoryWorkflowTestScopeLifecycleStore() : this(new object(), new SemaphoreSlim(1, 1), new(StringComparer.Ordinal))
    {
    }

    public ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            scopes.TryGetValue(scope.ScopeId, out var existing);
            var record = WorkflowTestScopeTransitions.Create(existing, scope, createdAt);
            scopes[scope.ScopeId] = record;
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            scopes.TryGetValue(scopeId, out var record);
            return ValueTask.FromResult(record);
        }
    }

    public async ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            lock (syncRoot)
            {
                scopes.TryGetValue(request.ScopeId, out var existing);
                var result = WorkflowTestScopeTransitions.Close(existing, request);
                if (result.Record is not null)
                    scopes[request.ScopeId] = result.Record;
                return result;
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    public ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            if (!scopes.TryGetValue(scopeId, out var existing))
                throw new InvalidOperationException("The workflow test scope was not found for closure completion.");
            var record = WorkflowTestScopeTransitions.Complete(existing, completedAt);
            scopes[scopeId] = record;
            return ValueTask.FromResult(record);
        }
    }

    public ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            var candidates = scopes.Values
                .Where(record => query.State is { } state
                    ? record.State == state
                    : record.State == WorkflowTestScopeState.Closing ||
                      record.State == WorkflowTestScopeState.Open && record.Scope.IsExpired(query.ObservedAt))
                .Where(record => query.ContinuationToken is null ||
                    StringComparer.Ordinal.Compare(record.Scope.ScopeId, query.ContinuationToken) > 0)
                .OrderBy(record => record.Scope.ScopeId, StringComparer.Ordinal)
                .Take(query.PageSize + 1)
                .ToArray();
            var items = candidates.Take(query.PageSize).ToArray();
            var continuation = candidates.Length > query.PageSize ? items[^1].Scope.ScopeId : null;
            return ValueTask.FromResult(new WorkflowTestScopePage(items, continuation));
        }
    }
}
