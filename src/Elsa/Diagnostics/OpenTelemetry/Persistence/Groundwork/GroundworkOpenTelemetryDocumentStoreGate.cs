using System.Runtime.ExceptionServices;
using Groundwork.Core.Queries;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;

/// <summary>Defers OpenTelemetry catalog access until the lifecycle owns its scoped document resources.</summary>
internal sealed class GroundworkOpenTelemetryDocumentStoreGate(DocumentStoreAccess access)
    : IGroundworkOpenTelemetryDocumentCommands, IBoundedDocumentStore
{
    private readonly Lock _gate = new();
    private IDocumentStore? _documents;
    private IBoundedDocumentStore? _queries;
    private ExceptionDispatchInfo? _failure;
    private bool _released;

    public DocumentStoreAccess Access { get; } = access ?? throw new ArgumentNullException(nameof(access));

    public void Publish(IDocumentStore documents, IBoundedDocumentStore queries)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(queries);
        lock (_gate)
        {
            if (_released)
                throw new ObjectDisposedException(nameof(GroundworkOpenTelemetryDocumentStoreGate));
            if (_failure is not null)
                throw new InvalidOperationException("A failed OpenTelemetry document-store gate cannot publish a store.");
            if (_documents is not null)
                throw new InvalidOperationException("The OpenTelemetry document-store gate has already published a store.");
            _documents = documents;
            _queries = queries;
        }
    }

    public void PublishFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_gate)
        {
            if (_documents is null && _failure is null)
                _failure = ExceptionDispatchInfo.Capture(failure);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _documents = null;
            _queries = null;
            _released = true;
        }
    }

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
        Documents.SaveAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Documents.LoadAsync(documentKind, id, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
        Documents.DeleteAsync(request, cancellationToken);

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Queries.QueryAsync(query, cancellationToken);

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Queries.CountAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Queries.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Queries.AnyAsync(query, cancellationToken);

    private IDocumentStore Documents => GetStores().Documents;

    private IBoundedDocumentStore Queries => GetStores().Queries;

    private (IDocumentStore Documents, IBoundedDocumentStore Queries) GetStores()
    {
        ExceptionDispatchInfo? failure;
        lock (_gate)
        {
            if (_documents is not null && _queries is not null)
                return (_documents, _queries);
            failure = _failure;
            if (failure is null && _released)
                throw new ObjectDisposedException(nameof(GroundworkOpenTelemetryDocumentStoreGate));
        }

        failure?.Throw();
        throw new InvalidOperationException("The OpenTelemetry document store has not completed startup admission.");
    }
}
