using System.Data;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Persists and reads the Elsa-owned <see cref="GroundworkAdmissionSkipStamp"/> in its own SQLite table,
/// separate from Groundwork's <c>groundwork_physical_schema_*</c> state and from the frozen legacy
/// <c>SchemaVersion</c> stamp (spec 133). Reading a stamp is a single indexed scalar lookup — orders of
/// magnitude cheaper than the full inspection walk it lets a warm boot skip.
/// </summary>
public sealed class SqliteGroundworkAdmissionStampStore
{
    // Own table, own name. Never groundwork_* (that namespace belongs to the package) and never
    // ElsaRuntimeStorageManifest.SchemaVersion (a frozen legacy stamp, not a skip lever).
    private const string TableName = "elsa_groundwork_admission_stamp";

    /// <summary>
    /// Reads the persisted stamp for one physical target, or null when the host has never stamped this
    /// database (fresh database, or a crash between apply and stamp write). A missing stamp table is
    /// treated as "no stamp": on a fresh database the table simply does not exist yet.
    /// </summary>
    public async Task<GroundworkAdmissionSkipStamp?> TryReadAsync(
        SqliteConnection connection,
        string manifestId,
        string providerName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await EnsureOpenAsync(connection, cancellationToken);

        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        exists.Parameters.AddWithValue("@name", TableName);
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
            return null;

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT stamp_format_version, target_fingerprint, composition_fingerprint, provider_version
            FROM {TableName}
            WHERE manifest_id = @manifestId AND provider_name = @providerName;
            """;
        command.Parameters.AddWithValue("@manifestId", manifestId);
        command.Parameters.AddWithValue("@providerName", providerName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new GroundworkAdmissionSkipStamp(
            checked((int)reader.GetInt64(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    /// <summary>
    /// Records the stamp for one physical target after a successful admission. Called only after the
    /// Groundwork apply has durably committed, so a crash before this write leaves no stamp and the next
    /// boot re-walks. The write is its own committed upsert; it never participates in the schema apply.
    /// </summary>
    public async Task WriteAsync(
        SqliteConnection connection,
        string manifestId,
        string providerName,
        GroundworkAdmissionSkipStamp stamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(stamp);
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TableName} (
                manifest_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                stamp_format_version INTEGER NOT NULL,
                target_fingerprint TEXT NOT NULL,
                composition_fingerprint TEXT NOT NULL,
                provider_version TEXT NOT NULL,
                stamped_utc TEXT NOT NULL,
                PRIMARY KEY (manifest_id, provider_name)
            );
            INSERT INTO {TableName}
                (manifest_id, provider_name, stamp_format_version, target_fingerprint, composition_fingerprint, provider_version, stamped_utc)
            VALUES (@manifestId, @providerName, @formatVersion, @targetFingerprint, @compositionFingerprint, @providerVersion, @stampedUtc)
            ON CONFLICT (manifest_id, provider_name) DO UPDATE SET
                stamp_format_version = excluded.stamp_format_version,
                target_fingerprint = excluded.target_fingerprint,
                composition_fingerprint = excluded.composition_fingerprint,
                provider_version = excluded.provider_version,
                stamped_utc = excluded.stamped_utc;
            """;
        command.Parameters.AddWithValue("@manifestId", manifestId);
        command.Parameters.AddWithValue("@providerName", providerName);
        command.Parameters.AddWithValue("@formatVersion", stamp.FormatVersion);
        command.Parameters.AddWithValue("@targetFingerprint", stamp.TargetFingerprint);
        command.Parameters.AddWithValue("@compositionFingerprint", stamp.CompositionFingerprint);
        command.Parameters.AddWithValue("@providerVersion", stamp.ProviderVersion);
        command.Parameters.AddWithValue("@stampedUtc", DateTimeOffset.UtcNow.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureOpenAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }
}
