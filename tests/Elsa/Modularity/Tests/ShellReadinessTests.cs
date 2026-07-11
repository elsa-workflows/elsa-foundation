using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Server.Readiness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ShellReadinessTests
{
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
