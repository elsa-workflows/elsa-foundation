using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Diagnostics;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Tasks.Tests;

public sealed class StartupTaskTelemetryTests
{
    [Fact]
    public async Task SuccessfulTask_EmitsBoundedSuccessActivityAndDuration()
    {
        using var telemetry = new TelemetryCapture();

        await ExecuteAsync(new SuccessfulTask(), new AvailableLockProvider(), CancellationToken.None);

        telemetry.AssertSingle(typeof(SuccessfulTask), StartupTaskTelemetry.SuccessOutcome);
    }

    [Fact]
    public async Task SingleNodeTaskWithoutLock_EmitsSkippedActivityAndDuration()
    {
        using var telemetry = new TelemetryCapture();

        await ExecuteAsync(new SkippedTask(), new UnavailableLockProvider(), CancellationToken.None);

        telemetry.AssertSingle(typeof(SkippedTask), StartupTaskTelemetry.SkippedOutcome);
    }

    [Fact]
    public async Task CancelledTask_EmitsCancelledActivityAndDuration_AndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var telemetry = new TelemetryCapture();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteAsync(new CancellingTask(cancellation), new AvailableLockProvider(), cancellation.Token));

        telemetry.AssertSingle(typeof(CancellingTask), StartupTaskTelemetry.CancelledOutcome);
    }

    [Fact]
    public async Task FailedTask_EmitsFailedActivityAndDuration_AndPropagatesFailure()
    {
        using var telemetry = new TelemetryCapture();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExecuteAsync(new FailingTask(), new AvailableLockProvider(), CancellationToken.None));

        Assert.Equal("startup failed", exception.Message);
        telemetry.AssertSingle(typeof(FailingTask), StartupTaskTelemetry.FailedOutcome);
    }

    [Fact]
    public async Task ThrowingTelemetryListenerDoesNotFailSuccessfulTask()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == StartupTaskTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("listener failure"));
        listener.Start();

        await ExecuteAsync(new SuccessfulTask(), new AvailableLockProvider(), CancellationToken.None);
    }

    private static async Task ExecuteAsync(IStartupTask task, IDistributedLockProvider lockProvider, CancellationToken cancellationToken)
    {
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton<ITaskExecutor>(executor);
        services.AddSingleton<IBackgroundTaskStarter>(executor);
        services.AddScoped<IStartupTask>(_ => task);

        await using var provider = services.BuildServiceProvider();
        await using var manager = new TaskManager(NullLoggerFactory.Instance, provider);
        await manager.StartExecutingRegisteredTasks(cancellationToken);
    }

    private sealed class SuccessfulTask : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [SingleNodeTask]
    private sealed class SkippedTask : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A skipped task must not execute.");
    }

    private sealed class CancellingTask(CancellationTokenSource cancellation) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTask : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("startup failed");
    }

    private sealed class AvailableLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
    }

    private sealed class UnavailableLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => null;
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(null);
    }

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
                ShouldListenTo = source => source.Name == StartupTaskTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == StartupTaskTelemetry.MeterName && instrument.Name == StartupTaskTelemetry.DurationInstrumentName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) => _measurements.Add(new(value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertSingle(Type taskType, string outcome)
        {
            var activity = Assert.Single(_activities, activity => activity.OperationName == StartupTaskTelemetry.ActivityName);
            Assert.Equal(taskType.FullName, activity.GetTagItem(StartupTaskTelemetry.TaskTypeTag));
            Assert.Equal(outcome, activity.GetTagItem(StartupTaskTelemetry.OutcomeTag));
            Assert.Equal(
                new[] { StartupTaskTelemetry.OutcomeTag, StartupTaskTelemetry.TaskTypeTag },
                activity.TagObjects.Select(tag => tag.Key).OrderBy(key => key));

            var measurement = Assert.Single(_measurements);
            Assert.True(measurement.Value >= 0);
            Assert.Equal(taskType.FullName, measurement.Tag(StartupTaskTelemetry.TaskTypeTag));
            Assert.Equal(outcome, measurement.Tag(StartupTaskTelemetry.OutcomeTag));
            Assert.Equal(
                new[] { StartupTaskTelemetry.OutcomeTag, StartupTaskTelemetry.TaskTypeTag },
                measurement.Tags.Select(tag => tag.Key).OrderBy(key => key));
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private sealed record Measurement(double Value, KeyValuePair<string, object?>[] Tags)
        {
            public object? Tag(string name) => Tags.Single(tag => tag.Key == name).Value;
        }
    }
}
