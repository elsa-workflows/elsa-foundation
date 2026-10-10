using System.Diagnostics;
using CShells.Features;
using CShells.Hosting;
using Elsa.Primitives.Diagnostics;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench.Readiness;

public sealed class DefaultShellWarmup(
    IHostApplicationLifetime applicationLifetime,
    IRuntimeFeatureCatalog featureCatalog,
    IShellActivationRunner activationRunner,
    ShellReadinessState readinessState,
    IOptions<ShellReadinessOptions> options,
    ILogger<DefaultShellWarmup> logger) : IHostedService
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _stopping;
    private Task? _backgroundTask;
    private Task? _stopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (_backgroundTask is not null || _stopTask is not null)
                return Task.CompletedTask;

            options.Value.Validate();
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                applicationLifetime.ApplicationStopping);
            _backgroundTask = WarmAsync(_stopping.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        lock (_syncRoot)
        {
            _stopTask ??= StopCoreAsync(_stopping, _backgroundTask);
            stopTask = _stopTask;
        }

        await stopTask.WaitAsync(cancellationToken);
    }

    private static async Task StopCoreAsync(CancellationTokenSource? stopping, Task? backgroundTask)
    {
        Exception? cancellationFailure = null;
        try
        {
            if (stopping is not null)
                await stopping.CancelAsync();
        }
        catch (Exception exception)
        {
            cancellationFailure = exception;
        }

        Exception? workFailure = null;
        try
        {
            if (backgroundTask is not null)
                await backgroundTask;
        }
        catch (Exception exception)
        {
            workFailure = exception;
        }
        finally
        {
            stopping?.Dispose();
        }

        if (cancellationFailure is not null && workFailure is not null)
            throw new AggregateException("Warmup cancellation and owned work both failed during shutdown.", cancellationFailure, workFailure);
        if (cancellationFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellationFailure).Throw();
        if (workFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(workFailure).Throw();
    }

    private async Task WarmAsync(CancellationToken cancellationToken)
    {
        IShellActivationRun? run = null;
        try
        {
            await WaitForApplicationStartedAsync(cancellationToken);
            var value = options.Value;
            if (!value.WarmDefaultShell)
            {
                readinessState.MarkDisabled(value.DefaultShellName);
                return;
            }

            if (!readinessState.TryBegin(value.DefaultShellName))
                return;

            logger.LogInformation("Preparing default shell {ShellName} after the server began listening", value.DefaultShellName);
            var generation = await ObservePhaseAsync(
                ShellActivationTelemetry.OverallPhase,
                async () =>
                {
                    await ObservePhaseAsync(
                        ShellActivationTelemetry.FeatureDiscoveryPhase,
                        () => featureCatalog.GetSnapshotAsync(cancellationToken));
                    return await ObservePhaseAsync(
                        ShellActivationTelemetry.ShellActivationPhase,
                        async () =>
                        {
                            var observer = new WarmupAttemptObserver();
                            run = activationRunner.Start(
                                [value.DefaultShellName],
                                static _ => ShellActivationRetryDecision.Stop,
                                observer,
                                cancellationToken);
                            await run.InitialPass;

                            var attempt = observer.Attempt
                                ?? throw new InvalidOperationException("The shell activation completed without an attempt result.");
                            return attempt.Outcome switch
                            {
                                ShellActivationAttemptOutcome.Succeeded when attempt.ReturnedGeneration is long returnedGeneration => checked((int)returnedGeneration),
                                ShellActivationAttemptOutcome.Succeeded => throw new InvalidOperationException("The shell activation completed without a returned generation."),
                                ShellActivationAttemptOutcome.ActivationFailed => ThrowActivationFailure(attempt),
                                _ => throw new InvalidOperationException("The shell activation did not return a current generation.")
                            };
                        });
                });
            readinessState.MarkReady(generation);
            logger.LogInformation(
                "Default shell {ShellName} generation {Generation} is ready after {DurationMs:F3} ms",
                value.DefaultShellName,
                generation,
                readinessState.Snapshot.Duration?.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readinessState.MarkCancelled(options.Value.DefaultShellName);
            logger.LogInformation("Default shell preparation was cancelled");
        }
        catch (Exception exception)
        {
            readinessState.MarkFailed("shell_activation_failed");
            logger.LogError(exception, "Default shell preparation failed");
        }
        finally
        {
            if (run is not null)
                await run.StopAsync(CancellationToken.None);
        }
    }

    private static int ThrowActivationFailure(ShellActivationAttempt attempt)
    {
        if (attempt.Exception is not { } exception)
            throw new InvalidOperationException("The shell activation failed without an exception.");

        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("Unreachable after rethrowing the activation exception.");
    }

    private sealed class WarmupAttemptObserver : IShellActivationAttemptObserver
    {
        public ShellActivationAttempt? Attempt { get; private set; }

        public ValueTask OnAttemptCompletedAsync(ShellActivationAttempt attempt, CancellationToken cancellationToken)
        {
            Attempt = attempt;
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<T> ObservePhaseAsync<T>(string phase, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = ShellActivationTelemetry.SuccessOutcome;
        using var activity = ObservationalTelemetryScope.Start(
            ShellActivationTelemetry.GetActivitySource,
            ShellActivationTelemetry.ActivityName);

        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            outcome = ShellActivationTelemetry.CancelledOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception)
        {
            outcome = ShellActivationTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { ShellActivationTelemetry.PhaseTag, phase },
                { ShellActivationTelemetry.OutcomeTag, outcome }
            };
            activity.SetTag(ShellActivationTelemetry.PhaseTag, phase);
            activity.SetTag(ShellActivationTelemetry.OutcomeTag, outcome);
            activity.Observe(
                ShellActivationTelemetry.GetDuration,
                histogram => histogram.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags));
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        if (applicationLifetime.ApplicationStarted.IsCancellationRequested)
            return;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            started);
        using var startedRegistration = applicationLifetime.ApplicationStarted.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            started);
        await started.Task;
    }
}
