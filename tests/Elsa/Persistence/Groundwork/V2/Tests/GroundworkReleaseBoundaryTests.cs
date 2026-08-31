using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkReleaseBoundaryTests
{
    [Fact]
    public async Task Admission_gate_delegates_the_complete_native_async_surface()
    {
        var session = new AsyncOnlySession(Unit("elsa-release-async-gate", "payload"));
        var gate = new GroundworkStorageSessionGate();
        var key = new StorageKey(new Dictionary<string, object?> { ["id"] = "one" });
        var values = new StorageValues(new Dictionary<string, object?> { ["id"] = "one", ["payload"] = "value" });

        gate.Publish(session);
        _ = await gate.ReadAsync(key);
        _ = await gate.QueryAsync(null!);
        _ = await gate.AggregateAsync(null!);
        _ = await gate.InsertAsync(values);
        _ = await gate.UpdateAsync(values);
        _ = await gate.UpsertAsync(values);
        _ = await gate.DeleteAsync(key);
        _ = await gate.AppendAsync(new OperationId(DateTimeOffset.UnixEpoch, "async-gate"), [values]);

        Assert.Equal(8, session.AsyncCalls);
    }

    [Fact]
    public async Task Cached_session_refuses_after_same_connection_publishes_an_evolved_declaration()
    {
        using var catalog = new TemporaryCatalog();
        using var connection = new SqliteProviderFactory().Create(catalog.ConnectionString);
        using var services = Services(connection);
        var initial = Unit("elsa-release-stale-session", "payload");
        var evolved = Unit("elsa-release-stale-session", "body");
        var initialSource = Source(services, initial);
        var observer = services.GetRequiredService<ProviderCommandObserver>();
        var retained = initialSource.Open(initial.Id.Value, StorageAccess.Global);
        var key = new StorageKey(new Dictionary<string, object?> { ["id"] = "one" });

        Assert.True(retained.Insert(new StorageValues(new Dictionary<string, object?>
        {
            ["id"] = "one",
            ["payload"] = "carried"
        })).Succeeded);
        Assert.True(connection.Schema.Apply(evolved).Applied);
        var roundTripsBeforeRefusal = observer.RoundTrips;

        var failure = await Assert.ThrowsAsync<StaleStorageSessionException>(
            () => retained.ReadAsync(key).AsTask());

        Assert.Equal(StaleStorageSessionException.DiagnosticCode, failure.Code);
        Assert.Equal(initial.Id, failure.StorageUnitId);
        Assert.Equal(roundTripsBeforeRefusal, observer.RoundTrips);

        var current = Source(services, evolved).Open(evolved.Id.Value, StorageAccess.Global);
        var carried = await current.ReadAsync(key);
        Assert.Equal("carried", carried!.Values.Values["body"]);
        Assert.False(carried.Values.Values.ContainsKey("payload"));
    }

    [Fact]
    public async Task Prior_preview_catalog_is_refused_with_discard_only_boundary_and_fresh_catalog_admits()
    {
        var unit = Unit("elsa-release-catalog-boundary", "payload");
        using var priorCatalog = new TemporaryCatalog();
        using (var initialConnection = new SqliteProviderFactory().Create(priorCatalog.ConnectionString))
            Assert.True(initialConnection.Schema.Apply(unit).Applied);

        PoisonRecordedFingerprint(priorCatalog.ConnectionString);

        using (var incompatibleConnection = new SqliteProviderFactory().Create(priorCatalog.ConnectionString))
        using (var incompatibleServices = Services(incompatibleConnection))
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Source(incompatibleServices, unit).InitializeAsync());
            var boundary = Assert.IsType<GroundworkSchemaBoundaryException>(failure.InnerException);

            Assert.StartsWith(GroundworkSchemaBoundaryException.Code, boundary.Message, StringComparison.Ordinal);
            Assert.Contains("Discard that catalog", boundary.Message, StringComparison.Ordinal);
            Assert.Contains("no in-place migration", boundary.Message, StringComparison.Ordinal);
        }

        using var freshCatalog = new TemporaryCatalog();
        using var freshConnection = new SqliteProviderFactory().Create(freshCatalog.ConnectionString);
        using var freshServices = Services(freshConnection);

        await Source(freshServices, unit).InitializeAsync();
        Assert.True(freshConnection.Schema.Diff(unit).IsEmpty);
    }

    private static GroundworkStorageSessionSource Source(IServiceProvider services, StorageUnit unit)
    {
        var registry = new GroundworkStorageUnitRegistry();
        registry.Declare(unit);
        return new GroundworkStorageSessionSource(services, registry);
    }

    private static ServiceProvider Services(IStorageProviderConnection connection) =>
        new ServiceCollection()
            .AddSingleton<IStorageProviderConnection>(connection)
            .AddSingleton<ProviderCommandObserver>()
            .AddSingleton<IProviderCommandObserver>(services => services.GetRequiredService<ProviderCommandObserver>())
            .BuildServiceProvider();

    private static StorageUnit Unit(string id, string payloadName) => new()
    {
        Id = new StorageUnitId(id),
        Name = id.Replace('-', '_'),
        Columns =
        [
            new ColumnDefinition
            {
                Id = "id",
                Name = "id",
                Type = PortableType.String,
                MaxLength = 64,
                IsNullable = false
            },
            new ColumnDefinition
            {
                Id = "payload",
                Name = payloadName,
                Type = PortableType.String,
                MaxLength = 64,
                IsNullable = false
            }
        ],
        Key = new KeyDefinition { Columns = ["id"] }
    };

    private static void PoisonRecordedFingerprint(string connectionString)
    {
        const string staleFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE "__groundwork_schema_history"
               SET "state_json" = json_set("state_json", '$.targetFingerprint', @stale);
            """;
        command.Parameters.AddWithValue("@stale", staleFingerprint);
        Assert.Equal(1, command.ExecuteNonQuery());

        command.CommandText =
            """
            SELECT json_extract("state_json", '$.targetFingerprint')
              FROM "__groundwork_schema_history";
            """;
        Assert.Equal(staleFingerprint, command.ExecuteScalar());
    }

    private sealed class TemporaryCatalog : IDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(),
            $"elsa-groundwork-release-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path};Pooling=False";

        public void Dispose()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", ".schema.lock" })
                File.Delete(path + suffix);
        }
    }

    private sealed class AsyncOnlySession(StorageUnit unit) : IStorageSession
    {
        public int AsyncCalls { get; private set; }
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Global;

        public StoredEntry? Read(StorageKey key) => throw SyncCall();
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw SyncCall();
        public AggregationResult Aggregate(AggregationQuery query) => throw SyncCall();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw SyncCall();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw SyncCall();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw SyncCall();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw SyncCall();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw SyncCall();

        public ValueTask<StoredEntry?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default) =>
            Result<StoredEntry?>(null);

        public ValueTask<QueryMaterializedResult> QueryAsync(
            QueryRequest request,
            QueryRenderOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Result<QueryMaterializedResult>(null!);

        public ValueTask<AggregationResult> AggregateAsync(
            AggregationQuery query,
            CancellationToken cancellationToken = default) =>
            Result<AggregationResult>(null!);

        public ValueTask<WriteOutcome> InsertAsync(
            StorageValues values,
            WriteOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Result<WriteOutcome>(null!);

        public ValueTask<WriteOutcome> UpdateAsync(
            StorageValues values,
            WriteOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Result<WriteOutcome>(null!);

        public ValueTask<WriteOutcome> UpsertAsync(
            StorageValues values,
            WriteOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Result<WriteOutcome>(null!);

        public ValueTask<WriteOutcome> DeleteAsync(
            StorageKey key,
            WriteOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Result<WriteOutcome>(null!);

        public ValueTask<WriteOutcome> AppendAsync(
            OperationId operationId,
            IReadOnlyList<StorageValues> values,
            CancellationToken cancellationToken = default) =>
            Result<WriteOutcome>(null!);

        private ValueTask<T> Result<T>(T value)
        {
            AsyncCalls++;
            return ValueTask.FromResult(value);
        }

        private static InvalidOperationException SyncCall() =>
            new("The gate called the synchronous surface instead of the native async member.");
    }
}
