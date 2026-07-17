using System.Data.Common;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.Groundwork.Sqlite;

internal sealed class SqliteWorkflowExecutionStatePageQuery(
    string connectionString,
    IGroundworkRuntimeDocumentSerializer serializer,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkWorkflowExecutionStatePageRouteSource routeSource)
    : RelationalGroundworkWorkflowExecutionStatePageQuery(serializer, accessContextAccessor, routeSource)
{
    protected override DbConnection CreateConnection() => new SqliteConnection(connectionString);
    protected override string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    protected override string SortTicksExpression => $"CAST(json_extract({CanonicalJsonExpression}, '$.historySortTicks') AS INTEGER)";
    protected override string TenantIdExpression => $"json_extract({CanonicalJsonExpression}, '$.state.tenantId')";
    protected override string DefinitionIdExpression => $"COALESCE(json_extract({CanonicalJsonExpression}, '$.state.pinnedSource.definitionId'), json_extract({CanonicalJsonExpression}, '$.state.pinnedExecutable.definitionId'))";
    protected override string StatusExpression => $"CAST(json_extract({CanonicalJsonExpression}, '$.state.status') AS INTEGER)";
    protected override string RunKindExpression => $"CAST(json_extract({CanonicalJsonExpression}, '$.state.runKind') AS INTEGER)";
    protected override string CorrelationIdExpression => $"json_extract({CanonicalJsonExpression}, '$.state.correlationId')";
    protected override string ArtifactIdExpression => $"json_extract({CanonicalJsonExpression}, '$.state.pinnedExecutable.artifactId')";
}
