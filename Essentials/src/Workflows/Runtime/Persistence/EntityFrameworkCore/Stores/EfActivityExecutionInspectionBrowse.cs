using System.Data;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfActivityExecutionInspectionStore
{
    public bool SupportsExecutionBrowsing => true;

    public async ValueTask<ActivityExecutionBrowsePage> BrowseAsync(ActivityExecutionBrowseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateWorkflow(query.WorkflowExecutionId);
        var scope = ActivityExecutionEfSupport.RequireScope(accessContextAccessor);
        var callerTransaction = context.Database.CurrentTransaction;
        if (System.Transactions.Transaction.Current is not null || context.Database.GetEnlistedTransaction() is not null)
            throw new InvalidOperationException("Activity browsing does not support ambient or externally enlisted transactions.");
        if (callerTransaction is not null && callerTransaction.GetDbTransaction().IsolationLevel is not (IsolationLevel.Serializable or IsolationLevel.Snapshot))
            throw new InvalidOperationException("Activity browsing requires a Serializable or Snapshot caller transaction.");

        return await RuntimeActivityExecutionEfPersistenceBoundary.QueryAsync(context, "browsing", query.WorkflowExecutionId, async () =>
        {
            await using var transaction = callerTransaction is null
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            var indexes = new List<(string Id, long Position, string? Previous, string? Next, long? Iteration)>();
            long total = 0, offset = 0;
            await using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
                command.CommandText = BuildBrowseSql(context.Database.ProviderName!);
                void Parameter(string name, object? value)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.DbType = name is "@offset" or "@limit" ? DbType.Int64 : DbType.String;
                    parameter.Value = value ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }
                Parameter("@scope", ActivityExecutionEfSupport.Hash(scope));
                Parameter("@workflow", ActivityExecutionEfSupport.Hash(query.WorkflowExecutionId));
                Parameter("@node", query.ExecutableNodeId is null ? null : ActivityExecutionEfSupport.Encode(query.ExecutableNodeId));
                Parameter("@parent", query.ParentActivityExecutionId is null ? null : ActivityExecutionEfSupport.Encode(query.ParentActivityExecutionId));
                Parameter("@ancestor", query.AncestorActivityExecutionId is null ? null : ActivityExecutionEfSupport.Encode(query.AncestorActivityExecutionId));
                Parameter("@anchor", query.AnchorActivityExecutionId is null ? null : ActivityExecutionEfSupport.Encode(query.AnchorActivityExecutionId));
                Parameter("@offset", query.Offset);
                Parameter("@limit", query.Limit);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    total = Convert.ToInt64(reader.GetValue(0));
                    offset = Convert.ToInt64(reader.GetValue(1));
                    if (!reader.IsDBNull(2)) indexes.Add((reader.GetString(2), Convert.ToInt64(reader.GetValue(3)),
                        reader.IsDBNull(4) ? null : ActivityExecutionEfSupport.Decode(reader.GetString(4)),
                        reader.IsDBNull(5) ? null : ActivityExecutionEfSupport.Decode(reader.GetString(5)),
                        reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6))));
                }
            }
            var ids = indexes.Select(x => x.Id).ToArray();
            var rows = await context.ActivityExecutionInspections.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(cancellationToken);
            var byId = rows.ToDictionary(x => x.Id, x => ReadChecked(x, scope, query.WorkflowExecutionId));
            var entries = indexes.Select(x => new ActivityExecutionBrowseEntry(
                ActivityExecutionInspectionSummaryProjection.FromProjection(byId[x.Id]), x.Position, x.Previous, x.Next, x.Iteration)).ToArray();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new ActivityExecutionBrowsePage(total, offset, entries);
        });
    }

    // Provider-owned read SQL: identifiers are constants, every caller value is a parameter. JSON is read in the
    // database so old committed rows need neither a migration nor a run-wide payload download/backfill.
    internal static string BuildBrowseSql(string provider)
    {
        var sqlServer = provider == RuntimeSqlServerDbContext.ExpectedProviderName;
        var mySql = provider == RuntimeMySqlDbContext.ExpectedProviderName;
        var postgres = provider == RuntimePostgreSqlDbContext.ExpectedProviderName;
        if (!sqlServer && !mySql && !postgres && provider != RuntimeSqliteDbContext.ExpectedProviderName)
            throw new NotSupportedException("Activity browsing is unavailable for this EF provider.");
        string Q(string name) => mySql ? $"`{name}`" : sqlServer ? $"[{name}]" : $"\"{name}\"";
        string Json(string path) => postgres
            ? $"CAST({Q("ContentJson")} AS jsonb)#>>'{{{path.Replace(".", ",")}}}'"
            : sqlServer ? $"JSON_VALUE({Q("ContentJson")}, '$.{path}') COLLATE Latin1_General_100_BIN2"
            : mySql ? $"NULLIF(JSON_UNQUOTE(JSON_EXTRACT({Q("ContentJson")}, '$.{path}')), 'null') COLLATE utf8mb4_bin"
            : $"json_extract({Q("ContentJson")}, '$.{path}')";
        var pathType = sqlServer ? "varchar(max)" : mySql ? "CHAR(65535)" : "TEXT";
        var append = sqlServer || mySql ? "CONCAT(t.visited, c.hash, '|')" : "t.visited || c.hash || '|'";
        var seen = sqlServer ? "CHARINDEX(c.hash, t.visited)" : mySql ? "LOCATE(c.hash, t.visited)" : postgres ? "POSITION(c.hash IN t.visited)" : "instr(t.visited, c.hash)";
        return $"""
            WITH {(sqlServer ? "" : "RECURSIVE ")}evidence AS (
                SELECT {Q("Id")} AS id, {Q("ActivityExecutionId")} AS execution,
                    {Q("ActivityExecutionIdHash")} AS hash, {Q("ActivityExecutionIdOrderKey")} AS ordering,
                    {Q("SummaryExecutionSequence")} AS sequence, {Q("SummaryScheduledAtUtcTicks")} AS ticks,
                    {Json("activityExecutionId")} AS identity_value,
                    {Json("executableNodeId")} AS node_value,
                    {Json("provenance.parentActivityExecutionId")} AS parent_value,
                    {Json("provenance.iterationId")} AS iteration_value
                FROM {Q(RuntimeActivityExecutionEfModule.ActivityExecutionInspectionTableName)}
                WHERE {Q("ScopeKeyHash")} = @scope AND {Q("WorkflowExecutionIdHash")} = @workflow
            ), tree(id, identity_value, visited) AS (
                SELECT id, identity_value, CAST(hash AS {pathType}) FROM evidence WHERE identity_value = @ancestor
                UNION ALL
                SELECT c.id, c.identity_value, CAST({append} AS {pathType})
                FROM evidence c JOIN tree t ON c.parent_value = t.identity_value
                WHERE {seen} = 0
            ), matched AS (
                SELECT e.* FROM evidence e
                WHERE (@node IS NULL OR node_value = @node) AND (@parent IS NULL OR parent_value = @parent)
                    AND (@ancestor IS NULL OR (identity_value <> @ancestor AND id IN (SELECT id FROM tree)))
            ), iteration_starts AS (
                SELECT node_value, parent_value, iteration_value, ticks, sequence, ordering, id,
                    ROW_NUMBER() OVER (PARTITION BY node_value, parent_value, iteration_value ORDER BY ticks, sequence, ordering, id) AS first_occurrence
                FROM matched WHERE iteration_value IS NOT NULL
            ), iterations AS (
                SELECT node_value, parent_value, iteration_value, ROW_NUMBER() OVER (
                    PARTITION BY node_value, parent_value ORDER BY ticks, sequence, ordering, id) AS iteration_position
                FROM iteration_starts WHERE first_occurrence = 1
            ), ranked AS (
                SELECT id, execution, node_value, parent_value, iteration_value, ROW_NUMBER() OVER (ORDER BY ticks, sequence, ordering, id) AS position,
                    LAG(execution) OVER (ORDER BY ticks, sequence, ordering, id) AS previous_execution,
                    LEAD(execution) OVER (ORDER BY ticks, sequence, ordering, id) AS next_execution
                FROM matched
            ), bounds AS (
                SELECT COUNT(*) AS total,
                    COALESCE(@offset, CASE WHEN @anchor IS NOT NULL THEN
                        (SELECT (position - 1) - ((position - 1) % @limit) FROM ranked WHERE execution = @anchor)
                        ELSE CASE WHEN COUNT(*) > @limit THEN COUNT(*) - @limit ELSE 0 END END, 0) AS start_at
                FROM matched
            )
            SELECT b.total, b.start_at, r.id, r.position, r.previous_execution, r.next_execution, it.iteration_position
            FROM bounds b LEFT JOIN ranked r ON r.position > b.start_at AND r.position <= b.start_at + @limit
                AND (@anchor IS NULL OR EXISTS (SELECT 1 FROM ranked a WHERE a.execution = @anchor))
            LEFT JOIN iterations it ON it.node_value = r.node_value AND it.iteration_value = r.iteration_value
                AND (it.parent_value = r.parent_value OR (it.parent_value IS NULL AND r.parent_value IS NULL))
            ORDER BY r.position
            {(sqlServer ? "OPTION (MAXRECURSION 0)" : "")}
            """;
    }
}
