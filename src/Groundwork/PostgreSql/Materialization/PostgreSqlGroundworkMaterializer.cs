using Groundwork.Relational.Materialization;
using Npgsql;

namespace Groundwork.PostgreSql.Materialization;

public sealed class PostgreSqlGroundworkMaterializer(NpgsqlConnection connection) : RelationalMaterializerBase(connection)
{
    protected override IReadOnlyList<string> SchemaStatements { get; } = [DocumentTableSql, IndexTableSql, SchemaHistorySql];

    protected override string InsertSchemaHistorySql => """
        INSERT INTO groundwork_schema_history
        (manifest_id, manifest_version, provider_name, provider_version, applied_utc)
        VALUES (@manifestId, @manifestVersion, @providerName, @providerVersion, @appliedUtc)
        ON CONFLICT (manifest_id, manifest_version, provider_name, provider_version) DO NOTHING;
        """;

    private const string DocumentTableSql = """
        CREATE TABLE IF NOT EXISTS groundwork_documents (
            document_kind TEXT NOT NULL,
            id TEXT NOT NULL,
            schema_version TEXT NOT NULL,
            version BIGINT NOT NULL,
            content_json TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY (document_kind, id)
        );
        """;

    private const string IndexTableSql = """
        CREATE TABLE IF NOT EXISTS groundwork_document_indexes (
            document_kind TEXT NOT NULL,
            index_name TEXT NOT NULL,
            index_value TEXT NOT NULL,
            document_id TEXT NOT NULL,
            is_unique INTEGER NOT NULL,
            PRIMARY KEY (document_kind, index_name, index_value, document_id),
            FOREIGN KEY (document_kind, document_id)
                REFERENCES groundwork_documents(document_kind, id)
                ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_groundwork_document_indexes_unique
        ON groundwork_document_indexes(document_kind, index_name, index_value)
        WHERE is_unique = 1;
        """;

    private const string SchemaHistorySql = """
        CREATE TABLE IF NOT EXISTS groundwork_schema_history (
            manifest_id TEXT NOT NULL,
            manifest_version TEXT NOT NULL,
            provider_name TEXT NOT NULL,
            provider_version TEXT NOT NULL,
            applied_utc TEXT NOT NULL,
            PRIMARY KEY (manifest_id, manifest_version, provider_name, provider_version)
        );
        """;
}
