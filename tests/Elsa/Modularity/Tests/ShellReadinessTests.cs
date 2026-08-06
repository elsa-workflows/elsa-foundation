using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Workbench.Readiness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ShellReadinessTests
{
    [Fact]
    public async Task StaticActivitySourceDiscoveryFailureDoesNotPreventSuccessfulWarmup()
    {
        await using var harness = WarmupHarness.Create();

        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();
        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        Assert.Equal(1, ShellStaticTelemetryFailureProbe.FailureCount);
    }

    [Fact]
    public void OptionsDefaultToWarmingTheDefaultShell()
    {
        var options = new ShellReadinessOptions();

        Assert.Equal("Elsa:Readiness", ShellReadinessOptions.SectionName);
        Assert.True(options.WarmDefaultShell);
        Assert.Equal("default", options.DefaultShellName);
        options.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OptionsRejectAnEmptyDefaultShellName(string shellName)
    {
        var options = new ShellReadinessOptions { DefaultShellName = shellName };

        Assert.ThrowsAny<ArgumentException>(options.Validate);
    }

    [Fact]
    public void StatePublishesImmutableSuccessfulTransitionWithMonotonicDuration()
    {
        var clock = new ManualTimeProvider();
        var state = new ShellReadinessState(clock);

        Assert.Equal(ShellReadinessStatus.NotStarted, state.Snapshot.Status);
        Assert.Equal("shell_activation_not_started", state.Snapshot.Code);
        Assert.True(state.TryBegin("default"));
        Assert.False(state.TryBegin("default"));
        Assert.Equal(ShellReadinessStatus.Starting, state.Snapshot.Status);
        Assert.Equal("shell_activation_pending", state.Snapshot.Code);

        var starting = state.Snapshot;
        clock.Advance(TimeSpan.FromMilliseconds(125));
        state.MarkReady(generation: 3);

        var ready = state.Snapshot;
        Assert.NotSame(starting, ready);
        Assert.Equal(ShellReadinessStatus.Ready, ready.Status);
        Assert.Equal("default", ready.ShellName);
        Assert.Equal(1, ready.Attempt);
        Assert.Equal(3, ready.Generation);
        Assert.Equal(TimeSpan.FromMilliseconds(125), ready.Duration);
        Assert.NotNull(ready.StartedAt);
        Assert.NotNull(ready.CompletedAt);
    }

    [Fact]
    public void StatePublishesBoundedFailureAndDisabledBranches()
    {
        var state = new ShellReadinessState(TimeProvider.System);
        Assert.True(state.TryBegin("default"));

        state.MarkFailed("shell_activation_failed");

        var failed = state.Snapshot;
        Assert.Equal(ShellReadinessStatus.Failed, failed.Status);
        Assert.Equal("shell_activation_failed", failed.Code);
        Assert.Null(failed.Generation);

        var disabled = new ShellReadinessState(TimeProvider.System);
        disabled.MarkDisabled("custom-shell");
        Assert.Equal(ShellReadinessStatus.Disabled, disabled.Snapshot.Status);
        Assert.Equal("custom-shell", disabled.Snapshot.ShellName);
        Assert.Equal("shell_warmup_disabled", disabled.Snapshot.Code);
    }

    [Theory]
    [InlineData(ShellReadinessStatus.Ready)]
    [InlineData(ShellReadinessStatus.Failed)]
    [InlineData(ShellReadinessStatus.Disabled)]
    public void TerminalStateIgnoresRepeatedAndConflictingTransitions(ShellReadinessStatus terminalStatus)
    {
        var state = new ShellReadinessState(TimeProvider.System);
        if (terminalStatus == ShellReadinessStatus.Disabled)
        {
            state.MarkDisabled("default");
        }
        else
        {
            Assert.True(state.TryBegin("default"));
            if (terminalStatus == ShellReadinessStatus.Ready)
                state.MarkReady(generation: 3);
            else
                state.MarkFailed("shell_activation_failed");
        }

        var terminal = state.Snapshot;

        Assert.False(state.TryBegin("other"));
        state.MarkReady(generation: 99);
        state.MarkFailed("other_failure");
        state.MarkCancelled("other");
        state.MarkDisabled("other");

        Assert.Same(terminal, state.Snapshot);
    }

    [Fact]
    public async Task WarmupReturnsFromStartAndDoesNotActivateBeforeApplicationStarted()
    {
        await using var harness = WarmupHarness.Create();

        await harness.Warmup.StartAsync(CancellationToken.None);

        Assert.Equal(ShellReadinessStatus.NotStarted, harness.State.Snapshot.Status);
        Assert.Equal(0, harness.Gate.Attempts);

        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        Assert.Equal(ShellReadinessStatus.Starting, harness.State.Snapshot.Status);
        Assert.Equal(1, harness.Gate.Attempts);

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);
        await harness.Warmup.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedWarmupStartSchedulesOnlyOneActivation()
    {
        await using var harness = WarmupHarness.Create();

        await harness.Warmup.StartAsync(CancellationToken.None);
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        Assert.Equal(1, harness.Gate.Attempts);

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);
    }

    [Fact]
    public async Task DisabledWarmupMarksStateAndNeverActivatesTheShell()
    {
        await using var harness = WarmupHarness.Create(warmDefaultShell: false);

        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Disabled);

        Assert.Equal(ShellReadinessStatus.Disabled, harness.State.Snapshot.Status);
        Assert.Equal("shell_warmup_disabled", harness.State.Snapshot.Code);
        Assert.Equal(0, harness.Gate.Attempts);
        Assert.Null(harness.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
        await harness.Warmup.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopCancelsAndAwaitsAnInFlightWarmup()
    {
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        await harness.Warmup.StopAsync(CancellationToken.None);

        Assert.Equal(ShellReadinessStatus.Failed, harness.State.Snapshot.Status);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
        Assert.Null(harness.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
    }

    [Fact]
    public async Task StopBeforeApplicationStartedPublishesCancelledState()
    {
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);

        await harness.Warmup.StopAsync(CancellationToken.None);

        Assert.Equal(ShellReadinessStatus.Failed, harness.State.Snapshot.Status);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
        Assert.Equal(ServerReadinessFixture.DefaultShellName, harness.State.Snapshot.ShellName);
        Assert.Equal(0, harness.Gate.Attempts);
    }

    [Fact]
    public async Task SuccessfulWarmupEmitsBoundedHierarchicalActivationTelemetry()
    {
        using var telemetry = new ShellActivationTelemetryRecorder();
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        telemetry.AssertAttempt(
            ShellActivationTelemetry.SuccessOutcome,
            ShellActivationTelemetry.SuccessOutcome);
    }

    [Fact]
    public async Task FailedWarmupEmitsReachedPhasesWithoutSensitiveDimensions()
    {
        using var telemetry = new ShellActivationTelemetryRecorder();
        await using var harness = WarmupHarness.Create();
        harness.Gate.Failure = new InvalidOperationException("sensitive-test-detail");
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Failed);

        telemetry.AssertAttempt(
            ShellActivationTelemetry.FailedOutcome,
            ShellActivationTelemetry.FailedOutcome);
    }

    [Fact]
    public async Task ThrowingTelemetryListenerDoesNotPreventSuccessfulWarmup()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ShellActivationTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("listener failure"));
        listener.Start();
        await using var harness = WarmupHarness.Create();

        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();
        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        Assert.Equal(ShellReadinessStatus.Ready, harness.State.Snapshot.Status);
    }

    private static async Task WaitForStatusAsync(ShellReadinessState state, ShellReadinessStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (state.Snapshot.Status != expected)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class WarmupHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ServerReadinessFixture.RouteInitializationControl _control;

        private WarmupHarness(
            ServiceProvider provider,
            DefaultShellWarmup warmup,
            ControlledHostApplicationLifetime lifetime,
            ShellReadinessState state,
            ServerReadinessFixture.RouteInitializationControl control,
            ServerReadinessFixture.RouteInitializationControl.Gate gate)
        {
            _provider = provider;
            _control = control;
            Warmup = warmup;
            Lifetime = lifetime;
            State = state;
            Gate = gate;
            Registry = provider.GetRequiredService<IShellRegistry>();
        }

        public DefaultShellWarmup Warmup { get; }
        public ControlledHostApplicationLifetime Lifetime { get; }
        public ShellReadinessState State { get; }
        public ServerReadinessFixture.RouteInitializationControl.Gate Gate { get; }
        public IShellRegistry Registry { get; }

        public static WarmupHarness Create(bool warmDefaultShell = true)
        {
            var lifetime = new ControlledHostApplicationLifetime();
            var state = new ShellReadinessState(TimeProvider.System);
            var control = new ServerReadinessFixture.RouteInitializationControl();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostApplicationLifetime>(lifetime);
            services.AddSingleton(state);
            services.AddSingleton<IOptions<ShellReadinessOptions>>(Options.Create(new ShellReadinessOptions
            {
                WarmDefaultShell = warmDefaultShell,
                DefaultShellName = ServerReadinessFixture.DefaultShellName
            }));
            services.AddSingleton(NullLogger<DefaultShellWarmup>.Instance);
            services.AddCShells(shells => shells
                .WithAssemblies(typeof(ServerReadinessFixture.ReadinessProbeFeature).Assembly)
                .AddShell(ServerReadinessFixture.DefaultShellName, shell =>
                    shell.WithFeature<ServerReadinessFixture.ReadinessProbeFeature>(feature =>
                    {
                        feature.ControlId = control.Id;
                        feature.ShellName = ServerReadinessFixture.DefaultShellName;
                    })));

            var provider = services.BuildServiceProvider();
            var warmup = ActivatorUtilities.CreateInstance<DefaultShellWarmup>(provider);
            return new WarmupHarness(
                provider,
                warmup,
                lifetime,
                state,
                control,
                control.For(ServerReadinessFixture.DefaultShellName));
        }

        public async ValueTask DisposeAsync()
        {
            Gate.Release();
            await Warmup.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
            _control.Dispose();
            Lifetime.Dispose();
        }
    }

    private sealed class ControlledHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void SignalStarted() => _started.Cancel();
        public void StopApplication() => _stopping.Cancel();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    private sealed class ShellActivationTelemetryRecorder : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _activities = new();
        private readonly ConcurrentQueue<Measurement> _measurements = new();
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public ShellActivationTelemetryRecorder()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ShellActivationTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _activities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == ShellActivationTelemetry.MeterName
                        && instrument.Name == ShellActivationTelemetry.DurationInstrumentName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
                _measurements.Enqueue(new Measurement(value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertAttempt(string overallOutcome, string shellActivationOutcome)
        {
            var activities = _activities.ToArray();
            var overall = activities.FirstOrDefault(candidate =>
                HasTags(candidate, ShellActivationTelemetry.OverallPhase, overallOutcome)
                && activities.Any(child => child.TraceId == candidate.TraceId
                    && child.ParentSpanId == candidate.SpanId
                    && HasTags(child, ShellActivationTelemetry.ShellActivationPhase, shellActivationOutcome)));
            Assert.NotNull(overall);
            var childPhases = activities
                .Where(activity => activity.TraceId == overall!.TraceId && activity.ParentSpanId == overall.SpanId)
                .ToArray();

            Assert.Contains(childPhases, activity => HasTags(
                activity,
                ShellActivationTelemetry.FeatureDiscoveryPhase,
                ShellActivationTelemetry.SuccessOutcome));
            Assert.Contains(childPhases, activity => HasTags(
                activity,
                ShellActivationTelemetry.ShellActivationPhase,
                shellActivationOutcome));
            Assert.All(
                childPhases.Append(overall!),
                activity => Assert.Equal(
                    new[] { ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.PhaseTag },
                    activity.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal)));

            Assert.Contains(_measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.OverallPhase,
                overallOutcome));
            Assert.Contains(_measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.FeatureDiscoveryPhase,
                ShellActivationTelemetry.SuccessOutcome));
            Assert.Contains(_measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.ShellActivationPhase,
                shellActivationOutcome));
            Assert.All(_measurements, measurement =>
            {
                Assert.True(measurement.Value >= 0);
                Assert.Equal(
                    new[] { ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.PhaseTag },
                    measurement.Tags.Select(tag => tag.Key).Order(StringComparer.Ordinal));
                Assert.DoesNotContain(measurement.Tags, tag =>
                    tag.Value?.ToString()?.Contains("sensitive-test-detail", StringComparison.Ordinal) == true);
            });
        }

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private static bool HasTags(Activity activity, string phase, string outcome) =>
            Equals(activity.GetTagItem(ShellActivationTelemetry.PhaseTag), phase)
            && Equals(activity.GetTagItem(ShellActivationTelemetry.OutcomeTag), outcome);

        private sealed record Measurement(double Value, KeyValuePair<string, object?>[] Tags)
        {
            public bool HasTags(string phase, string outcome) =>
                Equals(Tag(ShellActivationTelemetry.PhaseTag), phase)
                && Equals(Tag(ShellActivationTelemetry.OutcomeTag), outcome);

            private object? Tag(string name) => Tags.Single(tag => tag.Key == name).Value;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }
}
