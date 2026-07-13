using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

public sealed class SqliteGroundworkInitializationTests
{
    private const string DocumentKind = "probe";
    private const string IndexName = "by-name";
    private const string IndexFieldName = "name";
    private const string IndexValue = "alpha";
    private static readonly ProviderIdentity Provider = new("groundwork-sqlite", "1.0.0");

    [Fact]
    public async Task FirstInitialization_EmitsMaterializedActivityAndDuration()
    {
        var databasePath = NewDatabasePath();
        var holder = new GroundworkDocumentStoreHolder();
        using var telemetry = new TelemetryCapture();

        try
        {
            await NewInitializer(databasePath, holder).InitializeAsync();

            Assert.True(holder.IsInitialized);
            telemetry.AssertSingle(SqliteGroundworkTelemetry.MaterializedOutcome);
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task AlreadyInitializedHolder_EmitsHistoryHitActivityAndDuration()
    {
        var databasePath = NewDatabasePath();
        var holder = new GroundworkDocumentStoreHolder();
        var initializer = NewInitializer(databasePath, holder);

        try
        {
            await initializer.InitializeAsync();
            using var telemetry = new TelemetryCapture();

            await initializer.InitializeAsync();

            telemetry.AssertSingle(SqliteGroundworkTelemetry.HistoryHitOutcome);
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InitializationFailure_EmitsFailedActivityAndDuration_WithoutSensitiveTags()
    {
        await using var holder = new GroundworkDocumentStoreHolder();
        var initializer = new SqliteGroundworkDocumentStoreInitializer(
            "Data Source=:memory:;Mode=DefinitelyNotAValidMode",
            ElsaRuntimeStorageManifest.Create(),
            Provider,
            holder);
        using var telemetry = new TelemetryCapture();

        var exception = await Assert.ThrowsAsync<SqliteGroundworkInitializationException>(() => initializer.InitializeAsync());

        Assert.False(holder.IsInitialized);
        Assert.NotNull(exception.InnerException);
        Assert.Contains(ElsaRuntimeStorageManifest.Create().Identity.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Provider.Name, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", exception.Message, StringComparison.OrdinalIgnoreCase);
        telemetry.AssertSingle(SqliteGroundworkTelemetry.FailedOutcome);
    }

    [Fact]
    public async Task ExactSchemaHistory_OpensWithoutTouchingIndexProjection_AndEmitsHistoryHit()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await InstallIndexMutationTripwireAsync(databasePath);
        var holder = new GroundworkDocumentStoreHolder();

        try
        {
            using var telemetry = new TelemetryCapture();

            await NewProbeInitializer(databasePath, holder).InitializeAsync();

            Assert.NotNull(await holder.Store.LoadAsync(DocumentKind, "doc-1"));
            telemetry.AssertSingle(SqliteGroundworkTelemetry.HistoryHitOutcome);
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task MissingSchemaHistoryTable_FallsBackToMaterializationAndRebuildsProjection()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await ExecuteSqlAsync(databasePath, "DROP TABLE groundwork_schema_history; DELETE FROM groundwork_document_indexes;");

        await InitializeAndAssertProjectionAsync(databasePath, ProbeManifest(), Provider);
    }

    [Fact]
    public async Task ChangedManifest_FallsBackToMaterializationAndRebuildsProjection()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await ExecuteSqlAsync(databasePath, "DELETE FROM groundwork_document_indexes;");

        await InitializeAndAssertProjectionAsync(databasePath, ProbeManifest("2.0.0"), Provider);
    }

    [Fact]
    public async Task ChangedProvider_FallsBackToMaterializationAndRebuildsProjection()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await ExecuteSqlAsync(databasePath, "DELETE FROM groundwork_document_indexes;");

        await InitializeAndAssertProjectionAsync(databasePath, ProbeManifest(), new ProviderIdentity("groundwork-sqlite", "2.0.0"));
    }

    [Fact]
    public async Task HistoricalMatchingTuple_DoesNotBypassTheLatestMaterializedState()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await MaterializeAsync(databasePath, ProbeManifest("2.0.0"), Provider);
        await ExecuteSqlAsync(databasePath, "DELETE FROM groundwork_document_indexes;");

        await InitializeAndAssertProjectionAsync(databasePath, ProbeManifest(), Provider);
    }

    [Fact]
    public async Task ForceRematerialize_RebuildsProjectionDespiteExactHistory()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await ExecuteSqlAsync(databasePath, "DELETE FROM groundwork_document_indexes;");
        var holder = new GroundworkDocumentStoreHolder();

        try
        {
            var initializer = NewProbeInitializer(databasePath, holder, rematerializeOnStartup: true);
            await initializer.InitializeAsync();

            await AssertProjectionAsync(holder.Store);
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ConcurrentExactHistoryInitializations_OpenUsableStoresWithoutRematerialization()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        await InstallIndexMutationTripwireAsync(databasePath);
        var firstHolder = new GroundworkDocumentStoreHolder();
        var secondHolder = new GroundworkDocumentStoreHolder();

        try
        {
            await Task.WhenAll(
                NewProbeInitializer(databasePath, firstHolder).InitializeAsync(),
                NewProbeInitializer(databasePath, secondHolder).InitializeAsync());

            Assert.NotNull(await firstHolder.Store.LoadAsync(DocumentKind, "doc-1"));
            Assert.NotNull(await secondHolder.Store.LoadAsync(DocumentKind, "doc-1"));
        }
        finally
        {
            await firstHolder.DisposeAsync();
            await secondHolder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ExactHistoryOpenedStore_RemainsReadableWritableAndQueryable()
    {
        var databasePath = NewDatabasePath();
        await MaterializeBaselineAsync(databasePath);
        var holder = new GroundworkDocumentStoreHolder();

        try
        {
            await NewProbeInitializer(databasePath, holder).InitializeAsync();

            Assert.NotNull(await holder.Store.LoadAsync(DocumentKind, "doc-1"));
            await holder.Store.SaveAsync(new SaveDocumentRequest(DocumentKind, "doc-2", "1.0.0", """{"name":"beta"}"""));
            Assert.NotNull(await holder.Store.LoadAsync(DocumentKind, "doc-2"));
            Assert.Single(await holder.Store.QueryAsync(new DocumentStoreQuery(DocumentKind, IndexName, "beta")));
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"elsa-groundwork-init-{Guid.NewGuid():N}.db");

    private static SqliteGroundworkDocumentStoreInitializer NewInitializer(string databasePath, GroundworkDocumentStoreHolder holder) =>
        new($"Data Source={databasePath}", ElsaRuntimeStorageManifest.Create(), Provider, holder);

    private static SqliteGroundworkDocumentStoreInitializer NewProbeInitializer(
        string databasePath,
        GroundworkDocumentStoreHolder holder,
        bool rematerializeOnStartup = false)
    {
        if (!rematerializeOnStartup)
            return new($"Data Source={databasePath}", ProbeManifest(), Provider, holder);

        return Assert.IsType<SqliteGroundworkDocumentStoreInitializer>(Activator.CreateInstance(
            typeof(SqliteGroundworkDocumentStoreInitializer),
            $"Data Source={databasePath}",
            ProbeManifest(),
            Provider,
            holder,
            true));
    }

    private static async Task MaterializeBaselineAsync(string databasePath)
    {
        var holder = new GroundworkDocumentStoreHolder();
        try
        {
            await NewProbeInitializer(databasePath, holder).InitializeAsync();
            await holder.Store.SaveAsync(new SaveDocumentRequest(DocumentKind, "doc-1", "1.0.0", $$"""{"{{IndexFieldName}}":"{{IndexValue}}"}"""));
        }
        finally
        {
            await holder.DisposeAsync();
        }
    }

    private static async Task MaterializeAsync(string databasePath, StorageManifest manifest, ProviderIdentity provider)
    {
        await using var handle = await SqliteDocumentStoreFactory.CreateAsync(
            $"Data Source={databasePath}",
            manifest,
            provider);
    }

    private static async Task InitializeAndAssertProjectionAsync(string databasePath, StorageManifest manifest, ProviderIdentity provider)
    {
        var holder = new GroundworkDocumentStoreHolder();
        try
        {
            await new SqliteGroundworkDocumentStoreInitializer($"Data Source={databasePath}", manifest, provider, holder).InitializeAsync();
            await AssertProjectionAsync(holder.Store);
        }
        finally
        {
            await holder.DisposeAsync();
            DeleteDatabase(databasePath);
        }
    }

    private static async Task AssertProjectionAsync(IDocumentStore store) =>
        Assert.Single(await store.QueryAsync(new DocumentStoreQuery(DocumentKind, IndexName, IndexValue)));

    private static async Task InstallIndexMutationTripwireAsync(string databasePath) =>
        await ExecuteSqlAsync(databasePath, """
            CREATE TRIGGER reject_index_insert BEFORE INSERT ON groundwork_document_indexes
            BEGIN SELECT RAISE(FAIL, 'index projection was rematerialized'); END;
            CREATE TRIGGER reject_index_update BEFORE UPDATE ON groundwork_document_indexes
            BEGIN SELECT RAISE(FAIL, 'index projection was rematerialized'); END;
            CREATE TRIGGER reject_index_delete BEFORE DELETE ON groundwork_document_indexes
            BEGIN SELECT RAISE(FAIL, 'index projection was rematerialized'); END;
            """);

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static StorageManifest ProbeManifest(string version = "1.0.0") => new(
        new StorageManifestIdentity("elsa-fast-path-probe"),
        new StorageManifestOwner("elsa.persistence.groundwork.sqlite.tests"),
        new StorageManifestVersion(version),
        [
            new StorageUnit(
                new StorageUnitIdentity(DocumentKind),
                "Fast-path probe",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.None,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                [KeywordIndex()],
                [Query()],
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string> { "schema-history", "optimistic-concurrency" },
        []);

    private static IndexDeclaration KeywordIndex() => new(
        IndexName,
        [new IndexField(IndexFieldName)],
        IndexValueKind.Keyword,
        false,
        true,
        MissingValueBehavior.Excluded,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

    private static PortableQueryDeclaration Query() => new(
        "find-by-name",
        IndexName,
        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
        QuerySortSupport.None,
        QueryPagingSupport.Offset);

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly List<Activity> _activities = [];
        private readonly List<Measurement> _measurements = [];
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public TelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SqliteGroundworkTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == SqliteGroundworkTelemetry.MeterName && instrument.Name == SqliteGroundworkTelemetry.DurationInstrumentName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) => _measurements.Add(new(value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertSingle(string outcome)
        {
            var activity = Assert.Single(_activities, activity => activity.OperationName == SqliteGroundworkTelemetry.ActivityName);
            Assert.Equal(outcome, activity.GetTagItem(SqliteGroundworkTelemetry.OutcomeTag));
            Assert.Equal(new[] { SqliteGroundworkTelemetry.OutcomeTag }, activity.TagObjects.Select(tag => tag.Key));

            var measurement = Assert.Single(_measurements);
            Assert.True(measurement.Value >= 0);
            Assert.Equal(outcome, measurement.Tags.Single().Value);
            Assert.Equal(SqliteGroundworkTelemetry.OutcomeTag, measurement.Tags.Single().Key);
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private sealed record Measurement(double Value, KeyValuePair<string, object?>[] Tags);
    }
}
