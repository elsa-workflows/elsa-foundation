using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Pass-through <see cref="IDocumentStore"/> decorator. Fault-injection stores derive from this and override only the
/// one or two members whose behavior they bend, instead of restating the whole store surface.
/// </summary>
internal abstract class DelegatingDocumentStore(IDocumentStore inner) : IDocumentStore, IBoundedDocumentStore
{
    protected IDocumentStore Inner { get; } = inner;

    public DocumentStoreAccess Access => Inner.Access;
    public TransactionBoundary TransactionBoundary => Inner.TransactionBoundary;

    public virtual Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
        Inner.SaveAsync(request, cancellationToken);

    public virtual Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Inner.LoadAsync(documentKind, id, cancellationToken);

    public virtual Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
        Inner.DeleteAsync(request, cancellationToken);

    public virtual Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        Inner.BeginAsync(scope, cancellationToken);

#pragma warning disable GW0004 // IDocumentStore compatibility surface delegated by the decorator.
    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
        Inner.QueryAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        Inner.QueryAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        Inner.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        Inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Bounded().QueryAsync(query, cancellationToken);

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Bounded().CountAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Bounded().FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Bounded().AnyAsync(query, cancellationToken);

    private IBoundedDocumentStore Bounded() =>
        Inner as IBoundedDocumentStore ?? throw new NotSupportedException();
}

/// <summary>Pass-through <see cref="IDocumentUnitOfWork"/> decorator; see <see cref="DelegatingDocumentStore"/>.</summary>
internal abstract class DelegatingDocumentUnitOfWork(IDocumentUnitOfWork inner) : IDocumentUnitOfWork
{
    protected IDocumentUnitOfWork Inner { get; } = inner;

    public virtual Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
        Inner.SaveAsync(request, cancellationToken);

    public virtual Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
        Inner.DeleteAsync(request, cancellationToken);

    public virtual Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Inner.LoadAsync(documentKind, id, cancellationToken);

    public virtual Task CommitAsync(CancellationToken cancellationToken = default) =>
        Inner.CommitAsync(cancellationToken);

    public virtual Task RollbackAsync(CancellationToken cancellationToken = default) =>
        Inner.RollbackAsync(cancellationToken);

    public virtual ValueTask DisposeAsync() =>
        Inner.DisposeAsync();
}
