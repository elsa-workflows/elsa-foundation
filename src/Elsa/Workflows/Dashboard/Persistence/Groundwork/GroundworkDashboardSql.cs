using System.Data.Common;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork;

/// <summary>
/// The dialect-specific SQL primitives shared by the dashboard data sources: JSON path extraction,
/// instant conversion and binding, row limiting, identifier quoting, and the statement prefix T-SQL
/// needs before a CTE. Each data source keeps its own table- and document-shape knowledge and
/// delegates the dialect rendering here.
/// </summary>
internal sealed class GroundworkDashboardSql(GroundworkRunHealthDialect dialect)
{
    /// <summary>The physical entity-table envelope every declared table shares.</summary>
    public static readonly DocumentEnvelopeDefinition Envelope = new();

    public string JsonExtract(string alias, string column, string[] path) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"json_extract({alias}.{column}, '$.{string.Join('.', path)}')",
        GroundworkRunHealthDialect.SqlServer => $"JSON_VALUE({alias}.{column}, '$.{string.Join('.', path)}')",
        _ => $"{alias}.{column}::jsonb #>> '{{{string.Join(',', path)}}}'"
    };

    public string CastInt(string expression) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"CAST({expression} AS INTEGER)",
        GroundworkRunHealthDialect.SqlServer => $"TRY_CAST({expression} AS int)",
        _ => $"({expression})::integer"
    };

    public string Instant(string expression) => dialect switch
    {
        GroundworkRunHealthDialect.Sqlite => $"julianday({expression})",
        GroundworkRunHealthDialect.SqlServer => $"CAST({expression} AS datetimeoffset)",
        _ => $"({expression})::timestamptz"
    };

    public object ProviderInstant(DateTimeOffset value) => dialect == GroundworkRunHealthDialect.Sqlite
        ? value.ToUniversalTime().ToString("O")
        : value;

    // SQL Server has no LIMIT clause; row-limiting instead lives right after SELECT as TOP (n).
    public string SelectTop(int count) => dialect == GroundworkRunHealthDialect.SqlServer ? $"TOP ({count}) " : string.Empty;

    public string LimitClause(int count) => dialect == GroundworkRunHealthDialect.SqlServer ? string.Empty : $"LIMIT {count}";

    // T-SQL requires the statement preceding a CTE to be terminated with a semicolon.
    public string StatementPrefix => dialect == GroundworkRunHealthDialect.SqlServer ? ";" : string.Empty;

    // PostgreSQL folds unquoted identifiers, so camelCase table names are addressed quoted
    // (SQL Server via brackets, which need no session setting).
    public string QuoteIdentifier(string identifier) => dialect == GroundworkRunHealthDialect.SqlServer
        ? $"[{identifier}]"
        : $"\"{identifier}\"";

    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
