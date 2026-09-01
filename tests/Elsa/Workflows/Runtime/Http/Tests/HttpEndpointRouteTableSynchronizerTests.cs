using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// Unit coverage for <see cref="HttpEndpointRouteTableSynchronizer"/> — the single serialization point every
/// route-table refresh routes through (spec 089 D review fix). Proves that two concurrent refreshes never
/// interleave (the second waits for the first to release the lock) and that a fresh scope is opened per refresh so
/// the resolver reads the durable sources anew.
/// </summary>
[Collection(RouteTableTelemetryCollection.Name)]
public sealed class HttpEndpointRouteTableSynchronizerTests
{
    private const string ActivitySourceName = "Elsa.Workflows.Runtime.Http";
    private const string ActivityName = "elsa.http.route_table.refresh";
    private const string DurationInstrumentName = "elsa.http.route_table.refresh.duration";
    private const string OutcomeTag = "elsa.activation.outcome";
    private const string RouteCountTag = "elsa.route.count";

    [Fact]
    public async Task StaticInstrumentPublicationFailureDoesNotFailSuccessfulRefresh()
    {
        var synchronizer = Build(new StaticResolver(_ => ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([])));

        await synchronizer.RefreshAsync();

        Assert.Equal(1, HttpRouteStaticTelemetryFailureProbe.FailureCount);
    }

    [Fact]
    public async Task RefreshAsync_SerializesConcurrentRefreshes_SecondWaitsForTheFirst()
    {
        // A resolver that blocks inside its first ResolveRoutesAsync until the test releases it, and signals when a
        // SECOND call is entered. If the synchronizer serialized correctly, the second RefreshAsync cannot enter the
        // resolver while the first holds the lock — so entered2 must NOT complete until the first is released.
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var resolver = new BlockingResolver(async () =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
            else
            {
                secondEntered.SetResult();
            }
        });

        var synchronizer = Build(resolver);

        var first = synchronizer.RefreshAsync().AsTask();
        await firstEntered.Task; // The first refresh is now inside the resolver, holding the lock.

        var second = synchronizer.RefreshAsync().AsTask();

        // The second must be blocked on the lock: give it a real chance to (wrongly) proceed, then assert it hasn't.
        var raced = await Task.WhenAny(secondEntered.Task, Task.Delay(200));
        Assert.NotSame(secondEntered.Task, raced);
        Assert.False(second.IsCompleted);

