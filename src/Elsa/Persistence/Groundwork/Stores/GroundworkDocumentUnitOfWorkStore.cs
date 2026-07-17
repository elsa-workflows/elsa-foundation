using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Shared write/read-by-id adapter for an active document unit of work. Portable query members exist only
/// for IDocumentStore source compatibility and fail closed because transaction orchestration never scans.
/// </summary>
internal sealed class GroundworkDocumentUnitOfWorkStore(
    IDocumentStore owner,
    IDocumentUnitOfWork unitOfWork) : IDocumentStore
{
    public TransactionBoundary TransactionBoundary => owner.TransactionBoundary;
    public DocumentStoreAccess Access => owner.Access;

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        unitOfWork.SaveAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        unitOfWork.LoadAsync(documentKind, id, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        unitOfWork.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // Required IDocumentStore compatibility members; transaction adapters reject scans.
    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        DocumentStoreQuery query,
        CancellationToken cancellationToken = default) =>
        throw NoQueries();

    public Task<DocumentQueryResult> QueryAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        throw NoQueries();

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        throw NoQueries();

    public Task<bool> AnyAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        throw NoQueries();
#pragma warning restore GW0004

    public Task<IDocumentUnitOfWork> BeginAsync(
        DocumentCommitScope scope,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nested document unit-of-work scopes are not supported.");

    private static NotSupportedException NoQueries() =>
        new("Groundwork document unit-of-work adapter does not query documents.");
}
