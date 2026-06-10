using System.Globalization;
using System.Text.Json;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Groundwork.MongoDb.Documents;

public sealed class MongoDbDocumentStore(IMongoDatabase database, StorageManifest manifest) : IDocumentStore
{
    public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(request.DocumentKind);
        var collection = GetCollection(unit);
        var existing = await LoadCoreAsync(unit, request.Id, cancellationToken);

        if (existing is not null && request.ExpectedVersion is not null && existing.Version != request.ExpectedVersion)
            return DocumentStoreWriteResult.ConcurrencyConflict;

        if (existing is null && request.ExpectedVersion is not null)
            return DocumentStoreWriteResult.NotFound;

        var now = DateTimeOffset.UtcNow;
        var version = existing is null ? 1 : existing.Version + 1;
        var createdAt = existing?.CreatedAt ?? now;
        var document = CreateDocument(request, version, createdAt, now);

        if (existing is null)
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        else
        {
            var filter = request.ExpectedVersion is null
                ? Builders<BsonDocument>.Filter.Eq("_id", request.Id)
                : Builders<BsonDocument>.Filter.Eq("_id", request.Id) & Builders<BsonDocument>.Filter.Eq("version", request.ExpectedVersion.Value);
            var result = await collection.ReplaceOneAsync(filter, document, cancellationToken: cancellationToken);
            if (request.ExpectedVersion is not null && result.MatchedCount == 0)
                return await LoadCoreAsync(unit, request.Id, cancellationToken) is null
                    ? DocumentStoreWriteResult.NotFound
                    : DocumentStoreWriteResult.ConcurrencyConflict;
        }

        return DocumentStoreWriteResult.Saved(new DocumentEnvelope(
            request.DocumentKind,
            request.Id,
            request.SchemaVersion,
            version,
            JsonDocument.Parse(request.ContentJson),
            createdAt,
            now));
    }

    public async Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(documentKind);
        return await LoadCoreAsync(unit, id, cancellationToken);
    }

    public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(request.DocumentKind);
        var collection = GetCollection(unit);
        var filter = request.ExpectedVersion is null
            ? Builders<BsonDocument>.Filter.Eq("_id", request.Id)
            : Builders<BsonDocument>.Filter.Eq("_id", request.Id) & Builders<BsonDocument>.Filter.Eq("version", request.ExpectedVersion.Value);

        var result = await collection.DeleteOneAsync(filter, cancellationToken);
        if (result.DeletedCount == 1)
            return DocumentStoreWriteResult.Deleted;

        return await LoadCoreAsync(unit, request.Id, cancellationToken) is null
            ? DocumentStoreWriteResult.NotFound
            : DocumentStoreWriteResult.ConcurrencyConflict;
    }

    public async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(query.DocumentKind);
        var index = unit.Indexes.SingleOrDefault(index => index.Identity == query.IndexName)
            ?? throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        if (index.Fields.Count != 1 || !index.SupportedOperations.Contains(PortableQueryOperation.Equal))
            throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        var collection = GetCollection(unit);
        var path = $"content.{index.Fields[0].Path}";
        var filter = Builders<BsonDocument>.Filter.Eq(path, ToBsonValue(index, query.Value));
        var documents = await collection
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .Skip(query.Skip ?? 0)
            .Limit(query.Take ?? 100)
            .ToListAsync(cancellationToken);

        return documents.Select(document => ReadEnvelope(unit, document)).ToList();
    }

    private async Task<DocumentEnvelope?> LoadCoreAsync(StorageUnit unit, string id, CancellationToken cancellationToken)
    {
        var document = await GetCollection(unit)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .SingleOrDefaultAsync(cancellationToken);

        return document is null ? null : ReadEnvelope(unit, document);
    }

    private IMongoCollection<BsonDocument> GetCollection(StorageUnit unit) =>
        database.GetCollection<BsonDocument>(MongoDbGroundworkNames.CollectionName(unit));

    private StorageUnit GetUnit(string documentKind) =>
        manifest.StorageUnits.SingleOrDefault(unit => unit.Identity.Value == documentKind)
        ?? throw new InvalidOperationException($"Document kind '{documentKind}' is not declared by manifest '{manifest.Identity}'.");

    private static BsonDocument CreateDocument(SaveDocumentRequest request, long version, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new()
        {
            ["_id"] = request.Id,
            ["schema_version"] = request.SchemaVersion,
            ["version"] = version,
            ["content"] = BsonDocument.Parse(request.ContentJson),
            ["created_utc"] = createdAt.ToString("O"),
            ["updated_utc"] = updatedAt.ToString("O")
        };

    private static DocumentEnvelope ReadEnvelope(StorageUnit unit, BsonDocument document) =>
        new(
            unit.Identity.Value,
            document.GetValue("_id").AsString,
            document.GetValue("schema_version").AsString,
            document.GetValue("version").ToInt64(),
            JsonDocument.Parse(document.GetValue("content").AsBsonDocument.ToJson()),
            DateTimeOffset.Parse(document.GetValue("created_utc").AsString),
            DateTimeOffset.Parse(document.GetValue("updated_utc").AsString));

    private static BsonValue ToBsonValue(IndexDeclaration index, string value) =>
        index.ValueKind switch
        {
            IndexValueKind.Number when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue) => longValue,
            IndexValueKind.Number when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) => doubleValue,
            IndexValueKind.Boolean when bool.TryParse(value, out var boolValue) => boolValue,
            _ => value
        };
}
