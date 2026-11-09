using Elsa.Rules.Persistence.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Rules.Runtime;

/// <summary>Shared Rules host lifecycle; model and delivery stores remain the scheduling authorities.</summary>
internal abstract class RulesBackgroundWorker(IServiceScopeFactory scopes, int concurrency, TimeSpan pollInterval,
    TimeProvider clock, IHostApplicationLifetime lifetime, ILogger logger)
    : BackgroundService, IBackgroundTask
{
    private readonly object _startGate = new();
    private Task? _startup;
    private bool _waitForHostStartup;
    private readonly string _workerId = "rules-" + Guid.NewGuid().ToString("N");
    private int _missingStoreReported;

    public override Task StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, true);
    Task IBackgroundTask.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, false);

    private Task StartAsync(CancellationToken cancellationToken, bool waitForHostStartup)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_startGate)
        {
            if (_startup is not null) return _startup;
            // Tasks starts a shell only after Prepare-phase migrations; its copied host lifetime
            // is not root-host readiness. Neither activation caller owns the worker lifetime.
            _waitForHostStartup = waitForHostStartup;
            return _startup = base.StartAsync(CancellationToken.None);
        }
    }

    Task ITask.ExecuteAsync(CancellationToken cancellationToken) => ExecuteTask
        ?? throw new InvalidOperationException("The Rules background worker has not been started.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_waitForHostStartup)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (lifetime.ApplicationStarted.Register(() => ready.TrySetResult()))
                    await ready.Task.WaitAsync(stoppingToken);
            }
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => RunSlotAsync(stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    protected abstract ValueTask<bool> ExecuteOneAsync(IServiceProvider services, string workerId, CancellationToken cancellationToken);

    private async Task RunSlotAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var progressed = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (scope.ServiceProvider.GetService<IRuleRunStore>() is null
                    || scope.ServiceProvider.GetService<IRuleRunReader>() is null)
                {
                    if (Interlocked.Exchange(ref _missingStoreReported, 1) == 0)
                        logger.LogWarning("Rules background work requires a configured store and reader; durable work has not been acknowledged.");
                }
                else
                    progressed = await ExecuteOneAsync(scope.ServiceProvider, _workerId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogWarning("Rules background work stopped after {FailureType}; durable work remains recoverable.", error.GetType().Name);
            }
            if (!progressed)
                await Task.Delay(pollInterval, clock, cancellationToken);
        }
    }
}
