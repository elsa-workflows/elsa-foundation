using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Delegates document operations while injecting one deterministic provider exception for a named operation.</summary>
public sealed class ThrowingDocumentStore(
    IDocumentStore inner,
    GroundworkDocumentStoreOperation operation,
    Exception exception) : IDocumentStore
{
    public DocumentStoreAccess Access => inner.Access;
    public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
        operation == GroundworkDocumentStoreOperation.Save
            ? Task.FromException<DocumentStoreWriteResult>(exception)
            : inner.SaveAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        operation == GroundworkDocumentStoreOperation.Load
            ? Task.FromException<DocumentEnvelope?>(exception)
            : inner.LoadAsync(documentKind, id, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
        operation == GroundworkDocumentStoreOperation.Delete
            ? Task.FromException<DocumentStoreWriteResult>(exception)
            : inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // This decorator preserves the complete document-store compatibility surface.
    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

    public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        operation == GroundworkDocumentStoreOperation.Begin
            ? Task.FromException<IDocumentUnitOfWork>(exception)
            : inner.BeginAsync(scope, cancellationToken);
}

public enum GroundworkDocumentStoreOperation
{
    Load,
    Save,
    Delete,
    Begin
}
