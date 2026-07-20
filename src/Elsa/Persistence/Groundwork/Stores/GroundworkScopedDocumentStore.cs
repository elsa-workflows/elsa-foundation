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
    IGroundworkStoreSessionFactory sessions,
    GroundworkStoreSessionSource? sessionSource = null) : IDocumentStore, IBoundedDocumentStore
{
    public DocumentStoreAccess Access => GroundworkPersistenceAccessMapper.Map(
        accessContextAccessor.Current,
        PersistenceAccessPolicy.Ordinary);

    public TransactionBoundary TransactionBoundary =>
        sessionSource?.AdmittedTransactionBoundary ?? TransactionBoundary.PerOperation;

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
            return new SessionUnitOfWork(unitOfWork, session);
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

}
