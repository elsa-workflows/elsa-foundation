using System.Collections.Concurrent;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Records every bounded request before forwarding it to the supplied provider test double.</summary>
public sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
{
    public ConcurrentQueue<DocumentQuery> Queries { get; } = new();

    public Task<DocumentQueryResult> QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Enqueue(query);
        return inner.QueryAsync(query, cancellationToken);
    }

    public Task<long> CountAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Enqueue(query);
        return inner.CountAsync(query, cancellationToken);
    }

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Enqueue(query);
        return inner.FirstOrDefaultAsync(query, cancellationToken);
    }

    public Task<bool> AnyAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Enqueue(query);
        return inner.AnyAsync(query, cancellationToken);
    }
}
