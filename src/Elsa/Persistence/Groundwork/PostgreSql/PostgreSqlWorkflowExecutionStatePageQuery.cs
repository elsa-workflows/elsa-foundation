using System.Data.Common;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Npgsql;

namespace Elsa.Persistence.Groundwork.PostgreSql;

internal sealed class PostgreSqlWorkflowExecutionStatePageQuery(
    string connectionString,
    IGroundworkRuntimeDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkWorkflowExecutionStatePageRouteSource routeSource)
    : RelationalGroundworkWorkflowExecutionStatePageQuery(serializer, accessContextAccessor, routeSource)
{
    protected override DbConnection CreateConnection() => new NpgsqlConnection(connectionString);
    protected override string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    protected override string SortTicksExpression => $"CAST({CanonicalJsonExpression}::jsonb ->> 'historySortTicks' AS bigint)";
    protected override string TenantIdExpression => $"{CanonicalJsonExpression}::jsonb #>> '{{state,tenantId}}'";
    protected override string DefinitionIdExpression => $"COALESCE({CanonicalJsonExpression}::jsonb #>> '{{state,pinnedSource,definitionId}}', {CanonicalJsonExpression}::jsonb #>> '{{state,pinnedExecutable,definitionId}}')";
    protected override string StatusExpression => $"CAST({CanonicalJsonExpression}::jsonb #>> '{{state,status}}' AS integer)";
    protected override string RunKindExpression => $"CAST({CanonicalJsonExpression}::jsonb #>> '{{state,runKind}}' AS integer)";
    protected override string CorrelationIdExpression => $"{CanonicalJsonExpression}::jsonb #>> '{{state,correlationId}}'";
    protected override string ArtifactIdExpression => $"{CanonicalJsonExpression}::jsonb #>> '{{state,pinnedExecutable,artifactId}}'";
}
