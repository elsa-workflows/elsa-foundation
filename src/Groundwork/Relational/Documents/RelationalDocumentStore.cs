using System.Data;
using System.Data.Common;
using System.Text.Json;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Documents.Store;
using Groundwork.Relational.Physicalization;

namespace Groundwork.Relational.Documents;

public class RelationalDocumentStore(DbConnection connection, StorageManifest manifest, RelationalDocumentStoreDialect dialect) : IDocumentStore
{
    public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(request.DocumentKind);
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LoadCoreAsync(request.DocumentKind, request.Id, transaction, cancellationToken);
        if (existing is not null && request.ExpectedVersion is not null && existing.Version != request.ExpectedVersion)
            return DocumentStoreWriteResult.ConcurrencyConflict;

        if (existing is null && request.ExpectedVersion is not null)
            return DocumentStoreWriteResult.NotFound;

        var now = DateTimeOffset.UtcNow;
        var version = existing is null ? 1 : existing.Version + 1;
        var createdAt = existing?.CreatedAt ?? now;

        if (existing is null)
        {
            try
            {
                await InsertDocumentAsync(request, version, createdAt, now, transaction, cancellationToken);
            }
            catch (DbException exception) when (dialect.IsDuplicateDocumentKeyException(exception))
            {
                return DocumentStoreWriteResult.ConcurrencyConflict;
            }
        }
        else
        {
            var updated = await UpdateDocumentAsync(request, version, now, transaction, cancellationToken);
            if (!updated)
                return WriteMissResult(request.ExpectedVersion);
        }

        try
        {
            await DeleteIndexesAsync(request.DocumentKind, request.Id, transaction, cancellationToken);
            await InsertIndexesAsync(unit, request.Id, request.ContentJson, transaction, cancellationToken);
            await RefreshPhysicalizedAsync(unit, request.Id, version, request.ContentJson, transaction, cancellationToken);
        }
        catch (DbException exception) when (dialect.IsUniqueIndexException(exception))
        {
            return DocumentStoreWriteResult.ConcurrencyConflict;
        }
        catch (DbException exception) when (dialect.IsWriteDependencyException(exception))
        {
            return WriteMissResult(request.ExpectedVersion);
        }

