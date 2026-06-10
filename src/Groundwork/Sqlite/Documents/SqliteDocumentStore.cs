using System.Text.Json;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Store;
using Microsoft.Data.Sqlite;

namespace Groundwork.Sqlite.Documents;

public sealed class SqliteDocumentStore(SqliteConnection connection, StorageManifest manifest) : IDocumentStore
{
    public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(request.DocumentKind);
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LoadCoreAsync(request.DocumentKind, request.Id, (SqliteTransaction)transaction, cancellationToken);
        if (existing is not null && request.ExpectedVersion is not null && existing.Version != request.ExpectedVersion)
            return DocumentStoreWriteResult.ConcurrencyConflict;

        if (existing is null && request.ExpectedVersion is not null)
            return DocumentStoreWriteResult.NotFound;

        var now = DateTimeOffset.UtcNow;
        var version = existing is null ? 1 : existing.Version + 1;
        var createdAt = existing?.CreatedAt ?? now;

        await UpsertDocumentAsync(request, version, createdAt, now, (SqliteTransaction)transaction, cancellationToken);
        await DeleteIndexesAsync(request.DocumentKind, request.Id, (SqliteTransaction)transaction, cancellationToken);
        await InsertIndexesAsync(unit, request.Id, request.ContentJson, (SqliteTransaction)transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

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
        _ = GetUnit(documentKind);
        await EnsureOpenAsync(cancellationToken);
        return await LoadCoreAsync(documentKind, id, null, cancellationToken);
    }

    public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        _ = GetUnit(request.DocumentKind);
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LoadCoreAsync(request.DocumentKind, request.Id, (SqliteTransaction)transaction, cancellationToken);
        if (existing is null)
            return DocumentStoreWriteResult.NotFound;

        if (request.ExpectedVersion is not null && existing.Version != request.ExpectedVersion)
            return DocumentStoreWriteResult.ConcurrencyConflict;

        await DeleteIndexesAsync(request.DocumentKind, request.Id, (SqliteTransaction)transaction, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            DELETE FROM groundwork_documents
            WHERE document_kind = $kind AND id = $id;
            """;
        command.Parameters.AddWithValue("$kind", request.DocumentKind);
        command.Parameters.AddWithValue("$id", request.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return DocumentStoreWriteResult.Deleted;
    }

    public async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(query.DocumentKind);
        var index = unit.Indexes.SingleOrDefault(index => index.Identity == query.IndexName)
            ?? throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        if (!index.SupportedOperations.Contains(PortableQueryOperation.Equal))
            throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        await EnsureOpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.document_kind, d.id, d.schema_version, d.version, d.content_json, d.created_utc, d.updated_utc
            FROM groundwork_documents d
            INNER JOIN groundwork_document_indexes i
                ON i.document_kind = d.document_kind AND i.document_id = d.id
            WHERE i.document_kind = $kind AND i.index_name = $index AND i.index_value = $value
            ORDER BY d.id
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$kind", query.DocumentKind);
        command.Parameters.AddWithValue("$index", query.IndexName);
        command.Parameters.AddWithValue("$value", query.Value);
        command.Parameters.AddWithValue("$take", query.Take ?? 100);
        command.Parameters.AddWithValue("$skip", query.Skip ?? 0);

        var documents = new List<DocumentEnvelope>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            documents.Add(ReadEnvelope(reader));

        return documents;
    }

    private async Task UpsertDocumentAsync(
        SaveDocumentRequest request,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO groundwork_documents
            (document_kind, id, schema_version, version, content_json, created_utc, updated_utc)
            VALUES ($kind, $id, $schemaVersion, $version, $content, $createdUtc, $updatedUtc)
            ON CONFLICT(document_kind, id) DO UPDATE SET
                schema_version = excluded.schema_version,
                version = excluded.version,
                content_json = excluded.content_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$kind", request.DocumentKind);
        command.Parameters.AddWithValue("$id", request.Id);
        command.Parameters.AddWithValue("$schemaVersion", request.SchemaVersion);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$content", request.ContentJson);
        command.Parameters.AddWithValue("$createdUtc", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", updatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DocumentEnvelope?> LoadCoreAsync(
        string documentKind,
        string id,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT document_kind, id, schema_version, version, content_json, created_utc, updated_utc
            FROM groundwork_documents
            WHERE document_kind = $kind AND id = $id;
            """;
        command.Parameters.AddWithValue("$kind", documentKind);
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEnvelope(reader) : null;
    }

    private async Task DeleteIndexesAsync(string documentKind, string id, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM groundwork_document_indexes
            WHERE document_kind = $kind AND document_id = $id;
            """;
        command.Parameters.AddWithValue("$kind", documentKind);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertIndexesAsync(StorageUnit unit, string id, string contentJson, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(contentJson);
        foreach (var index in unit.Indexes)
        {
            if (!TryGetIndexValue(document.RootElement, index, out var value))
                continue;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO groundwork_document_indexes
                (document_kind, index_name, index_value, document_id, is_unique)
                VALUES ($kind, $index, $value, $documentId, $isUnique);
                """;
            command.Parameters.AddWithValue("$kind", unit.Identity.Value);
            command.Parameters.AddWithValue("$index", index.Identity);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$documentId", id);
            command.Parameters.AddWithValue("$isUnique", index.IsUnique ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private StorageUnit GetUnit(string documentKind) =>
        manifest.StorageUnits.SingleOrDefault(unit => unit.Identity.Value == documentKind)
        ?? throw new InvalidOperationException($"Document kind '{documentKind}' is not declared by manifest '{manifest.Identity}'.");

    private static bool TryGetIndexValue(JsonElement root, IndexDeclaration index, out string value)
    {
        value = "";
        if (index.Fields.Count != 1)
            return false;

        if (!TryGetPropertyPath(root, index.Fields[0].Path, out var element))
            return false;

        value = NormalizeValue(element);
        return value.Length > 0 || element.ValueKind == JsonValueKind.String;
    }

    private static bool TryGetPropertyPath(JsonElement root, string path, out JsonElement element)
    {
        element = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return false;
        }

        return element.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string NormalizeValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };

    private static DocumentEnvelope ReadEnvelope(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            JsonDocument.Parse(reader.GetString(4)),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }
}
