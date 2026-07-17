using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Testing;

public enum GroundworkDocumentStoreEventKind
{
    BoundedQuery,
    DirectDelete,
    BeginUnitOfWork,
    CommitUnderlying
}

public sealed record GroundworkDocumentStoreEvent(
    GroundworkDocumentStoreEventKind Kind,
    string? DocumentKind = null,
    string? Identity = null,
    long? ExpectedVersion = null);

/// <summary>
/// Provider-neutral decorator used to inject deterministic Identity commit failures into real physical
/// document stores while preserving the provider's own transaction and bounded-query implementations.
/// </summary>
public sealed class GroundworkFailureInjectingDocumentStore : IDocumentStore, IBoundedDocumentStore
{
    public static GroundworkFailureWindow BeforeUnderlyingCommit { get; } =
        new("identity-before-underlying-commit");

    public static GroundworkFailureWindow AfterUnderlyingCommit { get; } =
        new("identity-after-underlying-commit");

    private readonly IDocumentStore _documents;
    private readonly IBoundedDocumentStore _boundedDocuments;
    private readonly GroundworkFailureController _failures;
    private readonly ConcurrentQueue<SaveDocumentRequest> _stagedSaves = new();
    private readonly ConcurrentQueue<DeleteDocumentRequest> _stagedDeletes = new();
    private readonly ConcurrentQueue<DocumentQuery> _boundedQueries = new();
    private readonly ConcurrentQueue<GroundworkDocumentStoreEvent> _events = new();

    public GroundworkFailureInjectingDocumentStore(
        IDocumentStore documents,
        IBoundedDocumentStore boundedDocuments,
        DocumentStoreAccess boundedStoreAccess,
        GroundworkFailureController failures)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _boundedDocuments = boundedDocuments ?? throw new ArgumentNullException(nameof(boundedDocuments));
        ArgumentNullException.ThrowIfNull(boundedStoreAccess);
        _failures = failures ?? throw new ArgumentNullException(nameof(failures));
        EnsureSameAccessScope(documents.Access, boundedStoreAccess);
        if (boundedDocuments is IDocumentStore boundedDocumentStore)
            EnsureSameAccessScope(documents.Access, boundedDocumentStore.Access);
    }

    public DocumentStoreAccess Access => _documents.Access;
    public TransactionBoundary TransactionBoundary => _documents.TransactionBoundary;
    public IReadOnlyList<SaveDocumentRequest> StagedSaves => _stagedSaves.ToArray();
    public IReadOnlyList<DeleteDocumentRequest> StagedDeletes => _stagedDeletes.ToArray();
    public IReadOnlyList<DocumentQuery> BoundedQueries => _boundedQueries.ToArray();
    public IReadOnlyList<GroundworkDocumentStoreEvent> Events => _events.ToArray();

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        _documents.SaveAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        _documents.LoadAsync(documentKind, id, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        _events.Enqueue(new GroundworkDocumentStoreEvent(
            GroundworkDocumentStoreEventKind.DirectDelete,
            request.DocumentKind,
            request.Id,
            request.ExpectedVersion));
        return _documents.DeleteAsync(request, cancellationToken);
    }

#pragma warning disable GW0004 // The decorator must preserve the complete IDocumentStore bridge surface.
    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
        DocumentStoreQuery query,
        CancellationToken cancellationToken = default) =>
        _documents.QueryAsync(query, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        _documents.QueryAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        _documents.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(
        PortableDocumentQuery query,
        CancellationToken cancellationToken = default) =>
        _documents.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

    public async Task<IDocumentUnitOfWork> BeginAsync(
        DocumentCommitScope scope,
        CancellationToken cancellationToken = default)
    {
        _events.Enqueue(new GroundworkDocumentStoreEvent(GroundworkDocumentStoreEventKind.BeginUnitOfWork));
        return new FailureInjectingUnitOfWork(
            await _documents.BeginAsync(scope, cancellationToken),
            _failures,
            _stagedSaves,
            _stagedDeletes,
            _events);
    }

    public Task<DocumentQueryResult> QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        _boundedQueries.Enqueue(query);
        _events.Enqueue(new GroundworkDocumentStoreEvent(
            GroundworkDocumentStoreEventKind.BoundedQuery,
            query.DocumentKind,
            query.QueryIdentity));
        return _boundedDocuments.QueryAsync(query, cancellationToken);
    }

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        _boundedDocuments.CountAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default) =>
        _boundedDocuments.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        _boundedDocuments.AnyAsync(query, cancellationToken);

    private static void EnsureSameAccessScope(DocumentStoreAccess documents, DocumentStoreAccess boundedDocuments)
    {
        if (documents.Kind != boundedDocuments.Kind ||
            !string.Equals(documents.Scope?.Value, boundedDocuments.Scope?.Value, StringComparison.Ordinal))
            throw new ArgumentException(
                "The combined document and bounded stores must use the same access scope.",
                nameof(boundedDocuments));
    }

    private sealed class FailureInjectingUnitOfWork(
        IDocumentUnitOfWork inner,
        GroundworkFailureController failures,
        ConcurrentQueue<SaveDocumentRequest> stagedSaves,
        ConcurrentQueue<DeleteDocumentRequest> stagedDeletes,
        ConcurrentQueue<GroundworkDocumentStoreEvent> events) : IDocumentUnitOfWork
    {
        private bool _underlyingCommitted;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            stagedSaves.Enqueue(request);
            return inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            stagedDeletes.Enqueue(request);
            return inner.DeleteAsync(request, cancellationToken);
        }

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await failures.ReachAsync(BeforeUnderlyingCommit, cancellationToken);
            }
            catch (Exception failure)
            {
                try
                {
                    await inner.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(failure, rollbackFailure);
                }

                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }

            await inner.CommitAsync(cancellationToken);
            _underlyingCommitted = true;
            events.Enqueue(new GroundworkDocumentStoreEvent(GroundworkDocumentStoreEventKind.CommitUnderlying));
            await failures.ReachAsync(AfterUnderlyingCommit, cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_underlyingCommitted)
                throw new InvalidOperationException("A committed physical unit of work cannot be rolled back.");
            return inner.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
