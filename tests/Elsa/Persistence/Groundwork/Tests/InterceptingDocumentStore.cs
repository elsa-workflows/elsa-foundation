using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class InterceptingDocumentStore(IDocumentStore inner) : IDocumentStore, IBoundedDocumentStore
{
    public Func<SaveDocumentRequest, Task>? OnBeforeSave { get; set; }
    public Func<DeleteDocumentRequest, Task>? OnBeforeDelete { get; set; }
    public DocumentStoreAccess Access => inner.Access;
    public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

    public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (OnBeforeSave is { } hook)
        {
            OnBeforeSave = null;
            await hook(request);
        }

        return await inner.SaveAsync(request, cancellationToken);
    }

    public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (OnBeforeDelete is { } hook)
        {
            OnBeforeDelete = null;
            await hook(request);
        }

        return await inner.DeleteAsync(request, cancellationToken);
    }

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        inner.LoadAsync(documentKind, id, cancellationToken);

    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.AnyAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner is IBoundedDocumentStore boundedStore
            ? boundedStore.QueryAsync(query, cancellationToken)
            : throw new NotSupportedException();

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner is IBoundedDocumentStore boundedStore
            ? boundedStore.CountAsync(query, cancellationToken)
            : throw new NotSupportedException();

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner is IBoundedDocumentStore boundedStore
            ? boundedStore.FirstOrDefaultAsync(query, cancellationToken)
            : throw new NotSupportedException();

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner is IBoundedDocumentStore boundedStore
            ? boundedStore.AnyAsync(query, cancellationToken)
            : throw new NotSupportedException();

    public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        inner.BeginAsync(scope, cancellationToken);
}
