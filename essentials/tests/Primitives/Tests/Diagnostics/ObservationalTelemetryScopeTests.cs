using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.Primitives.Diagnostics;
using Xunit;

namespace Elsa.Primitives.Tests.Diagnostics;

public sealed class ObservationalTelemetryScopeTests
{
    [Fact]
    public void StartFactoryDoesNotSurfaceShouldListenToFailure()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName
                ? throw new InvalidOperationException("listener failure")
                : false,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var ambientActivity = Activity.Current;

        var scope = ObservationalTelemetryScope.Start(() => new ActivitySource(sourceName), "operation");
        scope.Dispose();

        Assert.Same(ambientActivity, Activity.Current);
    }

    [Fact]
    public void ObserveDoesNotSurfaceInstrumentPublishedFailure()
    {
        var meterName = $"test-{Guid.NewGuid():N}";
        var callbackInvoked = false;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name != meterName)
                    return;

                callbackInvoked = true;
                throw new InvalidOperationException("listener failure");
            }
        };
        listener.Start();
        using var source = NewSource();
        using var scope = ObservationalTelemetryScope.Start(source, "operation");

        scope.Observe(() =>
        {
            using var meter = new Meter(meterName);
            meter.CreateHistogram<double>("duration");
        });

        Assert.True(callbackInvoked);
    }

    [Fact]
    public void ObserveExecutesAndDoesNotSurfaceCallbackFailure()
    {
        using var source = NewSource();
        using var scope = ObservationalTelemetryScope.Start(source, "operation");
        var called = false;

        scope.Observe(() => called = true);
        scope.Observe(() => throw new InvalidOperationException("listener failure"));

        Assert.True(called);
    }

    [Fact]
    public void ResourceObservationRetriesCreationButObservesAtMostOnce()
    {
        using var source = NewSource();
        using var scope = ObservationalTelemetryScope.Start(source, "operation");
        var creationAttempts = 0;
        var persistentCreationAttempts = 0;
        var observations = 0;

        scope.Observe(
            () => ++creationAttempts == 1
                ? throw new InvalidOperationException("creation failure")
                : new object(),
            _ => observations++);
        scope.Observe<object>(
            () =>
            {
                persistentCreationAttempts++;
                throw new InvalidOperationException("persistent creation failure");
            },
            _ => observations++);
        scope.Observe(
            () => new object(),
            _ => throw new InvalidOperationException("observation failure"));

        Assert.Equal(2, creationAttempts);
        Assert.Equal(2, persistentCreationAttempts);
        Assert.Equal(1, observations);
    }

    [Fact]
    public void EmptyScopeAcceptsStatusAndTags()
    {
        using var source = NewSource();
        using var scope = ObservationalTelemetryScope.Start(source, "operation");

        scope.SetStatus(ActivityStatusCode.Error);
        scope.SetTag("outcome", "failed");
    }

    [Fact]
    public void StartDoesNotSurfaceActivityStartedListenerFailure()
    {
        using var source = NewSource();
        using var listener = ListenTo(
            source,
            activityStarted: _ => throw new InvalidOperationException("listener failure"));
        var ambientActivity = Activity.Current;

        using var scope = ObservationalTelemetryScope.Start(source, "operation");

        Assert.Same(ambientActivity, Activity.Current);
    }

    [Fact]
    public void FailedStartCleansUpWhenBothActivityLifecycleCallbacksThrow()
    {
        using var source = NewSource();
        var startedCallbacks = 0;
        var stoppedCallbacks = 0;
        using var listener = ListenTo(
            source,
            activityStarted: _ =>
            {
                startedCallbacks++;
                throw new InvalidOperationException("start listener failure");
            },
            activityStopped: _ =>
            {
                stoppedCallbacks++;
                throw new InvalidOperationException("stop listener failure");
            });
        var ambientActivity = Activity.Current;

        using var scope = ObservationalTelemetryScope.Start(source, "operation");

        Assert.Equal(1, startedCallbacks);
        Assert.Equal(1, stoppedCallbacks);
        Assert.Same(ambientActivity, Activity.Current);
    }

    [Fact]
    public void DisposeDoesNotSurfaceActivityStoppedListenerFailureAndIsIdempotent()
    {
        using var source = NewSource();
        using var listener = ListenTo(
            source,
            activityStopped: _ => throw new InvalidOperationException("listener failure"));
        var ambientActivity = Activity.Current;
        var scope = ObservationalTelemetryScope.Start(source, "operation");

        scope.Dispose();
        scope.Dispose();

        Assert.Same(ambientActivity, Activity.Current);
    }

    [Fact]
    public void NormalActivityRetainsStatusAndTags()
    {
        using var source = NewSource();
        Activity? stopped = null;
        using var listener = ListenTo(source, activityStopped: activity => stopped = activity);
        var scope = ObservationalTelemetryScope.Start(source, "operation");

        scope.SetStatus(ActivityStatusCode.Error);
        scope.SetTag("outcome", "failed");
        scope.Dispose();

        Assert.NotNull(stopped);
        Assert.Equal(ActivityStatusCode.Error, stopped.Status);
        Assert.Equal("failed", stopped.GetTagItem("outcome"));
    }

    [Fact]
    public void PublicInterfaceRejectsInvalidArguments()
    {
        using var source = NewSource();
        using var scope = ObservationalTelemetryScope.Start(source, "operation");

        Assert.Throws<ArgumentNullException>(() => ObservationalTelemetryScope.Start((ActivitySource)null!, "operation"));
        Assert.ThrowsAny<ArgumentException>(() => ObservationalTelemetryScope.Start(source, " "));
        Assert.Throws<ArgumentNullException>(() => scope.Observe(null!));
        Assert.Throws<ArgumentNullException>(() => scope.Observe<object>(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => scope.Observe(() => new object(), null!));
    }

    private static ActivitySource NewSource() => new($"test-{Guid.NewGuid():N}");

    private static ActivityListener ListenTo(
        ActivitySource source,
        Action<Activity>? activityStarted = null,
        Action<Activity>? activityStopped = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activityStarted,
            ActivityStopped = activityStopped
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
