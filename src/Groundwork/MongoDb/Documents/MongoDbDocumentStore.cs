using System.Globalization;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Documents.Store;
using MongoDB.Bson;
using MongoDB.Bson.IO;
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
        var document = CreateDocument(unit, request, version, createdAt, now);

        if (existing is null)
        {
            try
            {
                await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            }
            catch (MongoWriteException exception) when (IsDuplicateKey(exception))
            {
                return DocumentStoreWriteResult.ConcurrencyConflict;
            }
        }
        else
        {
            var filter = request.ExpectedVersion is null
                ? Builders<BsonDocument>.Filter.Eq("_id", request.Id)
                : Builders<BsonDocument>.Filter.Eq("_id", request.Id) & Builders<BsonDocument>.Filter.Eq("version", request.ExpectedVersion.Value);
            ReplaceOneResult result;
            try
            {
                result = await collection.ReplaceOneAsync(filter, document, cancellationToken: cancellationToken);
            }
            catch (MongoWriteException exception) when (IsDuplicateKey(exception))
            {
                return DocumentStoreWriteResult.ConcurrencyConflict;
            }

            if (result.MatchedCount == 0)
            {
                if (request.ExpectedVersion is null)
                    return DocumentStoreWriteResult.NotFound;

                return await LoadCoreAsync(unit, request.Id, cancellationToken) is null
                    ? DocumentStoreWriteResult.NotFound
                    : DocumentStoreWriteResult.ConcurrencyConflict;
            }
        }

        return DocumentStoreWriteResult.Saved(new DocumentEnvelope(
            request.DocumentKind,
            request.Id,
            request.SchemaVersion,
            version,
            request.ContentJson,
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

        if (query.Take == 0)
            return [];

        var collection = GetCollection(unit);
        var physicalizedField = PhysicalizationProjection.EligibleFields(unit).SingleOrDefault(field => field.Name == query.IndexName);
        var path = physicalizedField is null
            ? $"content.{index.Fields[0].Path}"
            : $"physicalized.{MongoDbGroundworkNames.PhysicalizedFieldName(physicalizedField)}";
        var filter = Builders<BsonDocument>.Filter.Eq(path, ToBsonValue(index.ValueKind, query.Value));
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

    private static BsonDocument CreateDocument(StorageUnit unit, SaveDocumentRequest request, long version, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        var content = BsonDocument.Parse(request.ContentJson);
        var document = new BsonDocument
        {
            ["_id"] = request.Id,
            ["schema_version"] = request.SchemaVersion,
            ["version"] = version,
            ["content"] = content,
            ["content_json"] = request.ContentJson,
            ["created_utc"] = createdAt.ToString("O"),
            ["updated_utc"] = updatedAt.ToString("O")
        };

        var physicalized = CreatePhysicalizedDocument(unit, content);
        if (physicalized.ElementCount > 0)
            document["physicalized"] = physicalized;

        return document;
    }

    private static DocumentEnvelope ReadEnvelope(StorageUnit unit, BsonDocument document) =>
        new(
            unit.Identity.Value,
            document.GetValue("_id").AsString,
            document.GetValue("schema_version").AsString,
            document.GetValue("version").ToInt64(),
            ReadContentJson(document),
            DateTimeOffset.Parse(document.GetValue("created_utc").AsString),
            DateTimeOffset.Parse(document.GetValue("updated_utc").AsString));

    private static string ReadContentJson(BsonDocument document) =>
        document.TryGetValue("content_json", out var contentJson)
            ? contentJson.AsString
            : document.GetValue("content").AsBsonDocument.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });

    private static BsonDocument CreatePhysicalizedDocument(StorageUnit unit, BsonDocument content)
    {
        var physicalized = new BsonDocument();
        foreach (var field in PhysicalizationProjection.EligibleFields(unit))
        {
            if (TryGetBsonPath(content, field.Path, out var value) && value is not BsonNull)
                physicalized[MongoDbGroundworkNames.PhysicalizedFieldName(field)] = value;
        }

        return physicalized;
    }

    private static bool TryGetBsonPath(BsonDocument root, string path, out BsonValue value)
    {
        value = BsonNull.Value;
        BsonValue current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!current.IsBsonDocument || !current.AsBsonDocument.TryGetValue(segment, out current))
                return false;
        }

        value = current;
        return true;
    }

    private static BsonValue ToBsonValue(IndexValueKind valueKind, string value) =>
        valueKind switch
        {
            IndexValueKind.Number when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue) => longValue,
            IndexValueKind.Number when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) => doubleValue,
            IndexValueKind.Boolean when bool.TryParse(value, out var boolValue) => boolValue,
            _ => value
        };

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Code == 11000;
}