        await transaction.CommitAsync(cancellationToken);

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
        _ = GetUnit(documentKind);
        await EnsureOpenAsync(cancellationToken);
        return await LoadCoreAsync(documentKind, id, null, cancellationToken);
    }

    public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(request.DocumentKind);
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LoadCoreAsync(request.DocumentKind, request.Id, transaction, cancellationToken);
        if (existing is null)
            return DocumentStoreWriteResult.NotFound;

        if (request.ExpectedVersion is not null && existing.Version != request.ExpectedVersion)
            return DocumentStoreWriteResult.ConcurrencyConflict;

        await using var command = CreateCommand(dialect.DeleteDocumentCommandSql(request.ExpectedVersion is not null), transaction);
        AddParameter(command, "kind", request.DocumentKind);
        AddParameter(command, "id", request.Id);
        if (request.ExpectedVersion is not null)
            AddParameter(command, "expectedVersion", request.ExpectedVersion.Value);

        var deletedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deletedRows == 0)
            return WriteMissResult(request.ExpectedVersion);

        await DeleteIndexesAsync(request.DocumentKind, request.Id, transaction, cancellationToken);
        await DeletePhysicalizedAsync(unit, request.Id, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return DocumentStoreWriteResult.Deleted;
    }

    public async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var unit = GetUnit(query.DocumentKind);
        var (skip, take) = NormalizePaging(query);
        var index = unit.Indexes.SingleOrDefault(index => index.Identity == query.IndexName)
            ?? throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        if (!index.SupportedOperations.Contains(PortableQueryOperation.Equal))
            throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        if (index.Fields.Count != 1)
            throw new UndeclaredDocumentIndexException(query.DocumentKind, query.IndexName);

        await EnsureOpenAsync(cancellationToken);
        await using var command = CreateQueryCommand(unit, query);
        AddParameter(command, "kind", query.DocumentKind);
        AddParameter(command, "index", query.IndexName);
        AddParameter(command, "value", query.Value);
        AddParameter(command, "take", take);
        AddParameter(command, "skip", skip);

        var documents = new List<DocumentEnvelope>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            documents.Add(ReadEnvelope(reader));

        return documents;
    }

    private DbCommand CreateQueryCommand(StorageUnit unit, DocumentStoreQuery query)
    {
        var physicalizedField = PhysicalizationProjection.EligibleFields(unit).SingleOrDefault(field => field.Name == query.IndexName);
        if (physicalizedField is null)
            return CreateCommand(dialect.QueryByIndexSql);

        var table = RelationalPhysicalizationNames.TableName(unit);
        var column = RelationalPhysicalizationNames.ColumnName(physicalizedField);
        return CreateCommand(dialect.QueryByPhysicalizedSql(table, column));
    }

    private async Task InsertDocumentAsync(
        SaveDocumentRequest request,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(dialect.InsertDocumentSql, transaction);
        AddDocumentParameters(command, request, version, createdAt, updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> UpdateDocumentAsync(
        SaveDocumentRequest request,
        long version,
        DateTimeOffset updatedAt,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(dialect.UpdateDocumentCommandSql(request.ExpectedVersion is not null), transaction);
        AddDocumentParameters(command, request, version, updatedAt);
        if (request.ExpectedVersion is not null)
            AddParameter(command, "expectedVersion", request.ExpectedVersion.Value);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private void AddDocumentParameters(DbCommand command, SaveDocumentRequest request, long version, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        AddDocumentParameters(command, request, version);
        AddParameter(command, "createdUtc", createdAt.ToString("O"));
        AddParameter(command, "updatedUtc", updatedAt.ToString("O"));
    }

    private void AddDocumentParameters(DbCommand command, SaveDocumentRequest request, long version, DateTimeOffset updatedAt)
    {
        AddDocumentParameters(command, request, version);
        AddParameter(command, "updatedUtc", updatedAt.ToString("O"));
    }

    private void AddDocumentParameters(DbCommand command, SaveDocumentRequest request, long version)
    {
        AddParameter(command, "kind", request.DocumentKind);
        AddParameter(command, "id", request.Id);
        AddParameter(command, "schemaVersion", request.SchemaVersion);
        AddParameter(command, "version", version);
        AddParameter(command, "content", request.ContentJson);
    }

    private async Task<DocumentEnvelope?> LoadCoreAsync(string documentKind, string id, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(dialect.LoadDocumentSql, transaction);
        AddParameter(command, "kind", documentKind);
        AddParameter(command, "id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEnvelope(reader) : null;
    }

    private async Task DeleteIndexesAsync(string documentKind, string id, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(dialect.DeleteIndexesSql, transaction);
        AddParameter(command, "kind", documentKind);
        AddParameter(command, "id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertIndexesAsync(StorageUnit unit, string id, string contentJson, DbTransaction transaction, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(contentJson);
        foreach (var index in unit.Indexes)
        {
            if (!TryGetIndexValue(document.RootElement, index, out var value))
                continue;

            await using var command = CreateCommand(dialect.InsertIndexSql, transaction);
            AddParameter(command, "kind", unit.Identity.Value);
            AddParameter(command, "index", index.Identity);
            AddParameter(command, "value", value);
            AddParameter(command, "documentId", id);
            AddParameter(command, "isUnique", dialect.Boolean(index.IsUnique));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task RefreshPhysicalizedAsync(
        StorageUnit unit,
        string id,
        long version,
        string contentJson,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var fields = PhysicalizationProjection.EligibleFields(unit);
        if (fields.Count == 0)
            return;

        await DeletePhysicalizedAsync(unit, id, transaction, cancellationToken);

        using var document = JsonDocument.Parse(contentJson);
        var table = RelationalPhysicalizationNames.TableName(unit);
        var columnNames = fields.Select(RelationalPhysicalizationNames.ColumnName).ToList();
        await using var command = CreateCommand(dialect.InsertPhysicalizedSql(table, columnNames), transaction);
        AddParameter(command, "kind", unit.Identity.Value);
        AddParameter(command, "id", id);
        AddParameter(command, "version", version);

        for (var index = 0; index < fields.Count; index++)
        {
            var value = TryGetPropertyPath(document.RootElement, fields[index].Path, out var element)
                ? NormalizeValue(element)
                : null;
            AddParameter(command, $"physicalized{index}", value is null ? DBNull.Value : value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeletePhysicalizedAsync(StorageUnit unit, string id, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (PhysicalizationProjection.EligibleFields(unit).Count == 0)
            return;

        await using var command = CreateCommand(dialect.DeletePhysicalizedSql(RelationalPhysicalizationNames.TableName(unit)), transaction);
        AddParameter(command, "kind", unit.Identity.Value);
        AddParameter(command, "id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(string commandText, DbTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        return command;
    }

    private void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = dialect.Parameter(name);
        parameter.Value = value;
        command.Parameters.Add(parameter);
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

    private static DocumentEnvelope ReadEnvelope(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));

    private static DocumentStoreWriteResult WriteMissResult(long? expectedVersion) =>
        expectedVersion is null
            ? DocumentStoreWriteResult.NotFound
            : DocumentStoreWriteResult.ConcurrencyConflict;

    private static (int Skip, int Take) NormalizePaging(DocumentStoreQuery query)
    {
        var skip = query.Skip ?? 0;
        var take = query.Take ?? 100;

        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(DocumentStoreQuery.Skip), skip, "Skip must be greater than or equal to 0.");

        if (take < 0)
            throw new ArgumentOutOfRangeException(nameof(DocumentStoreQuery.Take), take, "Take must be greater than or equal to 0.");

        return (skip, take);
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }
}
