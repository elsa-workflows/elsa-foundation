using Groundwork.Relational.Materialization;
using Microsoft.Data.SqlClient;

namespace Groundwork.SqlServer.Materialization;

public sealed class SqlServerGroundworkMaterializer(SqlConnection connection) : RelationalMaterializerBase(connection)
{
    protected override IReadOnlyList<string> SchemaStatements { get; } = [DocumentTableSql, IndexTableSql, SchemaHistorySql];

    protected override string InsertSchemaHistorySql => """
        IF NOT EXISTS (
            SELECT 1
            FROM groundwork_schema_history WITH (UPDLOCK, HOLDLOCK)
            WHERE manifest_id = @manifestId
              AND manifest_version = @manifestVersion
              AND provider_name = @providerName
              AND provider_version = @providerVersion
        )
        BEGIN
            INSERT INTO groundwork_schema_history
            (manifest_id, manifest_version, provider_name, provider_version, applied_utc)
            VALUES (@manifestId, @manifestVersion, @providerName, @providerVersion, @appliedUtc);
        END;
        """;

    private const string DocumentTableSql = """
        IF OBJECT_ID(N'groundwork_documents', N'U') IS NULL
        BEGIN
            CREATE TABLE groundwork_documents (
                document_kind NVARCHAR(450) NOT NULL,
                id NVARCHAR(450) NOT NULL,
                schema_version NVARCHAR(128) NOT NULL,
                version BIGINT NOT NULL,
                content_json NVARCHAR(MAX) NOT NULL,
                created_utc NVARCHAR(64) NOT NULL,
                updated_utc NVARCHAR(64) NOT NULL,
                CONSTRAINT pk_groundwork_documents PRIMARY KEY (document_kind, id)
            );
        END;
        """;

    private const string IndexTableSql = """
        IF OBJECT_ID(N'groundwork_document_indexes', N'U') IS NULL
        BEGIN
            CREATE TABLE groundwork_document_indexes (
                document_kind NVARCHAR(450) NOT NULL,
                index_name NVARCHAR(450) NOT NULL,
                index_value NVARCHAR(450) NOT NULL,
                document_id NVARCHAR(450) NOT NULL,
                is_unique BIT NOT NULL,
                CONSTRAINT pk_groundwork_document_indexes PRIMARY KEY (document_kind, index_name, index_value, document_id),
                CONSTRAINT fk_groundwork_document_indexes_documents FOREIGN KEY (document_kind, document_id)
                    REFERENCES groundwork_documents(document_kind, id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_groundwork_document_indexes_unique' AND object_id = OBJECT_ID(N'groundwork_document_indexes'))
        BEGIN
            CREATE UNIQUE INDEX ux_groundwork_document_indexes_unique
            ON groundwork_document_indexes(document_kind, index_name, index_value)
            WHERE is_unique = 1;
        END;
        """;

    private const string SchemaHistorySql = """
        IF OBJECT_ID(N'groundwork_schema_history', N'U') IS NULL
        BEGIN
            CREATE TABLE groundwork_schema_history (
                manifest_id NVARCHAR(450) NOT NULL,
                manifest_version NVARCHAR(128) NOT NULL,
                provider_name NVARCHAR(256) NOT NULL,
                provider_version NVARCHAR(128) NOT NULL,
                applied_utc NVARCHAR(64) NOT NULL,
                CONSTRAINT pk_groundwork_schema_history PRIMARY KEY (manifest_id, manifest_version, provider_name, provider_version)
            );
        END;
        """;
}