        // Release the first; the second is now free to run and both complete.
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        await secondEntered.Task;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshAsync_ResolvesFreshly_ReflectingRoutesAddedBetweenRefreshes()
    {
        var bindings = new Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore();
        var routeTable = new FakeRouteTable();
        var synchronizer = Synchronizers.Build(bindings, routeTable);

        await bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "first", "GET"));
        await synchronizer.RefreshAsync();
        Assert.Single(routeTable.RouteTemplates);

        await bindings.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "second", "GET"));
        await synchronizer.RefreshAsync();

        Assert.Equal(
            new[] { "first", "second" }.OrderBy(x => x),
            routeTable.RouteTemplates.OrderBy(x => x));
    }

    [Fact]
    public async Task RefreshAsync_ProjectsOnlyTheAuthoritativePublication_AcrossActivationAndCompensation()
    {
        var bindings = new Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore();
        var routeTable = new FakeRouteTable();
        var synchronizer = Synchronizers.Build(bindings, routeTable);
        var oldBinding = PublicationBinding("publication-old", "artifact-old", "foo");
        var candidateBinding = PublicationBinding("publication-new", "artifact-new", "bar");

        await bindings.PrepareActivationAsync("publication-old", [oldBinding]);
        await bindings.ActivateAsync("publication-old", replacedActivationId: null);
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);

        // Preparation is deliberately invisible until authority changes.
        await bindings.PrepareActivationAsync("publication-new", [candidateBinding]);
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);

        // Activation retires the old projection; compensation performs the inverse authority transition.
        await bindings.ActivateAsync("publication-new", "publication-old");
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "bar" }, routeTable.RouteTemplates);

        await bindings.ActivateAsync("publication-old", "publication-new");
        await synchronizer.RefreshAsync();
        Assert.Equal(new[] { "foo" }, routeTable.RouteTemplates);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesResolverFailure_AndReleasesTheLock()
    {
        // The resolver throws on the FIRST resolve, then succeeds. The synchronizer must propagate the first failure
        // AND release the lock in its finally — so the second refresh (on the same instance) runs rather than
        // deadlocking on a lock the failed refresh never released.
        var calls = 0;
        var synchronizer = Build(new BlockingResolver(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("boom");
            return Task.CompletedTask;
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await synchronizer.RefreshAsync());

        // Would hang here if the lock leaked; the test's own timeout would surface that.
        await synchronizer.RefreshAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshAsync_Success_EmitsDurationOutcomeAndRouteCount()
    {
        using var telemetry = new TelemetryCapture();
        var synchronizer = Build(new StaticResolver(_ =>
            ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([new("one"), new("two")])));

        await synchronizer.RefreshAsync();

        telemetry.AssertSingle("success", expectedRouteCount: 2);
    }

    [Fact]
    public async Task RefreshAsync_Failure_EmitsFailedDurationWithoutRouteCount()
    {
        using var telemetry = new TelemetryCapture();
        var synchronizer = Build(new StaticResolver(_ => throw new InvalidOperationException("route lookup failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await synchronizer.RefreshAsync());

        telemetry.AssertSingle("failed", expectedRouteCount: null);
    }

    [Fact]
    public async Task RefreshAsync_Cancellation_EmitsCancelledDurationWithoutRouteCount()
    {
        using var cancellation = new CancellationTokenSource();
        using var telemetry = new TelemetryCapture();
        var synchronizer = Build(new StaticResolver(token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([]);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await synchronizer.RefreshAsync(cancellation.Token));

        telemetry.AssertSingle("cancelled", expectedRouteCount: null);
    }

    [Fact]
    public async Task RefreshAsync_ThrowingActivityStoppedListenerDoesNotFailSuccessfulRefresh()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => throw new InvalidOperationException("listener failure")
        };
        ActivitySource.AddActivityListener(listener);
        var ambientActivity = Activity.Current;
        var routeTable = new FakeRouteTable();
        var synchronizer = Build(
            new StaticResolver(_ => ValueTask.FromResult<IReadOnlyCollection<HttpRouteData>>([new("one")])),
            routeTable);

        await synchronizer.RefreshAsync();

        Assert.Equal(new[] { "one" }, routeTable.RouteTemplates);
        Assert.Same(ambientActivity, Activity.Current);
    }

    private static HttpEndpointRouteTableSynchronizer Build(IHttpEndpointRoutesResolver resolver, IRouteTable? routeTable = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton(routeTable ?? new FakeRouteTable());
        var provider = services.BuildServiceProvider();
        return new HttpEndpointRouteTableSynchronizer(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static Elsa.Workflows.Runtime.Core.Models.WorkflowTriggerBinding PublicationBinding(
        string publicationId,
        string artifactId,
        string template)
    {
        var binding = Bindings.HttpEndpoint(artifactId, "node-http", template, "GET");
        return binding with
        {
            TriggerBindingId = Elsa.Workflows.Runtime.Core.Models.WorkflowTriggerBinding.BuildId(
                publicationId,
                artifactId,
                binding.ExecutableNodeId,
                binding.StimulusHash),
            ActivationId = publicationId,
            SlotId = "slot-default",
            IsActive = false
        };
    }

    /// <summary>A resolver whose <see cref="ResolveRoutesAsync"/> runs a supplied action, then returns no routes.</summary>
    private sealed class BlockingResolver(Func<Task> onResolve) : IHttpEndpointRoutesResolver
    {
        public async ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default)
        {
            await onResolve();
            return Array.Empty<HttpRouteData>();
        }
    }

    private sealed class StaticResolver(Func<CancellationToken, ValueTask<IReadOnlyCollection<HttpRouteData>>> resolve) : IHttpEndpointRoutesResolver
    {
        public ValueTask<IReadOnlyCollection<HttpRouteData>> ResolveRoutesAsync(CancellationToken cancellationToken = default) =>
            resolve(cancellationToken);
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
                ShouldListenTo = source => source.Name == ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == ActivitySourceName && instrument.Name == DurationInstrumentName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) => _measurements.Add(new(value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertSingle(string outcome, int? expectedRouteCount)
        {
            var activity = Assert.Single(_activities, activity => activity.OperationName == ActivityName);
            Assert.Equal(outcome, activity.GetTagItem(OutcomeTag));
            Assert.Equal(expectedRouteCount, activity.GetTagItem(RouteCountTag));
            Assert.All(activity.TagObjects, tag => Assert.Contains(tag.Key, new[] { OutcomeTag, RouteCountTag }));

            var measurement = Assert.Single(_measurements);
            Assert.True(measurement.Value >= 0);
            Assert.Equal(outcome, measurement.Tag(OutcomeTag));
            Assert.Null(measurement.Tag(RouteCountTag));
            Assert.Equal(new[] { OutcomeTag }, measurement.Tags.Select(tag => tag.Key));
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private sealed record Measurement(double Value, KeyValuePair<string, object?>[] Tags)
        {
            public object? Tag(string name) => Tags.SingleOrDefault(tag => tag.Key == name).Value;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RouteTableTelemetryCollection
{
    public const string Name = "Route table telemetry";
}
