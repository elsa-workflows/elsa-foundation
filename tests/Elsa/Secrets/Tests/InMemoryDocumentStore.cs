using System.Collections.Concurrent;
using System.Text.Json;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Secrets.Tests;

internal sealed class InMemoryDocumentStore(StorageManifest manifest) : IDocumentStore
{
    private readonly ConcurrentDictionary<(string Kind, string Id), DocumentEnvelope> _docs = new();

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        _docs.TryGetValue((request.DocumentKind, request.Id), out var existing);
        var now = DateTimeOffset.UtcNow;
        var envelope = new DocumentEnvelope(
            request.DocumentKind,
            request.Id,
            request.SchemaVersion,
            (existing?.Version ?? 0) + 1,
            request.ContentJson,
            existing?.CreatedAt ?? now,
            now);

        _docs[(request.DocumentKind, request.Id)] = envelope;
        return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
    }

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_docs.GetValueOrDefault((documentKind, id)));

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var deleted = _docs.TryRemove((request.DocumentKind, request.Id), out _);
        return Task.FromResult(deleted ? DocumentStoreWriteResult.Deleted : DocumentStoreWriteResult.NotFound);
    }

    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var fieldPath = ResolveIndexFieldPath(query.DocumentKind, query.IndexName);
        var matches = _docs.Values
            .Where(x => x.DocumentKind == query.DocumentKind)
            .Where(x => string.Equals(ReadField(x.ContentJson, fieldPath), query.Value, StringComparison.Ordinal))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult<IReadOnlyList<DocumentEnvelope>>(matches);
    }

    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private string ResolveIndexFieldPath(string documentKind, string indexName)
    {
        var unit = manifest.StorageUnits.Single(x => x.Identity.Value == documentKind);
        var index = unit.Indexes.Single(x => x.Identity == indexName);
        return index.Fields[0].Path;
    }

    private static string? ReadField(string contentJson, string path)
    {
        using var document = JsonDocument.Parse(contentJson);
        var element = document.RootElement;

        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }
}
