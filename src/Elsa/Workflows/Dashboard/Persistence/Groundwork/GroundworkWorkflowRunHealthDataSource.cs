using System.Data;
using System.Data.Common;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

public enum GroundworkRunHealthDialect
{
    Sqlite,
    PostgreSql,
    SqlServer
}

/// <summary>
/// Exact bounded Groundwork aggregate. Filtering, incident joining, outcome grouping, current-running count and
/// top-five failures execute in the provider; only one row per requested bucket plus five definition rows crosses
/// the application boundary.
/// <para>
/// Executions live in the physical entity table the runtime manifest declares for them
/// (<see cref="ElsaRuntimeStorageManifest.WorkflowExecutionStateTableName"/>), so tenant, status and run kind
/// are projected columns; only the start timestamp and the pinned definition id are read out of the document
/// JSON. Incidents remain in the shared documents table.
/// </para>
/// </summary>
public sealed class GroundworkWorkflowRunHealthDataSource(
    Func<DbConnection> connectionFactory,
    GroundworkRunHealthDialect dialect) : IWorkflowRunHealthDataSource
{
    private const string ExecutionsTable = ElsaRuntimeStorageManifest.WorkflowExecutionStateTableName;
    private const string TenantIdColumn = ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdColumn;
    private const string StatusColumn = ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusColumn;
    private const string RunKindColumn = ElsaRuntimeStorageManifest.WorkflowExecutionHistoryRunKindColumn;
    private const string ExecutionIdColumn = ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdColumn;

    private readonly GroundworkDashboardSql _sql = new(dialect);

    public bool IsAvailable => true;

    public async ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var bucketRows = await QueryBucketsAsync(connection, request, cancellationToken);
        var running = await QueryRunningAsync(connection, request.Query, cancellationToken);
        var top = await QueryTopFailuresAsync(connection, request.Query, cancellationToken);
        var buckets = request.Buckets.Select(range => bucketRows.TryGetValue(range.Index, out var counts)
            ? counts.ToSnapshot(range)
            : new WorkflowRunHealthBucket(range.From, range.To, 0, 0, 0, 0, 0, 0, 0)).ToArray();

        return new(
            buckets.Sum(x => x.StartedCount),
            buckets.Sum(x => x.SucceededCount),
            buckets.Sum(x => x.FailedCount),
            buckets.Sum(x => x.CancelledCount),
            buckets.Sum(x => x.IncompleteCount),
            buckets.Sum(x => x.IncidentBearingRunCount),
            buckets.Sum(x => x.IncidentCount),
            running,
            buckets,
            top);
    }

    private async Task<Dictionary<int, BucketCounts>> QueryBucketsAsync(
        DbConnection connection,
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildBucketSql(request);
        AddParameter(command, "incidentKind", ElsaRuntimeStorageManifest.IncidentStateDocumentKind);
        AddCommonParameters(command, request.Query);
        foreach (var bucket in request.Buckets)
        {
            AddParameter(command, $"b{bucket.Index}From", _sql.ProviderInstant(bucket.From));
            AddParameter(command, $"b{bucket.Index}To", _sql.ProviderInstant(bucket.To));
        }

        var result = new Dictionary<int, BucketCounts>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Convert.ToInt32(reader.GetValue(0)), new(
                Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), Convert.ToInt32(reader.GetValue(4)),
                Convert.ToInt32(reader.GetValue(5)), Convert.ToInt32(reader.GetValue(6)), Convert.ToInt32(reader.GetValue(7))));
        }
        return result;
    }

    private async Task<int> QueryRunningAsync(
        DbConnection connection,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM {ExecutionsTable} e
            WHERE e.{TenantIdColumn} = @tenantId
              AND e.{RunKindColumn} {(query.IncludeTestRuns ? ">= 0" : $"<> {(int)WorkflowRunKind.TestRun}")}
              AND e.{StatusColumn} = {(int)WorkflowExecutionStatus.Running};
            """;
        AddCommonParameters(command, query);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<IReadOnlyCollection<WorkflowFailureDefinitionSnapshot>> QueryTopFailuresAsync(
        DbConnection connection,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {_sql.SelectTop(5)}{ExecutionJson("pinnedExecutable", "definitionId")} AS definition_id, COUNT(*) AS failed_count
            FROM {ExecutionsTable} e
            WHERE e.{TenantIdColumn} = @tenantId
              AND e.{RunKindColumn} {(query.IncludeTestRuns ? ">= 0" : $"<> {(int)WorkflowRunKind.TestRun}")}
              AND e.{StatusColumn} = {(int)WorkflowExecutionStatus.Faulted}
              AND {Instant(ExecutionJson("startedAt"))} >= {Instant("@from")}
              AND {Instant(ExecutionJson("startedAt"))} < {Instant("@to")}
            GROUP BY {ExecutionJson("pinnedExecutable", "definitionId")}
            ORDER BY failed_count DESC, definition_id
            {_sql.LimitClause(5)};
            """;
        AddCommonParameters(command, query);
        var result = new List<WorkflowFailureDefinitionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), Convert.ToInt32(reader.GetValue(1))));
        return result;
    }

    /// <remarks>
    /// The incidents CTE joins on <c>(execution_id, storage_scope)</c>, never on execution id alone.
    /// Execution ids are not globally unique — <c>ShortIdentityGenerator</c> emits a 42-bit timestamp plus
    /// only 22 random bits, so a same-millisecond collision is a birthday problem at roughly 2^11 ids rather
    /// than a cryptographic improbability. <c>IncidentState</c> carries no tenant of its own, but every
    /// Groundwork row does: <c>storage_scope</c> is the physical tenant partition key that
    /// <c>GroundworkScopedDocumentStore</c> writes from the ambient <c>PersistenceScope</c>, and the
    /// execution entity table carries the same envelope column. Joining on it keeps a colliding id in
    /// another tenant's partition from being counted here.
    /// </remarks>
    private string BuildBucketSql(WorkflowRunHealthDataQuery request)
    {
        var cases = string.Join(Environment.NewLine, request.Buckets.Select(bucket =>
            $"WHEN e.started_at >= {Instant($"@b{bucket.Index}From")} AND e.started_at < {Instant($"@b{bucket.Index}To")} THEN {bucket.Index}"));
        var testFilter = request.Query.IncludeTestRuns ? ">= 0" : $"<> {(int)WorkflowRunKind.TestRun}";
        return $"""
            {StatementPrefix}WITH executions AS (
                SELECT e.{GroundworkDashboardSql.Envelope.StorageScopeColumn} AS storage_scope,
                       e.{ExecutionIdColumn} AS execution_id,
                       e.{StatusColumn} AS status,
                       {Instant(ExecutionJson("startedAt"))} AS started_at
                FROM {ExecutionsTable} e
                WHERE e.{TenantIdColumn} = @tenantId
                  AND e.{RunKindColumn} {testFilter}
                  AND {ExecutionJson("startedAt")} IS NOT NULL
                  AND {Instant(ExecutionJson("startedAt"))} >= {Instant("@from")}
                  AND {Instant(ExecutionJson("startedAt"))} < {Instant("@to")}
            ), incidents AS (
                SELECT i.storage_scope AS storage_scope,
                       {IncidentJson("workflowExecutionId")} AS execution_id,
                       COUNT(*) AS incident_count
                FROM {SharedDocumentsStorage.LogicalName} i
                JOIN executions e ON e.execution_id = {IncidentJson("workflowExecutionId")}
                                 AND e.storage_scope = i.storage_scope
                WHERE i.document_kind = @incidentKind
                GROUP BY i.storage_scope, {IncidentJson("workflowExecutionId")}
            ), bucketed AS (
                SELECT CASE
                    {cases}
                END AS bucket_index,
                e.status,
                COALESCE(i.incident_count, 0) AS incident_count
                FROM executions e
                LEFT JOIN incidents i ON i.execution_id = e.execution_id
                                     AND i.storage_scope = e.storage_scope
            )
            SELECT bucket_index,
                   COUNT(*) AS started_count,
                   SUM(CASE WHEN status = {(int)WorkflowExecutionStatus.Completed} THEN 1 ELSE 0 END) AS succeeded_count,
                   SUM(CASE WHEN status = {(int)WorkflowExecutionStatus.Faulted} THEN 1 ELSE 0 END) AS failed_count,
                   SUM(CASE WHEN status = {(int)WorkflowExecutionStatus.Cancelled} THEN 1 ELSE 0 END) AS cancelled_count,
                   SUM(CASE WHEN status NOT IN ({(int)WorkflowExecutionStatus.Completed}, {(int)WorkflowExecutionStatus.Faulted}, {(int)WorkflowExecutionStatus.Cancelled}) THEN 1 ELSE 0 END) AS incomplete_count,
                   SUM(CASE WHEN incident_count > 0 THEN 1 ELSE 0 END) AS incident_bearing_count,
                   SUM(incident_count) AS incident_count
            FROM bucketed
            WHERE bucket_index IS NOT NULL
            GROUP BY bucket_index
            ORDER BY bucket_index;
            """;
    }

    /// <summary>Extracts a path under the execution document's <c>state</c> envelope member.</summary>
    private string ExecutionJson(string property, string? nested = null) =>
        _sql.JsonExtract("e", GroundworkDashboardSql.Envelope.CanonicalJsonColumn, nested is null ? ["state", property] : ["state", property, nested]);

    /// <summary>Extracts a top-level path from an incident document in the shared documents table.</summary>
    private string IncidentJson(string property) =>
        _sql.JsonExtract("i", SharedDocumentsStorage.CanonicalJsonColumnName, [property]);

    private string Instant(string expression) => _sql.Instant(expression);

    private string StatementPrefix => _sql.StatementPrefix;

    private void AddCommonParameters(DbCommand command, WorkflowRunHealthQuery query)
    {
        AddParameter(command, "tenantId", query.TenantId);
        AddParameter(command, "from", _sql.ProviderInstant(query.From));
        AddParameter(command, "to", _sql.ProviderInstant(query.To));
    }

    private static void AddParameter(DbCommand command, string name, object value) =>
        GroundworkDashboardSql.AddParameter(command, name, value);

    private sealed record BucketCounts(
        int Started,
        int Succeeded,
        int Failed,
        int Cancelled,
        int Incomplete,
        int IncidentBearing,
        int Incidents)
    {
        public WorkflowRunHealthBucket ToSnapshot(WorkflowRunHealthBucketRange range) =>
            new(range.From, range.To, Started, Succeeded, Failed, Cancelled, Incomplete, IncidentBearing, Incidents);
    }
}
