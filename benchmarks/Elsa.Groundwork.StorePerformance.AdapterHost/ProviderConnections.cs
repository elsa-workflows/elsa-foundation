using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Opens a real v2 provider connection for a benchmark provider identity.
///
/// The v1 host reached for GroundworkProviderDriver in the testing library; that driver went with the v1
/// substrate in #1420. The four provider factories are the v2 seam, and they are what the four-provider
/// conformance suites use, so the benchmark measures the same drivers the conformance evidence describes.
///
/// The connection string arrives through the environment rather than argv: RunRequest values are screened
/// by ArtifactSafety, which rejects any string containing "://" or a host/port/database keyword, so a
/// connection string cannot travel as a benchmark request field.
/// </summary>
internal static class ProviderConnections
{
    public static string ConnectionEnvironmentVariable(string provider) => provider switch
    {
        "sqlite" => "ELSA_BENCH_SQLITE_CONNECTION",
        "postgresql" => "ELSA_BENCH_POSTGRES_CONNECTION",
        "sqlserver" => "ELSA_BENCH_SQLSERVER_CONNECTION",
        "mongodb" => "ELSA_BENCH_MONGO_CONNECTION",
        _ => throw new PerformanceContractException($"Unsupported benchmark provider '{provider}'.")
    };

    public static string RequireConnectionString(string provider)
    {
        var variable = ConnectionEnvironmentVariable(provider);
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new PerformanceContractException(
                $"Provider '{provider}' requires a connection string in {variable}. " +
                "A missing provider is a blocked run, never a simulated result.");
        return value;
    }

    public static IStorageProviderConnection Open(string provider, string connectionString) => provider switch
    {
        "sqlite" => new SqliteProviderFactory().Create(connectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
        "mongodb" => new MongoProviderFactory().Create(connectionString),
        _ => throw new PerformanceContractException($"Unsupported benchmark provider '{provider}'.")
    };
}
