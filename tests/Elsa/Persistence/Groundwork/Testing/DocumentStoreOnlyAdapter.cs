using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Hides an inner provider's bounded surface while preserving its complete document-store contract.</summary>
#pragma warning disable GW0004 // This adapter intentionally implements the complete compatibility surface.
public sealed class DocumentStoreOnlyAdapter(IDocumentStore inner) : IDocumentStore
{
    public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

    public DocumentStoreAccess Access => inner.Access;

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        inner.SaveAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        inner.LoadAsync(documentKind, id, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(request, cancellationToken);

    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        DocumentStoreQuery query,
        CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        inner.QueryAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        inner.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        inner.AnyAsync(query, cancellationToken);

    public Task<IDocumentUnitOfWork> BeginAsync(
        DocumentCommitScope scope,
        CancellationToken cancellationToken = default) =>
        inner.BeginAsync(scope, cancellationToken);
}
#pragma warning restore GW0004
