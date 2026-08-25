using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix;

internal sealed class GroundworkV2ProviderRuntime(
    string providerKey,
    string connectionString,
    IAsyncDisposable? container,
    string? sqlitePath) : IAsyncDisposable
{
    public string ProviderKey { get; } = providerKey;
    public string ConnectionString { get; } = connectionString;

    public static bool IsCi => Environment.GetEnvironmentVariable("CI") is "1" or "true";

    public static string? ConnectionEnvironmentVariable(string providerKey) => providerKey switch
    {
        "sqlite" => null,
        "postgresql" => "GROUNDWORK_POSTGRES_CONNECTION",
        "sqlserver" => "GROUNDWORK_SQLSERVER_CONNECTION",
        "mongodb" => "GROUNDWORK_MONGO_CONNECTION",
        _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, null)
    };

    public static async Task<GroundworkV2ProviderRuntime> CreateAsync(
        string providerKey,
        string? configuredConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            return new GroundworkV2ProviderRuntime(providerKey, configuredConnectionString, null, null);
        return providerKey switch
        {
            "sqlite" => CreateSqlite(),
            "postgresql" => await CreatePostgreSqlAsync(),
            "sqlserver" => await CreateSqlServerAsync(),
            "mongodb" => await CreateMongoDbAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(providerKey), providerKey, null)
        };
    }

    public IStorageProviderConnection CreateConnection() => ProviderKey switch
    {
        "sqlite" => new SqliteProviderFactory().Create(ConnectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(ConnectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(ConnectionString),
        "mongodb" => new MongoProviderFactory().Create(ConnectionString),
        _ => throw new InvalidOperationException($"Unsupported provider key '{ProviderKey}'.")
    };

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
        if (sqlitePath is null)
            return;
        foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
            if (File.Exists(path))
                File.Delete(path);
    }

    private static GroundworkV2ProviderRuntime CreateSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-identity-v2-{Guid.NewGuid():N}.db");
        return new GroundworkV2ProviderRuntime("sqlite", $"Data Source={path}", null, path);
    }

    private static async Task<GroundworkV2ProviderRuntime> CreatePostgreSqlAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("elsa")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        return new GroundworkV2ProviderRuntime("postgresql", container.GetConnectionString(), container, null);
    }

    private static async Task<GroundworkV2ProviderRuntime> CreateSqlServerAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
        await container.StartAsync();
        return new GroundworkV2ProviderRuntime("sqlserver", container.GetConnectionString(), container, null);
    }

    private static async Task<GroundworkV2ProviderRuntime> CreateMongoDbAsync()
    {
        var container = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        var queryStart = connection.IndexOf('?', StringComparison.Ordinal);
        var server = (queryStart < 0 ? connection : connection[..queryStart]).TrimEnd('/');
        return new GroundworkV2ProviderRuntime(
            "mongodb",
            $"{server}/elsa_identity_v2?replicaSet=rs0&authSource=admin&directConnection=true",
            container,
            null);
    }
}
