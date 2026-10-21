using Elsa.Persistence.EntityFramework;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

internal static class EtlRuntimeEfModule
{
    public const string Name = "EtlRuntime";
    public const string HistoryModuleName = "EtlRuntime";
    public const string DefaultConnectionName = "Elsa";
    public const string DefaultSqliteConnectionString = "Data Source=elsa.db";
    public const string SchemaFamily = "EtlRuntime";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 450;
    public const int EncodedIdentityMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int HashLength = 64;
    public const int SchemaVersionMaximumLength = 32;
    public const string RunTableName = "elsa_etl_runtime_runs";
    public const string ConnectorRunTableName = "elsa_etl_connector_runs";
    public const string ConnectorPartitionTableName = "elsa_etl_connector_partitions";
    public const string ConnectorHttpPageTableName = "elsa_etl_connector_http_pages";
    public const string PreparedTableName = "elsa_etl_runtime_prepared";
    public const string PartitionTableName = "elsa_etl_runtime_partitions";
    public const string ReservationTableName = "elsa_etl_runtime_reservations";
    public const string ManifestTableName = "elsa_etl_runtime_manifest";
    public const string TerminalTableName = "elsa_etl_runtime_terminals";
    public const string OutboxTableName = "elsa_etl_runtime_outbox";

    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(EtlRuntimeEfModule).Assembly, SchemaFamily);
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
