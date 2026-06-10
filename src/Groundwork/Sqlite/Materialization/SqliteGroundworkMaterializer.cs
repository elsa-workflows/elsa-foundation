using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Microsoft.Data.Sqlite;

namespace Groundwork.Sqlite.Materialization;

public sealed class SqliteGroundworkMaterializer(SqliteConnection connection)
{
    public async Task MaterializeAsync(StorageManifest manifest, ProviderIdentity provider, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(DocumentTableSql, transaction, cancellationToken);
        await ExecuteAsync(IndexTableSql, transaction, cancellationToken);
        await ExecuteAsync(SchemaHistorySql, transaction, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO groundwork_schema_history
            (manifest_id, manifest_version, provider_name, provider_version, applied_utc)
            VALUES ($manifestId, $manifestVersion, $providerName, $providerVersion, $appliedUtc);
            """;
        command.Parameters.AddWithValue("$manifestId", manifest.Identity.Value);
        command.Parameters.AddWithValue("$manifestVersion", manifest.Version.Value);
        command.Parameters.AddWithValue("$providerName", provider.Name);
        command.Parameters.AddWithValue("$providerVersion", provider.Version);
        command.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }

    private async Task ExecuteAsync(string sql, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string DocumentTableSql = """
        CREATE TABLE IF NOT EXISTS groundwork_documents (
            document_kind TEXT NOT NULL,
            id TEXT NOT NULL,
            schema_version TEXT NOT NULL,
            version INTEGER NOT NULL,
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
