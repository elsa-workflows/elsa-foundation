using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>Exercises the public-v2 distributed contract without the legacy Groundwork driver or document API.</summary>
public sealed class GroundworkV2DistributedProviderMatrixTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task PlacementAndAtomicCommandTransportConformAcrossNativeProviders(string providerName)
    {
        var connectionString = ConnectionString(providerName);
        Skip.If(
            providerName != "sqlite" && string.IsNullOrWhiteSpace(connectionString) && !IsContinuousIntegration(),
            $"Set {EnvironmentVariable(providerName)} locally, or run the Testcontainers integration gate.");

        await using var runtime = await NativeProviderRuntime.CreateAsync(providerName, connectionString);
        var scope = $"distributed-{Guid.NewGuid():N}";

        using (var connection = runtime.OpenConnection())
        {
            var units = DistributedGroundworkStorageManifest.CreateUnits();
            foreach (var unit in units)
                connection.Schema.Apply(unit);
            var sessions = new DistributedStoreHarness.DirectSessionSource(connection, units);
            var access = DistributedStoreHarness.Access(scope);
            var placement = new GroundworkExecutionPlacementStore(sessions, access);
            var transport = new GroundworkExecutionCommandTransport(sessions, access);

            var granted = await placement.TryClaimAsync(
                new ExecutionPlacementClaim("execution-1", "node-a", Now, Now.AddMinutes(1)),
                Now);
            var denied = await placement.TryClaimAsync(
                new ExecutionPlacementClaim("execution-1", "node-b", Now, Now.AddMinutes(1)),
                Now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, granted.Outcome);
            Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
            Assert.Equal(
                ["execution-1"],
                (await placement.ListOwnedAsync(new ExecutionPlacementLeaseListRequest("node-a", Now, 10)))
                .Select(lease => lease.WorkflowExecutionId));

            var sends = await Task.WhenAll(
                transport.SendAsync("execution-1", DistributedStoreHarness.Envelope("execution-1", "one", Now, scope), Now).AsTask(),
                transport.SendAsync("execution-1", DistributedStoreHarness.Envelope("execution-1", "two", Now, scope), Now).AsTask());
            Assert.Equal([1L, 2L], sends.Select(item => item.Sequence).Order());
            Assert.Equal(2, await transport.CountPendingAsync("execution-1"));
            Assert.Equal(["execution-1"], await transport.ListPendingExecutionIdsAsync(Now, 10));

            var leased = await transport.LeaseAsync("execution-1", "node-a", Now, TimeSpan.FromMinutes(1), 10);
            Assert.Equal([1L, 2L], leased.Select(item => item.Sequence));
            Assert.True(await transport.AckAsync(
                "execution-1",
                leased[0].TransportItemId,
                "node-a",
                leased[0].LeaseToken!.Value,
                Now.AddSeconds(1)));
            Assert.Equal(1, await transport.CountPendingAsync("execution-1"));
        }
    }

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private static string? ConnectionString(string providerName) =>
        providerName == "sqlite" ? null : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static bool IsContinuousIntegration() =>
        StringComparer.OrdinalIgnoreCase.Equals(Environment.GetEnvironmentVariable("CI"), "true") ||
        StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable("CI"), "1");

    internal sealed class NativeProviderRuntime(
        string providerName,
        string connectionString,
        IAsyncDisposable? container,
        string? sqlitePath) : IAsyncDisposable
    {
        public static async Task<NativeProviderRuntime> CreateAsync(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null, null);
            if (providerName == "sqlite")
                return CreateSqlite();
            if (!IsContinuousIntegration())
                throw new InvalidOperationException("Native provider provisioning is enabled only in CI.");

            return providerName switch
            {
                "postgresql" => await CreatePostgreSqlAsync(),
                "sqlserver" => await CreateSqlServerAsync(),
                "mongodb" => await CreateMongoAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        public IStorageProviderConnection OpenConnection() => CreateConnection(providerName, connectionString);

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is not null)
            {
                foreach (var candidate in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                {
                    if (File.Exists(candidate))
                        File.Delete(candidate);
                }
            }
        }

        private static NativeProviderRuntime CreateSqlite()
        {
            var path = Path.Join(Path.GetTempPath(), $"elsa-distributed-matrix-{Guid.NewGuid():N}.db");
            return new("sqlite", $"Data Source={path}", null, path);
        }

        private static async Task<NativeProviderRuntime> CreatePostgreSqlAsync()
        {
            var instance = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await instance.StartAsync();
            return new("postgresql", instance.GetConnectionString(), instance, null);
        }

        private static async Task<NativeProviderRuntime> CreateSqlServerAsync()
        {
            var instance = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await instance.StartAsync();
            return new("sqlserver", instance.GetConnectionString(), instance, null);
        }

        private static async Task<NativeProviderRuntime> CreateMongoAsync()
        {
            var instance = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
            await instance.StartAsync();
            return new("mongodb", BuildMongoConnectionString(instance.GetConnectionString(), "elsa"), instance, null);
        }

        internal static string BuildMongoConnectionString(string connectionString, string databaseName) =>
            new MongoUrlBuilder(connectionString)
            {
                DatabaseName = databaseName,
                ReplicaSetName = "rs0",
                AuthenticationSource = "admin",
                DirectConnection = true
            }.ToString();
    }
}
