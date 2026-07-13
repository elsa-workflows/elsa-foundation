using System.Diagnostics;
using Elsa.Primitives.Diagnostics;
using Xunit;

namespace Elsa.Primitives.Tests.Diagnostics;

public sealed class ObservationalTelemetryScopeTests
{
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

        Assert.Throws<ArgumentNullException>(() => ObservationalTelemetryScope.Start(null!, "operation"));
        Assert.ThrowsAny<ArgumentException>(() => ObservationalTelemetryScope.Start(source, " "));
        Assert.Throws<ArgumentNullException>(() => scope.Observe(null!));
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
