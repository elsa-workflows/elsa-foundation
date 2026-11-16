using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Hosting;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Workbench.Boot;
using Elsa.Workbench.Readiness;
using Microsoft.Extensions.Configuration;
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
    public async Task StoppingRealActivationRunCancelsActivationTokenWithoutStoppingShellLifetimeBackgroundTasks()
    {
        await using var harness = WarmupHarness.Create(includeBackgroundTaskProbe: true);
        var backgroundTask = Assert.IsType<BackgroundTaskLifetimeProbe>(harness.BackgroundTaskProbe);
        var activationTokenCapture = Assert.IsType<ActivationTokenCapture>(harness.ActivationTokenCapture);
        var observer = new GatedActivationAttemptObserver();
        var run = harness.ActivationRunner.Start([ServerReadinessFixture.DefaultShellName], observer: observer);
        Task? stopping = null;
        IShell? activatedShell = null;

        try
        {
            await harness.Gate.WaitUntilEnteredAsync();
            harness.Gate.Release();
            var attempt = await observer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ShellActivationAttemptOutcome.Succeeded, attempt.Outcome);
            await backgroundTask.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            activatedShell = Assert.IsAssignableFrom<IShell>(harness.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
            Assert.Equal(ShellLifecycleState.Active, activatedShell.State);

            var activationToken = await activationTokenCapture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(activationToken.IsCancellationRequested);

            stopping = run.StopAsync(CancellationToken.None);
            await WaitForAsync(() => activationToken.IsCancellationRequested);

            Assert.True(activationToken.IsCancellationRequested);
            Assert.False(harness.Lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(backgroundTask.ExecutionToken.IsCancellationRequested);
            Assert.False(backgroundTask.ExecutionEnded.Task.IsCompleted);

            var ticksAtStop = backgroundTask.TickCount;
            await WaitForAsync(() => backgroundTask.TickCount > ticksAtStop);
            Assert.False(backgroundTask.ExecutionToken.IsCancellationRequested);
            Assert.False(backgroundTask.ExecutionEnded.Task.IsCompleted);
        }
        finally
        {
            observer.Release();
            stopping ??= run.StopAsync(CancellationToken.None);
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var shell = Assert.IsAssignableFrom<IShell>(harness.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
        Assert.Same(activatedShell, shell);
        Assert.Equal(ShellLifecycleState.Active, shell.State);
        var drain = await harness.Registry.DrainAsync(shell).WaitAsync(TimeSpan.FromSeconds(10));
        await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await backgroundTask.ExecutionEnded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(backgroundTask.StopCalled.Task.IsCompleted);
        Assert.True(backgroundTask.ExecutionToken.IsCancellationRequested);
        Assert.False(harness.Lifetime.ApplicationStopping.IsCancellationRequested);
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
    public async Task WarmupRecordsTheGenerationReturnedByItsAcceptedActivation()
    {
        var returned = new GenerationShell(ServerReadinessFixture.DefaultShellName, 23);
        var later = new GenerationShell(ServerReadinessFixture.DefaultShellName, 24);
        var registry = new ControlledCustomRegistry(returned, later);
        await using var harness = WarmupHarness.Create(registry: registry);
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await registry.WaitUntilActivationStartsAsync();

        registry.ReleaseActivation();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        Assert.Equal(23, harness.State.Snapshot.Generation);
        Assert.Equal(1, registry.CurrentReads);
    }

    [Fact]
    public async Task FeatureDiscoveryCompletesBeforeRunnerStartsActivation()
    {
        await using var harness = WarmupHarness.Create(gateFeatureDiscovery: true);
        var discovery = Assert.IsType<GatedRuntimeFeatureCatalog>(harness.FeatureCatalogGate);

        try
        {
            await harness.Warmup.StartAsync(CancellationToken.None);
            harness.Lifetime.SignalStarted();
            await discovery.WaitUntilEnteredAsync();

            Assert.Equal(0, harness.Gate.Attempts);

            discovery.Release();
            await harness.Gate.WaitUntilEnteredAsync();
            Assert.Equal(1, harness.Gate.Attempts);

            harness.Gate.Release();
            await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);
        }
        finally
        {
            discovery.Release();
            harness.Gate.Release();
            await harness.Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task EagerAndWarmupRunsShareOneShellActivation()
    {
        await using var harness = WarmupHarness.Create();
        var eager = harness.CreateEagerService();
        var eagerStart = eager.StartAsync(CancellationToken.None);
        await harness.Gate.WaitUntilEnteredAsync();
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();

        Assert.Equal(1, harness.Gate.Attempts);
        harness.Gate.Release();
        await eagerStart.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        Assert.Equal(1, harness.Gate.Attempts);
        await eager.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
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
        using var testTrace = new Activity("elsa.test.shell-readiness").Start();
        using var telemetry = new ShellActivationTelemetryRecorder();
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        await harness.Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Warmup.StartAsync(CancellationToken.None);

        Assert.Equal(ShellReadinessStatus.Failed, harness.State.Snapshot.Status);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
        Assert.Null(harness.Registry.GetActive(ServerReadinessFixture.DefaultShellName));
        Assert.Equal(1, harness.Gate.Attempts);
        telemetry.AssertAttempt(
            testTrace.TraceId,
            ShellActivationTelemetry.CancelledOutcome,
            ShellActivationTelemetry.CancelledOutcome);
    }

    [Fact]
    public async Task BoundedWarmupStopRetainsAndJoinsCancellationIgnoringActivation()
    {
        await using var harness = WarmupHarness.Create();
        harness.Gate.IgnoreCancellation = true;
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        using var boundedWait = new CancellationTokenSource();
        var firstStop = harness.Warmup.StopAsync(boundedWait.Token);
        boundedWait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstStop.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(ShellReadinessStatus.Starting, harness.State.Snapshot.Status);
        Assert.Equal(1, harness.Gate.Attempts);
        harness.Gate.Release();
        await harness.Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Warmup.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ShellReadinessStatus.Failed, harness.State.Snapshot.Status);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
        Assert.Equal(1, harness.Gate.Attempts);
    }

    [Fact]
    public async Task WarmupStopJoinsOwnedWorkWhenCancellationCallbackThrows()
    {
        await using var harness = WarmupHarness.Create();
        harness.Gate.IgnoreCancellation = true;
        harness.Gate.ThrowFromCancellationCallback = true;
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        var stop = harness.Warmup.StopAsync(CancellationToken.None);
        Assert.False(stop.IsCompleted);
        await harness.Gate.WaitUntilCancellationCallbackInvokedAsync();
        Assert.False(stop.IsCompleted);
        harness.Gate.Release();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => stop.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("readiness cancellation callback failed", exception.ToString(), StringComparison.Ordinal);
        harness.ExpectStopFailure(exception);
        Assert.True(harness.Gate.Initialized);
        Assert.Equal(1, harness.Gate.Attempts);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
    }

    [Fact]
    public async Task StopBeforeApplicationStartedPublishesCancelledState()
    {
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);

        await harness.Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Warmup.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ShellReadinessStatus.Failed, harness.State.Snapshot.Status);
        Assert.Equal("shell_activation_cancelled", harness.State.Snapshot.Code);
        Assert.Equal(ServerReadinessFixture.DefaultShellName, harness.State.Snapshot.ShellName);
        Assert.Equal(0, harness.Gate.Attempts);
    }

    [Fact]
    public async Task WarmupStopBeforeStartPreventsLaterActivation()
    {
        await using var harness = WarmupHarness.Create();

        await harness.Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Warmup.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        harness.Lifetime.SignalStarted();

        Assert.Equal(ShellReadinessStatus.NotStarted, harness.State.Snapshot.Status);
        Assert.Equal(0, harness.Gate.Attempts);
    }

    [Fact]
    public async Task SuccessfulWarmupEmitsBoundedHierarchicalActivationTelemetry()
    {
        using var testTrace = new Activity("elsa.test.shell-readiness").Start();
        using var telemetry = new ShellActivationTelemetryRecorder();
        await using var harness = WarmupHarness.Create();
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Ready);

        telemetry.AssertAttempt(
            testTrace.TraceId,
            ShellActivationTelemetry.SuccessOutcome,
            ShellActivationTelemetry.SuccessOutcome);
    }

    [Fact]
    public async Task FailedWarmupEmitsReachedPhasesWithoutSensitiveDimensions()
    {
        using var testTrace = new Activity("elsa.test.shell-readiness").Start();
        using var telemetry = new ShellActivationTelemetryRecorder();
        await using var harness = WarmupHarness.Create();
        harness.Gate.Failure = new InvalidOperationException("sensitive-test-detail");
        await harness.Warmup.StartAsync(CancellationToken.None);
        harness.Lifetime.SignalStarted();
        await harness.Gate.WaitUntilEnteredAsync();

        harness.Gate.Release();
        await WaitForStatusAsync(harness.State, ShellReadinessStatus.Failed);

        telemetry.AssertAttempt(
            testTrace.TraceId,
            ShellActivationTelemetry.FailedOutcome,
            ShellActivationTelemetry.FailedOutcome);
        Assert.Equal(1, harness.Gate.Attempts);
    }

    [Fact]
    public void TelemetryRecorderSelectsOwnedTraceWhenForeignAttemptIsPartial()
    {
        var foreign = CreateTelemetryAttempt(includeFeatureDiscovery: false);
        var owned = CreateTelemetryAttempt(includeFeatureDiscovery: true);

        var selected = ShellActivationTelemetryRecorder.FindAttempt(
            foreign.Activities.Concat(owned.Activities),
            owned.TraceId,
            ShellActivationTelemetry.SuccessOutcome,
            ShellActivationTelemetry.SuccessOutcome);
        var selectedMeasurements = ShellActivationTelemetryRecorder.FindMeasurements(
            new[]
            {
                new ShellActivationTelemetryRecorder.Measurement(
                    foreign.TraceId,
                    1,
                    [new(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.ShellActivationPhase),
                        new(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome)]),
                new ShellActivationTelemetryRecorder.Measurement(
                    owned.TraceId,
                    1,
                    [new(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.OverallPhase),
                        new(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome)]),
                new ShellActivationTelemetryRecorder.Measurement(
                    owned.TraceId,
                    1,
                    [new(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.FeatureDiscoveryPhase),
                        new(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome)]),
                new ShellActivationTelemetryRecorder.Measurement(
                    owned.TraceId,
                    1,
                    [new(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.ShellActivationPhase),
                        new(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome)])
            },
            owned.TraceId);

        Assert.NotNull(selected);
        Assert.Equal(owned.TraceId, selected.TraceId);
        Assert.Equal(3, selectedMeasurements.Length);
        Assert.All(selectedMeasurements, measurement => Assert.Equal(owned.TraceId, measurement.TraceId));

        static (ActivityTraceId TraceId, Activity[] Activities) CreateTelemetryAttempt(bool includeFeatureDiscovery)
        {
            var previous = Activity.Current;
            var overall = new Activity("elsa.shell.activation").Start();
            overall.SetTag(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.OverallPhase);
            overall.SetTag(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome);

            var activities = new List<Activity> { overall };
            if (includeFeatureDiscovery)
            {
                var featureDiscovery = new Activity("elsa.shell.activation").Start();
                featureDiscovery.SetTag(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.FeatureDiscoveryPhase);
                featureDiscovery.SetTag(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome);
                featureDiscovery.Stop();
                activities.Add(featureDiscovery);
            }

            var shellActivation = new Activity("elsa.shell.activation").Start();
            shellActivation.SetTag(ShellActivationTelemetry.PhaseTag, ShellActivationTelemetry.ShellActivationPhase);
            shellActivation.SetTag(ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.SuccessOutcome);
            shellActivation.Stop();
            overall.Stop();
            Activity.Current = previous;
            activities.Add(shellActivation);
            return (overall.TraceId, activities.ToArray());
        }
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

    private static Task WaitForStatusAsync(ShellReadinessState state, ShellReadinessStatus expected) =>
        WaitForAsync(() => state.Snapshot.Status == expected);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class WarmupHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ServerReadinessFixture.RouteInitializationControl _control;
        private readonly BackgroundTaskLifetimeControl? _backgroundTaskControl;
        private Exception? _expectedStopFailure;

        private WarmupHarness(
            ServiceProvider provider,
            DefaultShellWarmup warmup,
            ControlledHostApplicationLifetime lifetime,
            ShellReadinessState state,
            ServerReadinessFixture.RouteInitializationControl control,
            ServerReadinessFixture.RouteInitializationControl.Gate gate,
            IRuntimeFeatureCatalog? featureCatalogGate,
            BackgroundTaskLifetimeControl? backgroundTaskControl)
        {
            _provider = provider;
            _control = control;
            Warmup = warmup;
            Lifetime = lifetime;
            State = state;
            Gate = gate;
            FeatureCatalogGate = featureCatalogGate;
            Registry = provider.GetRequiredService<IShellRegistry>();
            _backgroundTaskControl = backgroundTaskControl;
            BackgroundTaskProbe = backgroundTaskControl?.BackgroundTaskProbe;
            ActivationTokenCapture = backgroundTaskControl?.ActivationTokenCapture;
        }

        public DefaultShellWarmup Warmup { get; }
        public ControlledHostApplicationLifetime Lifetime { get; }
        public ShellReadinessState State { get; }
        public ServerReadinessFixture.RouteInitializationControl.Gate Gate { get; }
        public IShellRegistry Registry { get; }
        public IShellActivationRunner ActivationRunner => _provider.GetRequiredService<IShellActivationRunner>();
        public IRuntimeFeatureCatalog? FeatureCatalogGate { get; }
        public BackgroundTaskLifetimeProbe? BackgroundTaskProbe { get; }
        public ActivationTokenCapture? ActivationTokenCapture { get; }

        public EagerShellActivationHostedService CreateEagerService() =>
            ActivatorUtilities.CreateInstance<EagerShellActivationHostedService>(_provider);

        public void ExpectStopFailure(Exception exception) => _expectedStopFailure = exception;

        public static WarmupHarness Create(
            bool warmDefaultShell = true,
            IShellRegistry? registry = null,
            bool gateFeatureDiscovery = false,
            bool includeBackgroundTaskProbe = false)
        {
            var lifetime = new ControlledHostApplicationLifetime();
            var state = new ShellReadinessState(TimeProvider.System);
            var control = new ServerReadinessFixture.RouteInitializationControl();
            var backgroundTaskControl = includeBackgroundTaskProbe ? new BackgroundTaskLifetimeControl() : null;
            var services = new ServiceCollection();
            services.AddLogging();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
                ["CShells:Shells:default:Name"] = ServerReadinessFixture.DefaultShellName
            }).Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostApplicationLifetime>(lifetime);
            services.AddSingleton(state);
            services.AddSingleton<IOptions<ShellReadinessOptions>>(Options.Create(new ShellReadinessOptions
            {
                WarmDefaultShell = warmDefaultShell,
                DefaultShellName = ServerReadinessFixture.DefaultShellName
            }));
            services.AddSingleton(NullLogger<DefaultShellWarmup>.Instance);
            var featureAssemblies = includeBackgroundTaskProbe
                ? new[] { typeof(ServerReadinessFixture.ReadinessProbeFeature).Assembly, typeof(TasksFeature).Assembly }
                : new[] { typeof(ServerReadinessFixture.ReadinessProbeFeature).Assembly };
            services.AddCShells(shells => shells
                .WithAssemblies(featureAssemblies)
                .AddShell(ServerReadinessFixture.DefaultShellName, shell =>
                {
                    shell.WithFeature<ServerReadinessFixture.ReadinessProbeFeature>(feature =>
                    {
                        feature.ControlId = control.Id;
                        feature.ShellName = ServerReadinessFixture.DefaultShellName;
                    });
                    if (includeBackgroundTaskProbe)
                    {
                        shell.WithFeature<TasksFeature>();
                        shell.WithFeature<WarmupTaskProbeFeature>(feature =>
                        {
                            feature.ControlId = backgroundTaskControl!.Id;
                        });
                    }
                }));
            if (registry is not null)
                services.AddSingleton(registry);
            services.AddShellActivationRunner();

            var provider = services.BuildServiceProvider();
            var featureCatalogGate = gateFeatureDiscovery
                ? new GatedRuntimeFeatureCatalog(provider.GetRequiredService<IRuntimeFeatureCatalog>())
                : null;
            var warmup = featureCatalogGate is null
                ? ActivatorUtilities.CreateInstance<DefaultShellWarmup>(provider)
                : ActivatorUtilities.CreateInstance<DefaultShellWarmup>(provider, featureCatalogGate);
            return new WarmupHarness(
                provider,
                warmup,
                lifetime,
                state,
                control,
                control.For(ServerReadinessFixture.DefaultShellName),
                featureCatalogGate,
                backgroundTaskControl);
        }

        public async ValueTask DisposeAsync()
        {
            Gate.Release();
            var failures = new List<Exception>();
            try
            {
                await Warmup.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (ReferenceEquals(exception, _expectedStopFailure))
            {
                // The test already asserted this exact terminal stop failure.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (_backgroundTaskControl is not null)
            {
                foreach (var shell in Registry.GetActiveShells().ToArray())
                {
                    try
                    {
                        var drain = await Registry.DrainAsync(shell).WaitAsync(TimeSpan.FromSeconds(10));
                        await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            try
            {
                await _provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                _backgroundTaskControl?.Dispose();
                _control.Dispose();
                Lifetime.Dispose();
            }

            if (failures.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("Warmup harness cleanup failed.", failures);
        }
    }

    [ShellFeature(name: "WarmupTaskProbe", DisplayName = "Warmup task lifetime probe", Description = "Test-only task registration for shell readiness lifetime coverage.")]
    public sealed class WarmupTaskProbeFeature : IShellFeature
    {
        public string ControlId { get; set; } = null!;

        public void ConfigureServices(IServiceCollection services)
        {
            var control = BackgroundTaskLifetimeControl.Resolve(ControlId);
            services.AddSingleton<IDistributedLockProvider>(new UnusedDistributedLockProvider());
            services.AddSingleton<IBackgroundTask>(control.BackgroundTaskProbe);
            services.AddSingleton(control.ActivationTokenCapture);
            services.AddShellInitializer<ActivationTokenCaptureInitializer>(LifecyclePhase.Start, int.MaxValue);
        }
    }

    private sealed class BackgroundTaskLifetimeControl : IDisposable
    {
        private static readonly ConcurrentDictionary<string, BackgroundTaskLifetimeControl> Instances = new(StringComparer.Ordinal);

        public BackgroundTaskLifetimeControl()
        {
            Id = Guid.NewGuid().ToString("n");
            BackgroundTaskProbe = new BackgroundTaskLifetimeProbe();
            ActivationTokenCapture = new ActivationTokenCapture(BackgroundTaskProbe);
            Instances[Id] = this;
        }

        public string Id { get; }
        public BackgroundTaskLifetimeProbe BackgroundTaskProbe { get; }
        public ActivationTokenCapture ActivationTokenCapture { get; }

        public static BackgroundTaskLifetimeControl Resolve(string id) =>
            Instances.TryGetValue(id, out var control)
                ? control
                : throw new InvalidOperationException($"No warmup task probe control is registered for '{id}'.");

        public void Dispose() => Instances.TryRemove(Id, out _);
    }

    private sealed class ActivationTokenCapture(BackgroundTaskLifetimeProbe backgroundTaskProbe)
    {
        public TaskCompletionSource<CancellationToken> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CaptureAndWaitForTaskAsync(CancellationToken cancellationToken)
        {
            Captured.TrySetResult(cancellationToken);
            await backgroundTaskProbe.ExecutionStarted.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ActivationTokenCaptureInitializer(ActivationTokenCapture capture) : IShellInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => capture.CaptureAndWaitForTaskAsync(cancellationToken);
    }

    private sealed class GatedActivationAttemptObserver : IShellActivationAttemptObserver
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ShellActivationAttempt> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async ValueTask OnAttemptCompletedAsync(ShellActivationAttempt attempt, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(attempt);
            // Keep StopAsync pending until the test observes cancellation and releases this success boundary.
            await _release.Task;
        }
    }

    private sealed class BackgroundTaskLifetimeProbe : IBackgroundTask
    {
        private int _tickCount;
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExecutionEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ExecutionToken { get; private set; }
        public int TickCount => Volatile.Read(ref _tickCount);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ExecutionToken = cancellationToken;
            ExecutionStarted.TrySetResult();
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _tickCount);
                    var delay = Task.Delay(10, cancellationToken);
                    if (await Task.WhenAny(delay, _stopRequested.Task) == _stopRequested.Task)
                        return;
                    await delay;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shell teardown cancels the task lifetime after requesting a graceful stop.
            }
            finally
            {
                ExecutionEnded.TrySetResult();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalled.TrySetResult();
            _stopRequested.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The warmup task probe does not acquire distributed locks.");

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The warmup task probe does not acquire distributed locks.");

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The warmup task probe does not acquire distributed locks.");
    }

    private sealed class GenerationShell(string name, int generation) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create(name, generation);
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public IShellScope BeginScope() => throw new NotSupportedException();
        public IDrainOperation? Drain => null;
    }

    private sealed class ControlledCustomRegistry(IShell returned, IShell later) : IShellRegistry
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _currentReads;

        public int CurrentReads => Volatile.Read(ref _currentReads);

        public async Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return returned;
        }

        public Task WaitUntilActivationStartsAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void ReleaseActivation() => _release.TrySetResult();
        public IShell? GetActive(string name) => Interlocked.Increment(ref _currentReads) == 1 ? returned : later;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
                _measurements.Enqueue(new Measurement(Activity.Current?.TraceId ?? default, value, tags.ToArray())));
            _meterListener.Start();
        }

        public void AssertAttempt(ActivityTraceId traceId, string overallOutcome, string shellActivationOutcome)
        {
            var activities = _activities
                .Where(activity => activity.TraceId == traceId)
                .ToArray();
            var overall = FindAttempt(activities, traceId, overallOutcome, shellActivationOutcome);
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

            var measurements = FindMeasurements(_measurements, traceId);
            Assert.Contains(measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.OverallPhase,
                overallOutcome));
            Assert.Contains(measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.FeatureDiscoveryPhase,
                ShellActivationTelemetry.SuccessOutcome));
            Assert.Contains(measurements, measurement => measurement.HasTags(
                ShellActivationTelemetry.ShellActivationPhase,
                shellActivationOutcome));
            Assert.All(measurements, measurement =>
            {
                Assert.True(measurement.Value >= 0);
                Assert.Equal(
                    new[] { ShellActivationTelemetry.OutcomeTag, ShellActivationTelemetry.PhaseTag },
                    measurement.Tags.Select(tag => tag.Key).Order(StringComparer.Ordinal));
                Assert.DoesNotContain(measurement.Tags, tag =>
                    tag.Value?.ToString()?.Contains("sensitive-test-detail", StringComparison.Ordinal) == true);
            });
        }

        internal static Activity? FindAttempt(
            IEnumerable<Activity> activities,
            ActivityTraceId traceId,
            string overallOutcome,
            string shellActivationOutcome)
        {
            var capturedActivities = activities.Where(activity => activity.TraceId == traceId).ToArray();
            return capturedActivities.FirstOrDefault(candidate =>
                HasTags(candidate, ShellActivationTelemetry.OverallPhase, overallOutcome)
                && capturedActivities.Any(child => child.TraceId == candidate.TraceId
                    && child.ParentSpanId == candidate.SpanId
                    && HasTags(child, ShellActivationTelemetry.ShellActivationPhase, shellActivationOutcome)));
        }

        internal static Measurement[] FindMeasurements(IEnumerable<Measurement> measurements, ActivityTraceId traceId) =>
            measurements.Where(measurement => measurement.TraceId == traceId).ToArray();

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private static bool HasTags(Activity activity, string phase, string outcome) =>
            Equals(activity.GetTagItem(ShellActivationTelemetry.PhaseTag), phase)
            && Equals(activity.GetTagItem(ShellActivationTelemetry.OutcomeTag), outcome);

        internal sealed record Measurement(ActivityTraceId TraceId, double Value, KeyValuePair<string, object?>[] Tags)
        {
            public bool HasTags(string phase, string outcome) =>
                Equals(Tag(ShellActivationTelemetry.PhaseTag), phase)
                && Equals(Tag(ShellActivationTelemetry.OutcomeTag), outcome);

            private object? Tag(string name) => Tags.Single(tag => tag.Key == name).Value;
        }
    }

    private sealed class GatedRuntimeFeatureCatalog(IRuntimeFeatureCatalog inner) : IRuntimeFeatureCatalog
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => inner.CurrentSnapshot;

        public async Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await inner.GetSnapshotAsync(cancellationToken);
        }

        public Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            inner.RefreshAsync(cancellationToken);

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) =>
            inner.EnsureInitializedAsync(cancellationToken);

        public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _release.TrySetResult();
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
