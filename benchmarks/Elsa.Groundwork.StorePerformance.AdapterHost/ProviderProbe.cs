using System.Data.Common;
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
        if (hello.TryGetValue("setName", out var setName) &&
            setName.BsonType == BsonType.String &&
            !string.IsNullOrWhiteSpace(setName.AsString))
            return "transaction-capable-replica-set";

        if (hello.TryGetValue("msg", out var message) &&
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
                ["synchronous"] = synchronous
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

        return new Result(
            "postgresql",
            connectionType,
            version,
            "real-postgresql-container",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pooling"] = settings.Pooling.ToString(),
                ["multiplexing"] = settings.Multiplexing.ToString(),
                ["ssl_mode"] = settings.SslMode.ToString()
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

        return new Result(
            "sqlserver",
            connectionType,
            version,
            "real-sqlserver-container",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pooling"] = settings.Pooling.ToString(),
                ["encrypt"] = settings.Encrypt.ToString(),
                ["trust_server_certificate"] = settings.TrustServerCertificate.ToString(),
                ["multiple_active_result_sets"] = settings.MultipleActiveResultSets.ToString()
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
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["retry_reads"] = settings.RetryReads.ToString(),
                ["retry_writes"] = settings.RetryWrites.ToString(),
                ["direct_connection"] = settings.DirectConnection.ToString(),
                ["load_balanced"] = settings.LoadBalanced.ToString()
            });
    }

    private static bool IsMemory(SqliteConnectionStringBuilder settings) =>
        settings.Mode == SqliteOpenMode.Memory ||
        string.Equals(settings.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
        settings.DataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase);

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
