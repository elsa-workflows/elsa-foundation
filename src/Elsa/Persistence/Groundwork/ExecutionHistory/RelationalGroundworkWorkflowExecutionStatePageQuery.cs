using System.Data.Common;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Shared relational bounded query plan. Provider projects supply JSON expressions, identifier quoting,
/// connection creation and any dialect-specific page-select shape. Startup preparation is read-only.
/// </summary>
public abstract class RelationalGroundworkWorkflowExecutionStatePageQuery
    : IGroundworkWorkflowExecutionStatePageQuery
{
    private readonly IGroundworkRuntimeDocumentSerializer serializer;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private ExecutableStorageRoute? route;
    private bool initialized;

    protected RelationalGroundworkWorkflowExecutionStatePageQuery(
        GroundworkDocumentStoreHolder documentStoreHolder,
        IGroundworkRuntimeDocumentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(documentStoreHolder);
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    protected abstract DbConnection CreateConnection();
    protected abstract string QuoteIdentifier(string identifier);
    protected abstract string SortTicksExpression { get; }
    protected abstract string TenantIdExpression { get; }
    protected abstract string DefinitionIdExpression { get; }
    protected abstract string StatusExpression { get; }
    protected abstract string RunKindExpression { get; }
    protected abstract string CorrelationIdExpression { get; }
    protected abstract string ArtifactIdExpression { get; }

    protected ExecutableStorageRoute Route => route ?? throw new InvalidOperationException(
        "Workflow execution history has not been bound to the admitted Groundwork physical route.");

    protected string CanonicalJsonExpression => Column(Route.Envelope.CanonicalJson);
    protected string DocumentIdExpression => Column(Route.Envelope.Id);
    protected string PageSelectColumns => string.Join(", ",
        Column(Route.Envelope.Id),
        Column(Route.Envelope.SchemaVersion),
        Column(Route.Envelope.Version),
        Column(Route.Envelope.CanonicalJson));
    protected string PageTableExpression => $"{QuoteIdentifier(Route.PrimaryStorage.Name.Identifier)} d";

    /// <summary>Dialect extension point; SQL Server uses TOP while SQLite/PostgreSQL use LIMIT.</summary>
    protected virtual string BuildPageSelectSql(string whereSql, string orderBy) => $"""
        SELECT {PageSelectColumns}
        FROM {PageTableExpression}
        WHERE {whereSql}
        ORDER BY {orderBy}
        LIMIT @pageLimit;
        """;

    public void Bind(ExecutableStorageRoute executableRoute)
    {
        ArgumentNullException.ThrowIfNull(executableRoute);
        if (!string.Equals(
                executableRoute.StorageUnit.Value,
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected the '{ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind}' storage route, but received '{executableRoute.StorageUnit.Value}'.",
                nameof(executableRoute));
        }

        if (route is not null && !string.Equals(route.Fingerprint, executableRoute.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Workflow execution history is already bound to a different physical route.");

        route = executableRoute;
    }

    public async ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        if (!initialized)
            throw new InvalidOperationException("Workflow execution history has not been prepared. Run the Groundwork provider startup initializer before querying history.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var filteredWhere = BuildWhere(query, includeCursor: false);
        var total = await CountAsync(connection, filteredWhere, cancellationToken);
        if (total == 0)
            return new([], null, null, false, false, 0);

        var pageWhere = BuildWhere(query, includeCursor: true);
        var reverse = query.Cursor?.Direction == WorkflowExecutionStatePageDirection.Previous;
        var order = reverse
            ? $"{SortTicksExpression} ASC, {DocumentIdExpression} DESC"
            : $"{SortTicksExpression} DESC, {DocumentIdExpression} ASC";

        await using var command = connection.CreateCommand();
        command.CommandText = BuildPageSelectSql(pageWhere.Sql, order);
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

    public async ValueTask PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
            return;

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            _ = Route;
            cancellationToken.ThrowIfCancellationRequested();
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task<long> CountAsync(
        DbConnection connection,
        QueryWhere where,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {PageTableExpression} WHERE {where.Sql};";
        AddParameters(command, where.Parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private QueryWhere BuildWhere(WorkflowExecutionStatePageQuery query, bool includeCursor)
    {
        var clauses = new List<string> { $"{Column(Route.Envelope.DocumentKind)} = @documentKind" };
        var parameters = new Dictionary<string, object?>
        {
            ["documentKind"] = ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind
        };

        AddFilter(query.TenantId, "tenantId", TenantIdExpression);
        AddFilter(query.DefinitionId, "definitionId", DefinitionIdExpression);
        AddFilter(query.Status is { } status ? (int)status : null, "status", StatusExpression);
        AddFilter(query.RunKind is { } runKind ? (int)runKind : null, "runKind", RunKindExpression);
        AddFilter(query.CorrelationId, "correlationId", CorrelationIdExpression);
        AddFilter(query.WorkflowExecutionId, "workflowExecutionId", DocumentIdExpression);
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
            clauses.Add($"({SortTicksExpression} {timestampOperator} @cursorTicks OR ({SortTicksExpression} = @cursorTicks AND {DocumentIdExpression} {idOperator} @cursorId))");
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

    private string Column(ExecutableColumnRoute column) => $"d.{QuoteIdentifier(column.Identifier)}";

    private static DocumentEnvelope ReadEnvelope(DbDataReader reader)
    {
        var documentKind = ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind;
        return new(
            documentKind,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue);
    }

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
