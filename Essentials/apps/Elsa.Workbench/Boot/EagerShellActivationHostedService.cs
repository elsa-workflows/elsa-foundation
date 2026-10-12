using CShells.Hosting;

namespace Elsa.Workbench.Boot;

/// <summary>
/// Host-level hosted service that eagerly activates the configured shell(s) at boot (spec 132, program unit 4).
///
/// It is registered only when <c>Elsa:Boot:EagerShellActivation:Enabled</c> is set, so a host that leaves the
/// switch off never constructs it. When enabled, <see cref="StartAsync"/> resolves the target shells and drives
/// each through the host-owned activation runner. This uses the same registry activation path as a cold request,
/// while keeping a single, serial, one-shot startup pass that can overlap safely with other host activation work.
///
/// Deliberately a HOST-level <see cref="IHostedService"/>: CShells does not run shell-scoped hosted services, and
/// the eager trigger must live outside any shell container (it activates the shells). Activation failures are
/// logged and swallowed — eager activation is an optimization, so a failure degrades to today's lazy behavior
/// (the shell activates on its first request) rather than crashing the host.
/// </summary>
public sealed class EagerShellActivationHostedService(
    IConfiguration configuration,
    IShellActivationRunner activationRunner,
    ILogger<EagerShellActivationHostedService> logger) : IHostedService
{
    private readonly object _syncRoot = new();
    private IShellActivationRun? _run;
    private Task? _startTask;
    private Task? _stopTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Task startTask;
        lock (_syncRoot)
        {
            if (_stopTask is not null)
                return;

            _startTask ??= StartCoreAsync(cancellationToken);
            startTask = _startTask;
        }

        await startTask;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var options = EagerShellActivationOptions.Read(configuration);
        if (!options.Enabled)
            return; // Defensive: the service is only registered when enabled, but never activate if the switch flipped.

        var configured = EagerShellActivationOptions.ReadConfiguredShellNames(configuration);
        var targets = options.ResolveTargetShellNames(configured);
        if (targets.Count == 0)
        {
            logger.LogWarning(
                "Eager shell activation is enabled but resolved no target shells (configured shells: [{ConfiguredShells}]). Shells will activate lazily on first request.",
                string.Join(", ", configured));
            return;
        }

        logger.LogInformation("Eager shell activation: activating {Count} shell(s) at boot: [{Shells}]", targets.Count, string.Join(", ", targets));

        var observer = new LoggingAttemptObserver(logger);
        var run = activationRunner.Start(
            targets,
            static _ => ShellActivationRetryDecision.Stop,
            observer,
            cancellationToken);
        lock (_syncRoot)
            _run = run;

        try
        {
            await run.InitialPass;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await run.StopAsync(CancellationToken.None);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Eager activation was cancelled and its owned run failed during cleanup.", exception, cleanupFailure);
            }

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        lock (_syncRoot)
        {
            _stopTask ??= StopCoreAsync(_run, _startTask);
            stopTask = _stopTask;
        }

        return stopTask.WaitAsync(cancellationToken);
    }

    private static async Task StopCoreAsync(IShellActivationRun? run, Task? startTask)
    {
        Exception? stopFailure = null;
        try
        {
            if (run is not null)
                await run.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        Exception? startFailure = null;
        if (startTask is not null)
        {
            try
            {
                await startTask;
            }
            catch (OperationCanceledException)
            {
                // The owned run has already been stopped and joined.
            }
            catch (Exception exception)
            {
                startFailure = exception;
            }
        }

        if (stopFailure is not null && startFailure is not null)
            throw new AggregateException("Eager activation stop and startup both failed.", stopFailure, startFailure);
        if (stopFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopFailure).Throw();
        if (startFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startFailure).Throw();
    }

    private sealed class LoggingAttemptObserver(ILogger<EagerShellActivationHostedService> logger) : IShellActivationAttemptObserver
    {
        public ValueTask OnAttemptCompletedAsync(ShellActivationAttempt attempt, CancellationToken cancellationToken)
        {
            var elapsedMs = (attempt.CompletedAt - attempt.StartedAt).TotalMilliseconds;
            if (attempt.Outcome == ShellActivationAttemptOutcome.Succeeded)
            {
                logger.LogInformation("Eager shell activation: shell '{Shell}' active in {ElapsedMs} ms.", attempt.ShellName, elapsedMs);
            }
            else
            {
                logger.LogWarning(
                    attempt.Exception,
                    "Eager shell activation of shell '{Shell}' failed after {ElapsedMs} ms; it will activate lazily on its first request instead.",
                    attempt.ShellName,
                    elapsedMs);
            }

            return ValueTask.CompletedTask;
        }
    }
}
