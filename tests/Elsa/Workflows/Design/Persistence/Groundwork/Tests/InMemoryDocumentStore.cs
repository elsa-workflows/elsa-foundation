using System.Collections.Concurrent;
using System.Text.Json;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

// Test double standing in for a Groundwork provider. It implements the same provider-neutral
// IDocumentStore contract the real relational/document providers implement and resolves a query's
// index name -> declared field path from the manifest, exactly like the real providers. This proves
// GroundworkReadStore behaves correctly against the actual provider query surface (equality on a
// declared index + offset paging), not a bespoke shim.
internal sealed class InMemoryDocumentStore(StorageManifest manifest) : IDocumentStore
{
    private readonly ConcurrentDictionary<(string Kind, string Id), DocumentEnvelope> _docs = new();
    private readonly Lock _gate = new();

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            _docs.TryGetValue(key, out var existing);

            if (request.ExpectedVersion is { } expected && (existing?.Version ?? 0) != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

            var now = DateTimeOffset.UtcNow;
            var envelope = new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                (existing?.Version ?? 0) + 1,
                request.ContentJson,
                existing?.CreatedAt ?? now,
                now);

            _docs[key] = envelope;
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }
    }

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_docs.GetValueOrDefault((documentKind, id)));

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            if (!_docs.TryGetValue(key, out var existing))
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _docs.TryRemove(key, out _);
            return Task.FromResult(DocumentStoreWriteResult.Deleted);
        }
    }

    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var fieldPath = ResolveIndexFieldPath(query.DocumentKind, query.IndexName);
        var matches = _docs.Values
            .Where(d => d.DocumentKind == query.DocumentKind)
            .Where(d => string.Equals(ReadField(d.ContentJson, fieldPath), query.Value, StringComparison.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Skip(query.Skip ?? 0);

        if (query.Take is { } take)
            matches = matches.Take(take);

        return Task.FromResult<IReadOnlyList<DocumentEnvelope>>(matches.ToArray());
    }

    private string ResolveIndexFieldPath(string documentKind, string indexName)
    {
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == documentKind);
        var index = unit.Indexes.SingleOrDefault(i => i.Identity == indexName)
            ?? throw new UndeclaredDocumentIndexException(documentKind, indexName);
        return index.Fields[0].Path;
    }

    private static string? ReadField(string contentJson, string path)
    {
        using var doc = JsonDocument.Parse(contentJson);
        var element = doc.RootElement;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }
}
