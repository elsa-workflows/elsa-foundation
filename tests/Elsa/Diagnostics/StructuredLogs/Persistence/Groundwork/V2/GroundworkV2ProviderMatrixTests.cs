using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.SqlServer;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.Options;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests;

/// <summary>
/// Provider matrix proof for the v2 Structured Logs contract. Local runs are opt-in through an
/// environment connection string. The nightly integration workflow detects the Testcontainers
/// references in this project and sets CI, which provisions all native providers without secrets.
/// </summary>
public sealed class GroundworkV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Structured_logs_preserve_public_behavior_across_native_providers(string providerName)
    {
        var configuredConnection = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(configuredConnection) && !IsContinuousIntegration(),
            $"Set {EnvironmentVariable(providerName)} locally, or run the provider matrix in CI.");

        await using var runtime = await NativeProviderRuntime.CreateAsync(providerName, configuredConnection);
        var binding = new StructuredLogStoreBinding(
            $"matrix-{Guid.NewGuid():N}",
            "scope",
            "structured-logs");
        var unit = StructuredLogsGroundworkStorageSchema.CreateUnit();
        long highWater;

        using (var firstConnection = runtime.OpenConnection())
        {
            firstConnection.Schema.Apply(unit);
            await using (var firstStore = CreateStore(firstConnection, unit, binding))
            await using (var isolatedStore = CreateStore(firstConnection, unit, binding with { ScopeId = "isolated-scope" }))
            await using (var wrongScopeStore = CreateStore(firstConnection, unit, binding with { ScopeId = "wrong-scope" }))
            {
                var first = await firstStore.AppendAsync(Entry("first"));
                var second = await firstStore.AppendAsync(Entry("second"));
                var selected = await firstStore.AppendAsync(Entry("selected-old") with { SourceId = "selected" });
                for (var index = 0; index < 8; index++)
                    await firstStore.AppendAsync(Entry($"other-{index}") with { SourceId = "other" });

                var complex = await firstStore.AppendAsync(ComplexEntry());
                var filtered = await firstStore.GetRecentAsync(new StructuredLogFilter
                {
                    SourceId = "selected",
                    MaxCount = 1
                });
                Assert.Equal([selected.Message], filtered.Select(entry => entry.Message));

                var roundTripped = Assert.Single(await firstStore.GetRecentAsync(new StructuredLogFilter { MaxCount = 1 }));
                Assert.Equal(complex.Level, roundTripped.Level);
                Assert.Equal(complex.Properties, roundTripped.Properties);
                Assert.Equal(complex.Exception, roundTripped.Exception);
                var scope = Assert.Single(roundTripped.Scopes);
                Assert.Equal("checkout scope", scope.Text);
                Assert.Equal([new LogProperty("operation", "checkout")], scope.Items);

                var isolated = await isolatedStore.AppendAsync(Entry("isolated"));
                Assert.DoesNotContain(
                    "isolated",
                    (await firstStore.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
                Assert.Equal(isolated.Sequence, await isolatedStore.GetHighWaterMarkAsync());

                await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                    wrongScopeStore.ReadAfterAsync(second.ReplayCursor, StructuredLogFilter.None, 10));
                var tampered = new StructuredLogReplayCursor(second.ReplayCursor!.Value.Value + "x");
                await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                    firstStore.ReadAfterAsync(tampered, StructuredLogFilter.None, 10));

                var acknowledgementLoss = new ProviderAcknowledgementLosingSession(
                    firstConnection.OpenSession(
                        unit,
                        StorageAccess.Scoped(StructuredLogsGroundworkStorageSchema.ScopeFor(binding))));
                await using (var retryStore = CreateStore(firstConnection, unit, binding, acknowledgementLoss))
                {
                    var retried = await retryStore.AppendAsync(Entry("ack-loss") with { SourceId = "ack-loss" });
                    Assert.Equal(2, acknowledgementLoss.Calls);
                    Assert.Single(acknowledgementLoss.Operations.Distinct());
                    Assert.Equal(
                        retried.Sequence,
                        Assert.Single((await retryStore.GetRecentAsync(new StructuredLogFilter { SourceId = "ack-loss" }))).Sequence);
                }

                await firstStore.TrimAsync(0);
                highWater = await firstStore.GetHighWaterMarkAsync();
                Assert.True(highWater >= complex.Sequence);
                Assert.Empty(await firstStore.GetRecentAsync(StructuredLogFilter.None));
                Assert.Equal([isolated.Message], (await isolatedStore.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
            }
        }

        using var restartedConnection = runtime.OpenConnection();
        restartedConnection.Schema.Apply(unit);
        await using var restarted = CreateStore(restartedConnection, unit, binding);
        Assert.Equal(highWater, await restarted.GetHighWaterMarkAsync());
        var afterRestart = await restarted.AppendAsync(Entry("after-restart"));
        Assert.True(afterRestart.Sequence > highWater);
    }

    private static GroundworkStructuredLogStore CreateStore(
        IStorageProviderConnection connection,
        StorageUnit unit,
        StructuredLogStoreBinding binding,
        IStorageSession? sessionOverride = null)
    {
        var session = sessionOverride ?? connection.OpenSession(
            unit,
            StorageAccess.Scoped(StructuredLogsGroundworkStorageSchema.ScopeFor(binding)));
        var store = new GroundworkStructuredLogStore(
            session,
            Options.Create(new StructuredLogsOptions()),
            binding,
            maxRetainedEntries: 100_000,
            retentionInterval: 5_000);
        store.Start();
        return store;
    }

    private static StructuredLogEntry Entry(string message) => new()
    {
        Sequence = 999,
        Timestamp = DateTimeOffset.UtcNow,
        Level = Microsoft.Extensions.Logging.LogLevel.Information,
        Category = "V2.ProviderMatrix",
        Message = message,
        SourceId = "provider-matrix"
    };

    private static StructuredLogEntry ComplexEntry() => Entry("complex") with
    {
        Level = Microsoft.Extensions.Logging.LogLevel.Error,
        Properties = [new LogProperty("user", "alice")],
        Scopes = [new LogScope([new LogProperty("operation", "checkout")], "checkout scope")],
        Exception = new LogExceptionDetails("System.InvalidOperationException", "bad state", "at Checkout")
    };

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static bool IsContinuousIntegration() =>
        StringComparer.OrdinalIgnoreCase.Equals(Environment.GetEnvironmentVariable("CI"), "true") ||
        StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable("CI"), "1");

    private sealed class ProviderAcknowledgementLosingSession(IStorageSession inner)
        : DelegatingStorageSession(inner), IExactAppendStorageSession
    {
        private int loseAcknowledgement = 1;

        public int Calls { get; private set; }

        public List<OperationId> Operations { get; } = [];

        public AppendOutcomeReport AppendWithOutcomes(OperationId operationId, IReadOnlyList<StorageValues> values)
        {
            Calls++;
            Operations.Add(operationId);
            var result = Inner.AppendWithOutcomes(operationId, values);
            if (Interlocked.Exchange(ref loseAcknowledgement, 0) == 1)
                throw new IOException("The provider committed but the acknowledgement was lost.");
            return result;
        }
    }

    private sealed class NativeProviderRuntime(
        string providerName,
        string connectionString,
        IAsyncDisposable? container,
        string? sqlitePath) : IAsyncDisposable
    {
        public static async Task<NativeProviderRuntime> CreateAsync(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null, null);

            if (!IsContinuousIntegration())
                throw new InvalidOperationException("Native provider provisioning is only enabled in CI.");

            return providerName switch
            {
                "sqlite" => CreateSqliteRuntime(),
                "postgresql" => await CreatePostgreSqlRuntimeAsync(),
                "sqlserver" => await CreateSqlServerRuntimeAsync(),
                "mongodb" => await CreateMongoRuntimeAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        public IStorageProviderConnection OpenConnection() => CreateConnection(providerName, connectionString);

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is not null && File.Exists(sqlitePath))
                File.Delete(sqlitePath);
        }

        private static NativeProviderRuntime CreateSqliteRuntime()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-v2-matrix-{Guid.NewGuid():N}.db");
            return new("sqlite", $"Data Source={path}", null, path);
        }

        private static async Task<NativeProviderRuntime> CreatePostgreSqlRuntimeAsync()
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            return new("postgresql", container.GetConnectionString(), container, null);
        }

        private static async Task<NativeProviderRuntime> CreateSqlServerRuntimeAsync()
        {
            var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            return new("sqlserver", container.GetConnectionString(), container, null);
        }

        private static async Task<NativeProviderRuntime> CreateMongoRuntimeAsync()
        {
            var container = new MongoDbBuilder("mongo:7.0.37")
                .WithReplicaSet("rs0")
                .Build();
            await container.StartAsync();
            var connectionString = container.GetConnectionString();
            var queryStart = connectionString.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connectionString : connectionString[..queryStart]).TrimEnd('/');
            return new("mongodb", $"{server}/elsa?replicaSet=rs0&authSource=admin&directConnection=true", container, null);
        }
    }
}
