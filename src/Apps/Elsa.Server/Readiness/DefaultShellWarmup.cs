using System.Diagnostics;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.Options;

namespace Elsa.Server.Readiness;

public sealed class DefaultShellWarmup(
    IHostApplicationLifetime applicationLifetime,
    IRuntimeFeatureCatalog featureCatalog,
    IShellRegistry shellRegistry,
    ShellReadinessState readinessState,
    IOptions<ShellReadinessOptions> options,
    ILogger<DefaultShellWarmup> logger) : IHostedService
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _stopping;
    private Task? _backgroundTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            if (_backgroundTask is not null)
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
        Task? backgroundTask;
        lock (_syncRoot)
        {
            _stopping?.Cancel();
            backgroundTask = _backgroundTask;
        }

        if (backgroundTask is not null)
            await backgroundTask.WaitAsync(cancellationToken);
    }

    private async Task WarmAsync(CancellationToken cancellationToken)
    {
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
            var shell = await ObservePhaseAsync(
                ShellActivationTelemetry.OverallPhase,
                async () =>
                {
                    await ObservePhaseAsync(
                        ShellActivationTelemetry.FeatureDiscoveryPhase,
                        () => featureCatalog.GetSnapshotAsync(cancellationToken));
                    return await ObservePhaseAsync(
                        ShellActivationTelemetry.ShellActivationPhase,
                        () => shellRegistry.GetOrActivateAsync(value.DefaultShellName, cancellationToken));
                });
            readinessState.MarkReady(shell.Descriptor.Generation);
            logger.LogInformation(
                "Default shell {ShellName} generation {Generation} is ready after {DurationMs:F3} ms",
                value.DefaultShellName,
                shell.Descriptor.Generation,
                readinessState.Snapshot.Duration?.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readinessState.MarkFailed("shell_activation_cancelled");
            logger.LogInformation("Default shell preparation was cancelled");
        }
        catch (Exception exception)
        {
            readinessState.MarkFailed("shell_activation_failed");
            logger.LogError(exception, "Default shell preparation failed");
        }
    }

    private static async Task<T> ObservePhaseAsync<T>(string phase, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = ShellActivationTelemetry.SuccessOutcome;
        using var activity = ShellActivationTelemetry.ActivitySource.StartActivity(ShellActivationTelemetry.ActivityName);

        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            outcome = ShellActivationTelemetry.CancelledOutcome;
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception)
        {
            outcome = ShellActivationTelemetry.FailedOutcome;
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { ShellActivationTelemetry.PhaseTag, phase },
                { ShellActivationTelemetry.OutcomeTag, outcome }
            };
            activity?.SetTag(ShellActivationTelemetry.PhaseTag, phase);
            activity?.SetTag(ShellActivationTelemetry.OutcomeTag, outcome);
            ShellActivationTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
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
