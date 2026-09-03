using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Store;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Reads provider identity from the live provider rather than treating matrix arguments as observations.
/// The returned configuration intentionally contains only safe driver settings; connection material never
/// crosses the benchmark artifact boundary.
/// </summary>
internal static class ProviderProbe
{
    public sealed record Result(
        string Provider,
        string ConnectionType,
        string Version,
        string Topology,
        IReadOnlyDictionary<string, string> Configuration);

    public static async Task<Result> ReadAsync(
        string provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Opening the Groundwork connection proves that the adapter's selected provider package accepts
        // this connection string. Native handshakes below then establish server identity and topology.
        using var groundwork = ProviderConnections.Open(provider, connectionString);
        var connectionType = groundwork.GetType().Name;
        var expectedType = provider switch
        {
            "sqlite" => "SqliteProviderConnection",
            "postgresql" => "PostgreSqlProviderConnection",
            "sqlserver" => "SqlServerProviderConnection",
            "mongodb" => "MongoStoreConnection",
            _ => throw new PerformanceContractException($"Unsupported benchmark provider '{provider}'.")
        };
        if (!string.Equals(connectionType, expectedType, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Provider '{provider}' resolved Groundwork connection type '{connectionType}', expected '{expectedType}'.");

        return provider switch
        {
            "sqlite" => await ReadSqliteAsync(connectionString, connectionType, cancellationToken),
            "postgresql" => await ReadPostgreSqlAsync(connectionString, connectionType, cancellationToken),
            "sqlserver" => await ReadSqlServerAsync(connectionString, connectionType, cancellationToken),
            "mongodb" => await ReadMongoAsync(connectionString, connectionType, cancellationToken),
            _ => throw new PerformanceContractException($"Unsupported benchmark provider '{provider}'.")
        };
    }

    internal static string SqliteTopology(SqliteConnectionStringBuilder settings) =>
        IsMemory(settings)
            ? throw new PerformanceContractException(
                "SQLite benchmark evidence requires a file-backed database with distinct connections.")
            : "file-backed-distinct-connections";

    internal static string MongoTopology(BsonDocument hello)
    {
        var sessions = hello.TryGetValue("logicalSessionTimeoutMinutes", out var timeout) &&
            timeout.BsonType is BsonType.Int32 or BsonType.Int64 && timeout.ToInt64() > 0;
        var writable = hello.TryGetValue("isWritablePrimary", out var primary) &&
            primary.BsonType == BsonType.Boolean && primary.AsBoolean;
        var wireVersion = hello.TryGetValue("maxWireVersion", out var wire) &&
            wire.BsonType is BsonType.Int32 or BsonType.Int64 ? wire.ToInt64() : -1;

        if (sessions && writable && wireVersion >= 7 &&
            hello.TryGetValue("setName", out var setName) &&
            setName.BsonType == BsonType.String &&
            !string.IsNullOrWhiteSpace(setName.AsString))
            return "transaction-capable-replica-set";

        if (sessions && writable && wireVersion >= 8 &&
            hello.TryGetValue("msg", out var message) &&
            message.BsonType == BsonType.String &&
            string.Equals(message.AsString, "isdbgrid", StringComparison.Ordinal))
            return "transaction-capable-sharded-cluster";

        throw new PerformanceContractException(
            "MongoDB benchmark evidence requires a transaction-capable replica set or sharded cluster; " +
            "the live hello response did not prove one.");
    }

    private static async Task<Result> ReadSqliteAsync(
        string connectionString,
        string connectionType,
        CancellationToken cancellationToken)
    {
        var settings = new SqliteConnectionStringBuilder(connectionString);
        await using var connection = new SqliteConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await ScalarAsync(connection, "SELECT sqlite_version();", cancellationToken);
        var journalMode = await ScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken);
        var synchronous = await ScalarAsync(connection, "PRAGMA synchronous;", cancellationToken);

        return new Result(
            "sqlite",
            connectionType,
            version,
            SqliteTopology(settings),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = settings.Mode.ToString(),
                ["cache"] = settings.Cache.ToString(),
                ["pooling"] = settings.Pooling.ToString(),
                ["journal_mode"] = journalMode,
                ["synchronous"] = synchronous,
                ["options_digest"] = ConnectionOptionsDigest(settings.ConnectionString)
            });
    }

    private static async Task<Result> ReadPostgreSqlAsync(
        string connectionString,
        string connectionType,
        CancellationToken cancellationToken)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await ScalarAsync(connection, "SHOW server_version;", cancellationToken);

