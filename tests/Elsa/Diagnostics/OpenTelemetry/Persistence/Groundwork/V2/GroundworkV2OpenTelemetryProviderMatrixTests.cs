using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
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

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2OpenTelemetryProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Ordinary_units_and_filters_round_trip_on_each_native_provider(string providerName)
    {
        var configured = Environment.GetEnvironmentVariable($"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING");
        Skip.If(string.IsNullOrWhiteSpace(configured) && !IsCi(), $"Set GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING locally, or run the matrix in CI.");
        await using var runtime = await ProviderRuntime.CreateAsync(providerName, configured);
        var path = runtime.ConnectionString;
        var connection = CreateConnection(providerName, path);
        var testScope = Guid.NewGuid().ToString("N");
        await using var store = new GroundworkOpenTelemetryStore(
            connection,
            Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 }),
            new V2OpenTelemetryBinding($"matrix-{providerName}", testScope, "otel"));
        await using var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        store.Start();
        var resource = new TelemetryResource("resource-1", "matrix-service", null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace("trace-1", "root-1", "matrix", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan("span-1", trace.TraceId, "span-1", null, resource.Id, "matrix", "server", trace.StartTime, trace.EndTime, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var point = new MetricPoint("point-1", "instrument-1", "requests", resource.Id, trace.EndTime, 1, 1, 1, new Dictionary<string, string?>(), null, null);
        var instrument = new MetricInstrument("instrument-1", resource.Id, "requests", null, null, MetricKind.Sum, new Dictionary<string, string?>());
        var log = new OtlpLogRecord("log-1", resource.Id, trace.EndTime, "Information", 9, "matrix", trace.TraceId, span.SpanId, new Dictionary<string, string?>());
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), new OpenTelemetryBatch([resource], [trace], [span], [instrument], [point], [log]));

        Assert.Single((await store.QueryTracesAsync(new OpenTelemetryTraceFilter { ServiceName = "matrix-service" })).Items);
        Assert.Single((await store.QueryMetricsAsync(new OpenTelemetryMetricFilter { ServiceName = "matrix-service" })).Points);
        Assert.Single((await store.QueryLogsAsync(new OpenTelemetryLogFilter { Search = "matrix" })).Items);
    }

    private static IStorageProviderConnection CreateConnection(string provider, string connectionString) => provider switch
    {
        "sqlite" => new SqliteProviderFactory().Create(connectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
        "mongodb" => new MongoProviderFactory().Create(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static bool IsCi() => Environment.GetEnvironmentVariable("CI") is "1" or "true";

    private sealed class ProviderRuntime(string provider, string connectionString, IAsyncDisposable? container, string? sqlitePath) : IAsyncDisposable
    {
        internal string Provider => provider;
        internal string ConnectionString => connectionString;

        internal static async Task<ProviderRuntime> CreateAsync(string provider, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return new(provider, configured, null, null);
            return provider switch
            {
                "sqlite" => CreateSqlite(),
                "postgresql" => await CreatePostgresAsync(),
                "sqlserver" => await CreateSqlServerAsync(),
                "mongodb" => await CreateMongoAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is not null && File.Exists(sqlitePath))
                File.Delete(sqlitePath);
        }

        private static ProviderRuntime CreateSqlite()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-otel-v2-{Guid.NewGuid():N}.db");
            return new("sqlite", $"Data Source={path}", null, path);
        }

        private static async Task<ProviderRuntime> CreatePostgresAsync()
        {
            if (!IsCi())
                throw new InvalidOperationException("Provider containers are enabled only in CI.");
            var container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("elsa").WithUsername("postgres").WithPassword("postgres").Build();
            await container.StartAsync();
            return new("postgresql", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateSqlServerAsync()
        {
            if (!IsCi())
                throw new InvalidOperationException("Provider containers are enabled only in CI.");
            var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            return new("sqlserver", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateMongoAsync()
        {
            if (!IsCi())
                throw new InvalidOperationException("Provider containers are enabled only in CI.");
            var container = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
            await container.StartAsync();
            var connection = container.GetConnectionString();
            var queryStart = connection.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connection : connection[..queryStart]).TrimEnd('/');
            return new("mongodb", $"{server}/elsa?replicaSet=rs0&authSource=admin&directConnection=true", container, null);
        }
    }
}
