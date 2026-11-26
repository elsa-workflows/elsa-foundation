using System.Data;
using System.Data.Common;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableSourceReferenceStore
{
    public async ValueTask<IReadOnlyList<RuntimeReferenceSourceSnapshot>> ListByDefinitionVersionBoundedAsync(string definitionVersionId,
        PersistenceScope expectedScope, RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId); ArgumentNullException.ThrowIfNull(expectedScope);
        if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
        var rows = new List<RuntimeBoundedSourceRead.Row>();
        try
        {
            return await RuntimeBoundedSourceRead.ExecuteAsync<IReadOnlyList<RuntimeReferenceSourceSnapshot>>(context, limits, [], async (lengths, token) =>
            {
                if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
                var result = new List<RuntimeReferenceSourceSnapshot>();
                foreach (var key in rows)
                {
                    var row = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync(value => value.Id == key.Id, token)
                        ?? throw new RuntimeSourceReadException("runtime_source_stored_snapshot_lost");
                    var reference = Read(row, expectedScope.Value, Decode(row.SourceReferenceId), limits);
                    if (reference.DefinitionVersionId != definitionVersionId) throw new RuntimeSourceReadException("runtime_source_reference_identity_conflict");
                    result.Add(new(reference, expectedScope, lengths[key]));
                }
                return Array.AsReadOnly(result.ToArray());
            }, cancellationToken, SelectRows);
        }
        catch (Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions.RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (InvalidDataException) { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || Elsa.Persistence.EntityFramework.EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { cancellationToken.ThrowIfCancellationRequested(); throw NormalizeProviderFailure("reading bounded version references", definitionVersionId, exception); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }

        async ValueTask<IReadOnlyList<RuntimeBoundedSourceRead.Row>> SelectRows(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            var entity = context.Model.FindEntityType(typeof(WorkflowExecutableSourceReferenceEntity)) ?? throw new RuntimeSourceReadException("runtime_source_mapping_unsupported", true);
            var table = entity.GetTableName() ?? throw new RuntimeSourceReadException("runtime_source_mapping_unsupported", true);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema()); var quote = context.GetService<ISqlGenerationHelper>();
            string Column(string name) => quote.DelimitIdentifier(entity.FindProperty(name)?.GetColumnName(store) ?? throw new RuntimeSourceReadException("runtime_source_mapping_unsupported", true));
            var id = Column("Id"); var length = "length(CAST(" + id + " AS BLOB))";
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT typeof(" + id + ")," + length + ",CASE WHEN typeof(" + id + ")='text' AND " + length + "<=@metadata THEN CAST(" + id + " AS BLOB) ELSE NULL END FROM " +
                quote.DelimitIdentifier(table, entity.GetSchema()) + " WHERE " + Column("ScopeKeyHash") + "=@scope AND " + Column("DefinitionVersionIdHash") + "=@version ORDER BY " + id + " LIMIT @count";
            Add("@metadata", limits.MaxMetadataBytes, DbType.Int32); Add("@scope", Hash(expectedScope.Value), DbType.String);
            Add("@version", Hash(definitionVersionId), DbType.String); Add("@count", limits.MaxMaterials + 1, DbType.Int32);
            await using var reader = await command.ExecuteReaderAsync(token); var utf8 = new UTF8Encoding(false, true);
            while (await reader.ReadAsync(token))
            {
                if (rows.Count >= limits.MaxMaterials) throw new RuntimeSourceReadException("runtime_source_reference_limit");
                if (reader.GetString(0) != "text" || reader.IsDBNull(1) || reader.GetInt64(1) < 1 || reader.GetInt64(1) > limits.MaxMetadataBytes || reader.IsDBNull(2))
                    throw new RuntimeSourceReadException("runtime_source_stored_identity_invalid");
                rows.Add(new(typeof(WorkflowExecutableSourceReferenceEntity), utf8.GetString(reader.GetFieldValue<byte[]>(2))));
            }
            return rows;
            void Add(string name, object value, DbType type)
            { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; parameter.DbType = type; command.Parameters.Add(parameter); }
        }
    }

    public async ValueTask<RuntimeReferenceSourceSnapshot?> FindBoundedAsync(string sourceReferenceId, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId); ArgumentNullException.ThrowIfNull(expectedScope);
        if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
        var key = new RuntimeBoundedSourceRead.Row(typeof(WorkflowExecutableSourceReferenceEntity), CreateId(expectedScope.Value, sourceReferenceId));
        try
        {
            return await RuntimeBoundedSourceRead.ExecuteAsync<RuntimeReferenceSourceSnapshot?>(context, limits, [key], async (lengths, token) =>
            {
                if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
                var row = await RowsOf(expectedScope.Value, sourceReferenceId).AsNoTracking().SingleOrDefaultAsync(token);
                return row is null ? null : new(Read(row, expectedScope.Value, sourceReferenceId, limits), expectedScope, lengths[key]);
            }, cancellationToken);
        }
        catch (Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions.RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (InvalidDataException) { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || Elsa.Persistence.EntityFramework.EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { cancellationToken.ThrowIfCancellationRequested(); throw NormalizeProviderFailure("reading bounded source reference", sourceReferenceId, exception); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
    }
}
