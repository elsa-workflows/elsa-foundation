using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests.Fixtures;

public enum DiagnosticsProviderKind
{
    Sqlite,
    SqlServer,
    PostgreSql,
    MongoDb
}

public enum DiagnosticsProviderCapability
{
    Relational,
    Document,
    Transactions,
    BoundedQueries,
    ExecutionPlans
}

public sealed record DiagnosticsProviderLease(
    DiagnosticsProviderKind Provider,
    string ConnectionString,
    string DatabaseName,
    IReadOnlySet<DiagnosticsProviderCapability> Capabilities,
    bool IsReady = true,
    string? Failure = null);

/// <summary>Owns real, isolated lifecycle resources for the shared four-provider diagnostics suite.</summary>
public sealed class DiagnosticsProviderFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
    private readonly PostgreSqlContainer _postgreSql = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("elsa")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly MongoDbContainer _mongoDb = new MongoDbBuilder("mongo:8.0")
        .WithReplicaSet("rs0")
        .Build();
    private readonly ConcurrentBag<string> _sqliteFiles = [];
    private bool _sqlServerStarted;
    private bool _postgreSqlStarted;
    private bool _mongoDbStarted;

    public async Task InitializeAsync()
    {
        try
        {
            _sqlServerStarted = true;
            await _sqlServer.StartAsync();
            _postgreSqlStarted = true;
            await _postgreSql.StartAsync();
            _mongoDbStarted = true;
            await _mongoDb.StartAsync();
        }
        catch
        {
            await DisposeContainersAsync();
            throw;
        }
    }

    public async Task<DiagnosticsProviderLease> CreateIsolatedAsync(
        DiagnosticsProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"elsa_diagnostics_{Guid.NewGuid():N}";
        return provider switch
        {
            DiagnosticsProviderKind.Sqlite => await CreateSqliteAsync(databaseName, cancellationToken),
            DiagnosticsProviderKind.SqlServer => await CreateSqlServerAsync(databaseName, cancellationToken),
            DiagnosticsProviderKind.PostgreSql => await CreatePostgreSqlAsync(databaseName, cancellationToken),
            DiagnosticsProviderKind.MongoDb => await CreateMongoDbAsync(databaseName, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    private async Task<DiagnosticsProviderLease> CreateSqliteAsync(string databaseName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{databaseName}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        _sqliteFiles.Add(path);
        return Lease(DiagnosticsProviderKind.Sqlite, connectionString, databaseName, relational: true);
    }

    private async Task<DiagnosticsProviderLease> CreateSqlServerAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_sqlServer.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync(cancellationToken);
        var connectionString = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString()) { InitialCatalog = databaseName }.ConnectionString;
        return Lease(DiagnosticsProviderKind.SqlServer, connectionString, databaseName, relational: true);
    }

    private async Task<DiagnosticsProviderLease> CreatePostgreSqlAsync(string databaseName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgreSql.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);
        var connectionString = new NpgsqlConnectionStringBuilder(_postgreSql.GetConnectionString()) { Database = databaseName }.ConnectionString;
        return Lease(DiagnosticsProviderKind.PostgreSql, connectionString, databaseName, relational: true);
    }

    private async Task<DiagnosticsProviderLease> CreateMongoDbAsync(string databaseName, CancellationToken cancellationToken)
    {
        var client = new MongoClient(_mongoDb.GetConnectionString());
        await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
        return Lease(DiagnosticsProviderKind.MongoDb, _mongoDb.GetConnectionString(), databaseName, relational: false);
    }

    private static DiagnosticsProviderLease Lease(
        DiagnosticsProviderKind provider,
        string connectionString,
        string databaseName,
        bool relational)
    {
        var capabilities = new HashSet<DiagnosticsProviderCapability>
        {
            DiagnosticsProviderCapability.Transactions,
            DiagnosticsProviderCapability.BoundedQueries,
            DiagnosticsProviderCapability.ExecutionPlans,
            relational ? DiagnosticsProviderCapability.Relational : DiagnosticsProviderCapability.Document
        };
        return new(provider, connectionString, databaseName, capabilities);
    }

    public async Task DisposeAsync()
    {
        await DisposeContainersAsync();
        foreach (var path in _sqliteFiles)
            File.Delete(path);
    }

    private async Task DisposeContainersAsync()
    {
        var disposals = new List<Task>();
        if (_sqlServerStarted)
            disposals.Add(_sqlServer.DisposeAsync().AsTask());
        if (_postgreSqlStarted)
            disposals.Add(_postgreSql.DisposeAsync().AsTask());
        if (_mongoDbStarted)
            disposals.Add(_mongoDb.DisposeAsync().AsTask());
        await Task.WhenAll(disposals);
        _sqlServerStarted = false;
        _postgreSqlStarted = false;
        _mongoDbStarted = false;
    }
}

[CollectionDefinition(Name)]
public sealed class DiagnosticsProviderCollection : ICollectionFixture<DiagnosticsProviderFixture>
{
    public const string Name = "diagnostics-providers";
}
