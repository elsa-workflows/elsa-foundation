using System.Data.Common;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Shared relational bounded query plan. Provider projects supply only their JSON expressions, connection,
/// and native expression-index declarations; result semantics stay identical across providers.
/// </summary>
public abstract class RelationalGroundworkWorkflowExecutionStatePageQuery(
    IDocumentStore documentStore,
    IGroundworkRuntimeDocumentSerializer serializer) : IGroundworkWorkflowExecutionStatePageQuery
{
    private const int MigrationBatchSize = 100;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    protected abstract DbConnection CreateConnection();
    protected abstract string SortTicksExpression { get; }
    protected abstract string TenantIdExpression { get; }
    protected abstract string DefinitionIdExpression { get; }
    protected abstract string StatusExpression { get; }
    protected abstract string RunKindExpression { get; }
    protected abstract string CorrelationIdExpression { get; }
    protected abstract string ArtifactIdExpression { get; }
    protected abstract string MissingSortTicksPredicate { get; }
    protected abstract IReadOnlyList<string> CreateIndexStatements { get; }

    public async ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var filteredWhere = BuildWhere(query, includeCursor: false);
        var total = await CountAsync(connection, filteredWhere, cancellationToken);
        if (total == 0)
            return new([], null, null, false, false, 0);

        var pageWhere = BuildWhere(query, includeCursor: true);
        var reverse = query.Cursor?.Direction == WorkflowExecutionStatePageDirection.Previous;
        var order = reverse
            ? $"{SortTicksExpression} ASC, d.id DESC"
            : $"{SortTicksExpression} DESC, d.id ASC";
        var sql = $"""
                  SELECT d.id, d.schema_version, d.version, d.content_json
                  FROM groundwork_documents d
                  WHERE {pageWhere.Sql}
                  ORDER BY {order}
                  LIMIT @pageLimit;
                  """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, pageWhere.Parameters);
        var pageSize = WorkflowExecutionStateHistory.EffectivePageSize(query);
        AddParameter(command, "pageLimit", checked(pageSize + 1));

        var states = new List<WorkflowExecutionState>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var envelope = ReadEnvelope(reader);
                states.Add(serializer.Deserialize<WorkflowExecutionStateDocument>(envelope).State);
            }
        }

        var hasExtra = states.Count > pageSize;
        if (hasExtra)
            states.RemoveAt(states.Count - 1);
        if (reverse)
            states.Reverse();

        var hasPrevious = query.Cursor?.Direction switch
        {
            WorkflowExecutionStatePageDirection.Next => true,
            WorkflowExecutionStatePageDirection.Previous => hasExtra,
            _ => false
        };
        var hasNext = query.Cursor?.Direction switch
        {
            WorkflowExecutionStatePageDirection.Previous => true,
            _ => hasExtra
        };

        return new(
            states,
            hasPrevious && states.Count > 0
                ? WorkflowExecutionStateHistory.Cursor(states[0], WorkflowExecutionStatePageDirection.Previous, query)
                : null,
            hasNext && states.Count > 0
                ? WorkflowExecutionStateHistory.Cursor(states[^1], WorkflowExecutionStatePageDirection.Next, query)
                : null,
            hasPrevious,
            hasNext,
            total);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await BackfillStableSortKeysAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            foreach (var statement in CreateIndexStatements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task BackfillStableSortKeysAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<DocumentEnvelope> batch;
            await using (var connection = CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                                      SELECT d.id, d.schema_version, d.version, d.content_json
                                      FROM groundwork_documents d
                                      WHERE d.document_kind = @documentKind AND {MissingSortTicksPredicate}
                                      ORDER BY d.id
                                      LIMIT @batchSize;
                                      """;
                AddParameter(command, "documentKind", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);
                AddParameter(command, "batchSize", MigrationBatchSize);

                var documents = new List<DocumentEnvelope>(MigrationBatchSize);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    documents.Add(ReadEnvelope(reader));
                batch = documents;
            }

            if (batch.Count == 0)
                return;

            foreach (var envelope in batch)
            {
                var state = serializer.Deserialize<WorkflowExecutionStateDocument>(envelope).State;
                var document = WorkflowExecutionStateDocument.From(state);
                var serialized = serializer.Serialize(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, document);
                var result = await documentStore.SaveAsync(new SaveDocumentRequest(
                    ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                    state.WorkflowExecutionId,
                    serialized.SchemaVersion,
                    serialized.ContentJson,
                    envelope.Version), cancellationToken);

                if (result.Status is not (DocumentStoreWriteStatus.Saved or DocumentStoreWriteStatus.ConcurrencyConflict))
                    throw new InvalidOperationException($"Unable to backfill workflow execution history key '{state.WorkflowExecutionId}': {result.Status}.");
            }
        }
    }

    private async Task<long> CountAsync(
        DbConnection connection,
        QueryWhere where,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM groundwork_documents d WHERE {where.Sql};";
        AddParameters(command, where.Parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private QueryWhere BuildWhere(WorkflowExecutionStatePageQuery query, bool includeCursor)
    {
        var clauses = new List<string> { "d.document_kind = @documentKind" };
        var parameters = new Dictionary<string, object?>
        {
            ["documentKind"] = ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind
        };

        AddFilter(query.TenantId, "tenantId", TenantIdExpression);
        AddFilter(query.DefinitionId, "definitionId", DefinitionIdExpression);
        AddFilter(query.Status is { } status ? (int)status : null, "status", StatusExpression);
        AddFilter(query.RunKind is { } runKind ? (int)runKind : null, "runKind", RunKindExpression);
        AddFilter(query.CorrelationId, "correlationId", CorrelationIdExpression);
        AddFilter(query.WorkflowExecutionId, "workflowExecutionId", "d.id");
        AddFilter(query.ArtifactId, "artifactId", ArtifactIdExpression);

        if (query.From is { } from)
        {
            clauses.Add($"{SortTicksExpression} >= @fromTicks");
            parameters["fromTicks"] = from.UtcTicks;
        }
        if (query.To is { } to)
        {
            clauses.Add($"{SortTicksExpression} <= @toTicks");
            parameters["toTicks"] = to.UtcTicks;
        }
        if (includeCursor && query.Cursor is { } cursor)
        {
            var timestampOperator = cursor.Direction == WorkflowExecutionStatePageDirection.Next ? "<" : ">";
            var idOperator = cursor.Direction == WorkflowExecutionStatePageDirection.Next ? ">" : "<";
            clauses.Add($"({SortTicksExpression} {timestampOperator} @cursorTicks OR ({SortTicksExpression} = @cursorTicks AND d.id {idOperator} @cursorId))");
            parameters["cursorTicks"] = cursor.SortTimestamp.UtcTicks;
            parameters["cursorId"] = cursor.WorkflowExecutionId;
        }

        return new(string.Join(" AND ", clauses), parameters);

        void AddFilter(object? value, string name, string expression)
        {
            if (value is null)
                return;
            clauses.Add($"{expression} = @{name}");
            parameters[name] = value;
        }
    }

    private static DocumentEnvelope ReadEnvelope(DbDataReader reader) => new(
        ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt64(2),
        reader.GetString(3),
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);

    private static void AddParameters(DbCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var parameter in parameters)
            AddParameter(command, parameter.Key, parameter.Value);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record QueryWhere(string Sql, IReadOnlyDictionary<string, object?> Parameters);
}
