using System.Data.Common;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Microsoft.Data.SqlClient;

namespace Elsa.Persistence.Groundwork.SqlServer;

internal sealed class SqlServerWorkflowExecutionStatePageQuery(
    string connectionString,
    IGroundworkRuntimeDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkWorkflowExecutionStatePageRouteSource routeSource)
    : RelationalGroundworkWorkflowExecutionStatePageQuery(serializer, accessContextAccessor, routeSource)
{
    protected override DbConnection CreateConnection() => new SqlConnection(connectionString);
    protected override string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    protected override string SortTicksExpression =>
        $"TRY_CONVERT(bigint, JSON_VALUE({CanonicalJsonExpression}, '$.historySortTicks'))";
    protected override string TenantIdExpression => $"JSON_VALUE({CanonicalJsonExpression}, '$.state.tenantId')";
    protected override string DefinitionIdExpression =>
        $"COALESCE(JSON_VALUE({CanonicalJsonExpression}, '$.state.pinnedSource.definitionId'), JSON_VALUE({CanonicalJsonExpression}, '$.state.pinnedExecutable.definitionId'))";
    protected override string StatusExpression =>
        $"TRY_CONVERT(int, JSON_VALUE({CanonicalJsonExpression}, '$.state.status'))";
    protected override string RunKindExpression =>
        $"TRY_CONVERT(int, JSON_VALUE({CanonicalJsonExpression}, '$.state.runKind'))";
    protected override string CorrelationIdExpression => $"JSON_VALUE({CanonicalJsonExpression}, '$.state.correlationId')";
    protected override string ArtifactIdExpression =>
        $"JSON_VALUE({CanonicalJsonExpression}, '$.state.pinnedExecutable.artifactId')";

    protected override string BuildPageSelectSql(string whereSql, string orderBy) => $"""
        SELECT TOP (@pageLimit) {PageSelectColumns}
        FROM {PageTableExpression}
        WHERE {whereSql}
        ORDER BY {orderBy};
        """;
}
