using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

public static class StructuredLogsEfModule
{
    public const string HistoryModuleName = "ElsaStructuredLogs";
    public const string RecordsTableName = "elsa_structured_log_records";
    public const string StreamStatesTableName = "elsa_structured_log_stream_states";
    public const string AppendOperationsTableName = "elsa_structured_log_append_operations";
    public const string DefaultConnectionName = "ElsaStructuredLogs";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-structured-logs.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
