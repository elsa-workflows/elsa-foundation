using System.Data.Common;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Documents.Store;
using Npgsql;

namespace Elsa.Persistence.Groundwork.PostgreSql;

internal sealed class PostgreSqlWorkflowExecutionStatePageQuery(
    string connectionString,
    IDocumentStore documentStore,
    IGroundworkRuntimeDocumentSerializer serializer)
    : RelationalGroundworkWorkflowExecutionStatePageQuery(documentStore, serializer)
{
    protected override DbConnection CreateConnection() => new NpgsqlConnection(connectionString);
    protected override string SortTicksExpression => "CAST(d.content_json::jsonb ->> 'historySortTicks' AS bigint)";
    protected override string TenantIdExpression => "d.content_json::jsonb #>> '{state,tenantId}'";
    protected override string DefinitionIdExpression => "COALESCE(d.content_json::jsonb #>> '{state,pinnedSource,definitionId}', d.content_json::jsonb #>> '{state,pinnedExecutable,definitionId}')";
    protected override string StatusExpression => "CAST(d.content_json::jsonb #>> '{state,status}' AS integer)";
    protected override string RunKindExpression => "CAST(d.content_json::jsonb #>> '{state,runKind}' AS integer)";
    protected override string CorrelationIdExpression => "d.content_json::jsonb #>> '{state,correlationId}'";
    protected override string ArtifactIdExpression => "d.content_json::jsonb #>> '{state,pinnedExecutable,artifactId}'";
    protected override string MissingSortTicksPredicate => "NOT (d.content_json::jsonb ? 'historySortTicks')";

    protected override IReadOnlyList<string> CreateIndexStatements =>
    [
        Index("ix_elsa_workflow_history_sort", null),
        Index("ix_elsa_workflow_history_tenant", TenantIdExpression),
        Index("ix_elsa_workflow_history_definition", DefinitionIdExpression),
        Index("ix_elsa_workflow_history_status", StatusExpression),
        Index("ix_elsa_workflow_history_run_kind", RunKindExpression),
        Index("ix_elsa_workflow_history_correlation", CorrelationIdExpression),
        Index("ix_elsa_workflow_history_artifact", ArtifactIdExpression)
    ];

    private string Index(string name, string? filterExpression)
    {
        var sortTicksExpression = SortTicksExpression.Replace("d.", string.Empty, StringComparison.Ordinal);
        var indexFilterExpression = filterExpression?.Replace("d.", string.Empty, StringComparison.Ordinal);
        var fields = indexFilterExpression is null
            ? $"({sortTicksExpression}) DESC, id ASC"
            : $"({indexFilterExpression}), ({sortTicksExpression}) DESC, id ASC";
        return $"CREATE INDEX IF NOT EXISTS {name} ON groundwork_documents ({fields}) WHERE document_kind = 'workflowExecutionState';";
    }
}
