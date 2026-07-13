using System.Data;
using System.Data.Common;
using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

public enum GroundworkRunHealthDialect
{
    Sqlite,
    PostgreSql
}

/// <summary>
/// Exact bounded Groundwork aggregate. Filtering, incident joining, outcome grouping, current-running count and
/// top-five failures execute in the provider; only one row per requested bucket plus five definition rows crosses
/// the application boundary.
/// </summary>
public sealed class GroundworkWorkflowRunHealthDataSource(
    Func<DbConnection> connectionFactory,
    GroundworkRunHealthDialect dialect) : IWorkflowRunHealthDataSource
{
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
        AddCommonParameters(command, request.Query);
        foreach (var bucket in request.Buckets)
        {
            AddParameter(command, $"b{bucket.Index}From", ProviderInstant(bucket.From));
            AddParameter(command, $"b{bucket.Index}To", ProviderInstant(bucket.To));
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
            FROM groundwork_documents e
            WHERE e.document_kind = @executionKind
              AND {Json(e: true, "tenantId")} = @tenantId
              AND {JsonInt("origin")} {(query.IncludeTestRuns ? ">= 0" : "<> 1")}
              AND {JsonInt("status")} = {(int)WorkflowExecutionStatus.Running};
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
            SELECT {Json(e: true, "pinnedExecutable", "definitionId")} AS definition_id, COUNT(*) AS failed_count
            FROM groundwork_documents e
            WHERE e.document_kind = @executionKind
              AND {Json(e: true, "tenantId")} = @tenantId
              AND {JsonInt("origin")} {(query.IncludeTestRuns ? ">= 0" : "<> 1")}
              AND {JsonInt("status")} = {(int)WorkflowExecutionStatus.Faulted}
              AND {Instant(Json(e: true, "startedAt"))} >= {Instant("@from")}
              AND {Instant(Json(e: true, "startedAt"))} < {Instant("@to")}
            GROUP BY definition_id
            ORDER BY failed_count DESC, definition_id
            LIMIT 5;
            """;
        AddCommonParameters(command, query);
        var result = new List<WorkflowFailureDefinitionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), Convert.ToInt32(reader.GetValue(1))));
        return result;
    }

    private string BuildBucketSql(WorkflowRunHealthDataQuery request)
    {
        var cases = string.Join(Environment.NewLine, request.Buckets.Select(bucket =>
            $"WHEN e.started_at >= {Instant($"@b{bucket.Index}From")} AND e.started_at < {Instant($"@b{bucket.Index}To")} THEN {bucket.Index}"));
        var testFilter = request.Query.IncludeTestRuns ? ">= 0" : "<> 1";
        return $"""
            WITH executions AS (
                SELECT {Json(e: true, "workflowExecutionId")} AS execution_id,
                       {JsonInt("status")} AS status,
                       {Instant(Json(e: true, "startedAt"))} AS started_at
                FROM groundwork_documents e
                WHERE e.document_kind = @executionKind
                  AND {Json(e: true, "tenantId")} = @tenantId
                  AND {JsonInt("origin")} {testFilter}
                  AND {Json(e: true, "startedAt")} IS NOT NULL
                  AND {Instant(Json(e: true, "startedAt"))} >= {Instant("@from")}
                  AND {Instant(Json(e: true, "startedAt"))} < {Instant("@to")}
            ), incidents AS (
                SELECT {Json(e: false, "workflowExecutionId", alias: "i")} AS execution_id, COUNT(*) AS incident_count
                FROM groundwork_documents i
                JOIN executions e ON e.execution_id = {Json(e: false, "workflowExecutionId", alias: "i")}
                WHERE i.document_kind = @incidentKind
                GROUP BY {Json(e: false, "workflowExecutionId", alias: "i")}
            ), bucketed AS (
                SELECT CASE
                    {cases}
                END AS bucket_index,
                e.status,
                COALESCE(i.incident_count, 0) AS incident_count
                FROM executions e
                LEFT JOIN incidents i ON i.execution_id = e.execution_id
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

    private string Json(bool e, string property, string? nested = null, string? alias = null)
    {
        alias ??= "e";
        var path = e
            ? nested is null ? new[] { "state", property } : new[] { "state", property, nested }
            : new[] { property };
        return dialect == GroundworkRunHealthDialect.Sqlite
            ? $"json_extract({alias}.content_json, '$.{string.Join('.', path)}')"
            : $"{alias}.content_json::jsonb #>> '{{{string.Join(',', path)}}}'";
    }

    private string JsonInt(string property) => dialect == GroundworkRunHealthDialect.Sqlite
        ? $"COALESCE(CAST({Json(e: true, property)} AS INTEGER), 0)"
        : $"COALESCE(({Json(e: true, property)})::integer, 0)";

    private string Instant(string expression) => dialect == GroundworkRunHealthDialect.Sqlite
        ? $"julianday({expression})"
        : $"({expression})::timestamptz";

    private object ProviderInstant(DateTimeOffset value) => dialect == GroundworkRunHealthDialect.Sqlite
        ? value.ToUniversalTime().ToString("O")
        : value;

    private void AddCommonParameters(DbCommand command, WorkflowRunHealthQuery query)
    {
        AddParameter(command, "executionKind", ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind);
        AddParameter(command, "incidentKind", ElsaRuntimeStorageManifest.IncidentStateDocumentKind);
        AddParameter(command, "tenantId", query.TenantId);
        AddParameter(command, "from", ProviderInstant(query.From));
        AddParameter(command, "to", ProviderInstant(query.To));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

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
