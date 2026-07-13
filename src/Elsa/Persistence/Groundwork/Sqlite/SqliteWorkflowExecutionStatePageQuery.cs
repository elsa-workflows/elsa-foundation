using System.Data.Common;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.Groundwork.Sqlite;

internal sealed class SqliteWorkflowExecutionStatePageQuery(
    string connectionString,
    GroundworkDocumentStoreHolder documentStoreHolder,
    IGroundworkRuntimeDocumentSerializer serializer)
    : RelationalGroundworkWorkflowExecutionStatePageQuery(documentStoreHolder, serializer)
{
    protected override DbConnection CreateConnection() => new SqliteConnection(connectionString);
    protected override string SortTicksExpression => "CAST(json_extract(d.content_json, '$.historySortTicks') AS INTEGER)";
    protected override string TenantIdExpression => "json_extract(d.content_json, '$.state.tenantId')";
    protected override string DefinitionIdExpression => "COALESCE(json_extract(d.content_json, '$.state.pinnedSource.definitionId'), json_extract(d.content_json, '$.state.pinnedExecutable.definitionId'))";
    protected override string StatusExpression => "CAST(json_extract(d.content_json, '$.state.status') AS INTEGER)";
    protected override string RunKindExpression => "CAST(json_extract(d.content_json, '$.state.runKind') AS INTEGER)";
    protected override string CorrelationIdExpression => "json_extract(d.content_json, '$.state.correlationId')";
    protected override string ArtifactIdExpression => "json_extract(d.content_json, '$.state.pinnedExecutable.artifactId')";
    protected override string MissingSortTicksPredicate => "json_type(d.content_json, '$.historySortTicks') IS NULL";

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
            ? $"{sortTicksExpression} DESC, id ASC"
            : $"{indexFilterExpression}, {sortTicksExpression} DESC, id ASC";
        return $"CREATE INDEX IF NOT EXISTS {name} ON groundwork_documents ({fields}) WHERE document_kind = 'workflowExecutionState';";
    }
}
