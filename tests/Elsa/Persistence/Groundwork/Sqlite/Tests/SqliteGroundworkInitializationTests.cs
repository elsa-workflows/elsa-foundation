using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.Persistence.Groundwork.Scoping;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

public sealed class SqliteGroundworkInitializationTests
{
    [Fact]
    public async Task FirstInitialization_EmitsMaterializedActivityAndDuration()
    {
        var databasePath = NewDatabasePath();
        await using var provider = NewProvider($"Data Source={databasePath}");
        using var telemetry = new TelemetryCapture();

        try
        {
            await provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>().InitializeAsync();

            Assert.True(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
            telemetry.AssertSingle(SqliteGroundworkTelemetry.MaterializedOutcome);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task AlreadyInitializedProvider_EmitsHistoryHitActivityAndDuration()
    {
        var databasePath = NewDatabasePath();
        await using var provider = NewProvider($"Data Source={databasePath}");
        var initializer = provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>();

        try
        {
            await initializer.InitializeAsync();
            using var telemetry = new TelemetryCapture();

            await initializer.InitializeAsync();

            telemetry.AssertSingle(SqliteGroundworkTelemetry.HistoryHitOutcome);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InitializationFailure_EmitsFailedActivityAndDuration_WithoutSensitiveTags()
    {
        const string secret = "sqlite-admission-secret";
        await using var provider = NewProvider(
            $"Data Source=:memory:;Mode=DefinitelyNotAValidMode;Password={secret}");
        var initializer = provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>();
        using var telemetry = new TelemetryCapture();

        var exception = await Assert.ThrowsAsync<SqliteGroundworkPersistenceException>(
            () => initializer.InitializeAsync());

        Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        telemetry.AssertSingle(SqliteGroundworkTelemetry.FailedOutcome);
    }

    [Fact]
    public async Task CancelledInitialization_PropagatesCancellationAndEmitsCancelledTelemetry()
    {
        var databasePath = NewDatabasePath();
        await using var provider = NewProvider($"Data Source={databasePath}");
        using var cancellation = new CancellationTokenSource();
        using var telemetry = new TelemetryCapture();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>()
                    .InitializeAsync(cancellation.Token));

            Assert.False(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
            telemetry.AssertSingle(SqliteGroundworkTelemetry.CancelledOutcome);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ThrowingActivityStartedListenerDoesNotPreventInitialization()
    {
        var databasePath = NewDatabasePath();
        await using var provider = NewProvider($"Data Source={databasePath}");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SqliteGroundworkTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => throw new InvalidOperationException("listener failure")
        };
        ActivitySource.AddActivityListener(listener);
        var ambientActivity = Activity.Current;

        try
        {
            await provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>().InitializeAsync();

            Assert.True(provider.GetRequiredService<GroundworkStoreSessionSource>().IsInitialized);
            Assert.Same(ambientActivity, Activity.Current);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static ServiceProvider NewProvider(string connectionString)
    {
        var services = new ServiceCollection();
        new SqliteGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = connectionString,
            AutoApplySchemaOnStartup = true
        }.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"elsa-groundwork-init-{Guid.NewGuid():N}.db");

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
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == SqliteGroundworkTelemetry.MeterName &&
                        instrument.Name == SqliteGroundworkTelemetry.DurationInstrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _meterListener.SetMeasurementEventCallback<double>(
                (_, value, tags, _) => _measurements.Add(new(value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertSingle(string outcome)
        {
            var activity = Assert.Single(
                _activities,
                activity => activity.OperationName == SqliteGroundworkTelemetry.ActivityName);
            Assert.Equal(outcome, activity.GetTagItem(SqliteGroundworkTelemetry.OutcomeTag));
            Assert.Equal(
                [SqliteGroundworkTelemetry.OutcomeTag],
                activity.TagObjects.Select(tag => tag.Key));

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

        private sealed record Measurement(
            double Value,
            KeyValuePair<string, object?>[] Tags);
    }
}