        // The wire handshake establishes product/version only. Require the independently captured
        // launcher attestation before emitting the stronger frozen container topology label.
        var containerDigest = RequireContainerAttestation("postgresql");
        return new Result(
            "postgresql",
            connectionType,
            version,
            "real-postgresql-container",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["container_image_digest"] = containerDigest,
                ["pooling"] = settings.Pooling.ToString(),
                ["multiplexing"] = settings.Multiplexing.ToString(),
                ["ssl_mode"] = settings.SslMode.ToString(),
                ["min_pool_size"] = settings.MinPoolSize.ToString(),
                ["max_pool_size"] = settings.MaxPoolSize.ToString(),
                ["timeout"] = settings.Timeout.ToString(),
                ["command_timeout"] = settings.CommandTimeout.ToString(),
                ["idle_lifetime"] = settings.ConnectionIdleLifetime.ToString(),
                ["pruning_interval"] = settings.ConnectionPruningInterval.ToString(),
                ["keep_alive"] = settings.KeepAlive.ToString(),
                ["no_reset_on_close"] = settings.NoResetOnClose.ToString(),
                ["options_digest"] = ConnectionOptionsDigest(settings.ConnectionString)
            });
    }

    private static async Task<Result> ReadSqlServerAsync(
        string connectionString,
        string connectionType,
        CancellationToken cancellationToken)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await ScalarAsync(
            connection,
            "SELECT CONVERT(varchar(128), SERVERPROPERTY('ProductVersion'));",
            cancellationToken);

        // The wire handshake establishes product/version only. Require the independently captured
        // launcher attestation before emitting the stronger frozen container topology label.
        var containerDigest = RequireContainerAttestation("sqlserver");
        return new Result(
            "sqlserver",
            connectionType,
            version,
            "real-sqlserver-container",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["container_image_digest"] = containerDigest,
                ["pooling"] = settings.Pooling.ToString(),
                ["encrypt"] = settings.Encrypt.ToString(),
                ["trust_certificate"] = settings.TrustServerCertificate.ToString(),
                ["multiple_active_result_sets"] = settings.MultipleActiveResultSets.ToString(),
                ["min_pool_size"] = settings.MinPoolSize.ToString(),
                ["max_pool_size"] = settings.MaxPoolSize.ToString(),
                ["connect_timeout"] = settings.ConnectTimeout.ToString(),
                ["load_balance_timeout"] = settings.LoadBalanceTimeout.ToString(),
                ["packet_size"] = settings.PacketSize.ToString(),
                ["options_digest"] = ConnectionOptionsDigest(settings.ConnectionString)
            });
    }

    private static async Task<Result> ReadMongoAsync(
        string connectionString,
        string connectionType,
        CancellationToken cancellationToken)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        var client = new MongoClient(settings);
        var admin = client.GetDatabase("admin");
        var hello = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument("hello", 1),
            cancellationToken: cancellationToken);
        var buildInfo = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument("buildInfo", 1),
            cancellationToken: cancellationToken);
        var version = buildInfo.TryGetValue("version", out var value) && value.BsonType == BsonType.String
            ? value.AsString
            : throw new PerformanceContractException("MongoDB hello succeeded but buildInfo did not expose a server version.");

        return new Result(
            "mongodb",
            connectionType,
            version,
            MongoTopology(hello),
            MongoConfiguration(settings, connectionString));
    }

    internal static IReadOnlyDictionary<string, string> MongoConfiguration(
        MongoClientSettings settings,
        string connectionString) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["retry_reads"] = settings.RetryReads.ToString(),
            ["retry_writes"] = settings.RetryWrites.ToString(),
            ["direct_mode"] = settings.DirectConnection.ToString(),
            ["load_balanced"] = settings.LoadBalanced.ToString(),
            ["max_pool_size"] = settings.MaxConnectionPoolSize.ToString(),
            ["min_pool_size"] = settings.MinConnectionPoolSize.ToString(),
            ["wait_queue_timeout"] = settings.WaitQueueTimeout.ToString(),
            ["connect_timeout"] = settings.ConnectTimeout.ToString(),
            ["selection_timeout"] = settings.ServerSelectionTimeout.ToString(),
            ["socket_timeout"] = settings.SocketTimeout.ToString(),
            ["heartbeat_interval"] = settings.HeartbeatInterval.ToString(),
            ["heartbeat_timeout"] = settings.HeartbeatTimeout.ToString(),
            ["use_tls"] = settings.UseTls.ToString(),
            ["tls_insecure"] = settings.AllowInsecureTls.ToString(),
            ["read_concern"] = settings.ReadConcern?.Level?.ToString() ?? "default",
            ["read_preference"] = settings.ReadPreference?.ReadPreferenceMode.ToString() ?? "default",
            ["write_concern"] = settings.WriteConcern?.W?.ToString() ?? "default",
            ["write_concern_timeout"] = settings.WriteConcern?.WTimeout?.ToString() ?? "default",
            ["options_digest"] = ConnectionOptionsDigest(connectionString)
        };

    private static bool IsMemory(SqliteConnectionStringBuilder settings) =>
        settings.Mode == SqliteOpenMode.Memory ||
        string.Equals(settings.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
        settings.DataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase) ||
        IsMemoryUri(settings.DataSource);

    private static bool IsMemoryUri(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            return false;

        var queryStart = dataSource.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return false;

        var query = Uri.UnescapeDataString(dataSource[(queryStart + 1)..]);
        return query
            .Split(['&', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Any(pair => pair.Length == 2 &&
                         string.Equals(Uri.UnescapeDataString(pair[0]).Trim(), "mode", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(Uri.UnescapeDataString(pair[1]).Trim(), "memory", StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireContainerAttestation(string provider)
    {
        return ValidateContainerAttestation(provider, Environment.GetEnvironmentVariable(AttestationVariable(provider)));
    }

    internal static string ValidateContainerAttestation(string provider, string? value)
    {
        var variable = AttestationVariable(provider);
        if (value is null ||
            !value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            value.Length != "sha256:".Length + 64 ||
            value["sha256:".Length..].Any(character => !Uri.IsHexDigit(character)))
            throw new PerformanceContractException(
                $"Provider '{provider}' requires launcher-bound container attestation in {variable} " +
                "using the form sha256:<64-hex-digest>.");

        return value.ToLowerInvariant();
    }

    private static string AttestationVariable(string provider) => provider switch
    {
        "postgresql" => "ELSA_BENCH_POSTGRES_CONTAINER_ATTESTATION",
        "sqlserver" => "ELSA_BENCH_SQLSERVER_CONTAINER_ATTESTATION",
        _ => throw new PerformanceContractException($"Provider '{provider}' does not support container attestation.")
    };

    internal static string ConnectionOptionsDigest(string canonicalConnectionString) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalConnectionString.Trim())))
            .ToLowerInvariant();

    private static async Task<string> ScalarAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
               ?? throw new PerformanceContractException($"Provider probe returned no value for '{commandText}'.");
    }
}
