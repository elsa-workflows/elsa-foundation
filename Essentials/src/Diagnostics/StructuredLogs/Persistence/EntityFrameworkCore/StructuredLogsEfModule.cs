using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

public static class StructuredLogsEfModule
{
    public const string HistoryModuleName = "ElsaStructuredLogs";
    public const string RecordsTableName = "elsa_structured_log_records";
    public const string StreamStatesTableName = "elsa_structured_log_stream_states";
    public const string AppendOperationsTableName = "elsa_structured_log_append_operations";

    /// <summary>The persisted-schema version every structured-log row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "StructuredLogs";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(StructuredLogsEfModule).Assembly, SchemaFamily);
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
