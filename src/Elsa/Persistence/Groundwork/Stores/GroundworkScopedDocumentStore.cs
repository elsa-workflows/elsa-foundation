using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Scoped Elsa adapter that acquires a fresh immutable Groundwork session for each operation and
/// retains a session only for the lifetime of an explicit unit of work.
/// </summary>
public sealed class GroundworkScopedDocumentStore(
    IPersistenceAccessContextAccessor accessContextAccessor,
    IGroundworkStoreSessionFactory sessions) : IDocumentStore, IBoundedDocumentStore
{
    public DocumentStoreAccess Access => GroundworkPersistenceAccessMapper.Map(
        accessContextAccessor.Current,
        PersistenceAccessPolicy.Ordinary);

    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.SaveAsync(request, cancellationToken), cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.LoadAsync(documentKind, id, cancellationToken), cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.DeleteAsync(request, cancellationToken), cancellationToken);

#pragma warning disable GW0004
    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        DocumentStoreQuery query,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.QueryAsync(query, cancellationToken), cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.QueryAsync(query, cancellationToken), cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.FirstOrDefaultAsync(query, cancellationToken), cancellationToken);

    public Task<bool> AnyAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        WithDocumentsAsync(store => store.AnyAsync(query, cancellationToken), cancellationToken);
#pragma warning restore GW0004

    public async Task<IDocumentUnitOfWork> BeginAsync(
        DocumentCommitScope scope,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        try
        {
            var unitOfWork = await session.DocumentStore.BeginAsync(scope, cancellationToken);
            return new SessionDocumentUnitOfWork(unitOfWork, session);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    Task<DocumentQueryResult> IBoundedDocumentStore.QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken) =>
        WithBoundedAsync(store => store.QueryAsync(query, cancellationToken), cancellationToken);

    Task<long> IBoundedDocumentStore.CountAsync(
        DocumentQuery query,
        CancellationToken cancellationToken) =>
        WithBoundedAsync(store => store.CountAsync(query, cancellationToken), cancellationToken);

    Task<DocumentEnvelope?> IBoundedDocumentStore.FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken) =>
        WithBoundedAsync(store => store.FirstOrDefaultAsync(query, cancellationToken), cancellationToken);

    Task<bool> IBoundedDocumentStore.AnyAsync(
        DocumentQuery query,
        CancellationToken cancellationToken) =>
        WithBoundedAsync(store => store.AnyAsync(query, cancellationToken), cancellationToken);

    private async Task<TResult> WithDocumentsAsync<TResult>(
        Func<IDocumentStore, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await using var session = await sessions.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        return await operation(session.DocumentStore);
    }

    private async Task<TResult> WithBoundedAsync<TResult>(
        Func<IBoundedDocumentStore, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await using var session = await sessions.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        return await operation(session.BoundedDocumentStore);
    }

    private sealed class SessionDocumentUnitOfWork(
        IDocumentUnitOfWork unitOfWork,
        GroundworkStoreSession session) : IDocumentUnitOfWork
    {
        private int _disposed;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            unitOfWork.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            unitOfWork.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            unitOfWork.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            unitOfWork.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            unitOfWork.RollbackAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                await unitOfWork.DisposeAsync();
            }
            finally
            {
                await session.DisposeAsync();
            }
        }
    }
}
