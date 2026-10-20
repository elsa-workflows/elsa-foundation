using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Runs bounded attempts over only the host's explicitly configured scopes.</summary>
internal sealed class EtlRuntimeWorker(
    IServiceScopeFactory scopes,
    EtlRuntimeOptions options,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<EtlRuntimeWorker> logger) : BackgroundService, IBackgroundTask
{
    private readonly object _startGate = new();
    private Task? _startup;
    private bool _waitForHostStartup;
    private readonly string _workerId = "etl-" + Guid.NewGuid().ToString("N");
    private readonly EtlScope[] _scopes = options.FileBindings.Select(binding => binding.Scope).Distinct().ToArray();
    private long _workOffset;
    private int _missingStoreReported;

    public override Task StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, waitForHostStartup: true);

    // Tasks starts shell-owned work in the Start phase, after every Prepare-phase migration.
    // A shell container's copied IHostApplicationLifetime is not the root host's readiness signal.
    Task IBackgroundTask.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken, waitForHostStartup: false);

    private Task StartAsync(CancellationToken cancellationToken, bool waitForHostStartup)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An activation caller's token must not become the running shell's lifetime.
        lock (_startGate)
        {
            if (_startup is not null)
                return _startup;
            _waitForHostStartup = waitForHostStartup;
            return _startup = base.StartAsync(CancellationToken.None);
        }
    }

    Task ITask.ExecuteAsync(CancellationToken cancellationToken) => ExecuteTask
        ?? throw new InvalidOperationException("The ETL background worker has not been started.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempts = new List<Task>(options.MaxConcurrentAttempts);
        try
        {
            if (_waitForHostStartup)
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (lifetime.ApplicationStarted.Register(() => ready.TrySetResult()))
                    await ready.Task.WaitAsync(stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                for (var index = attempts.Count - 1; index >= 0; index--)
                {
                    if (!attempts[index].IsCompleted)
                        continue;
                    await attempts[index];
                    attempts.RemoveAt(index);
                }
                while (_scopes.Length > 0 && attempts.Count < options.MaxConcurrentAttempts)
                    attempts.Add(RunOneAsync(stoppingToken));
                await Task.Delay(TimeSpan.FromMilliseconds(250), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            // Host shutdown is a request to stop. Wait for real attempts to unwind before disposing their services.
            await Task.WhenAll(attempts);
        }
    }

    private async Task RunOneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var operation = scopes.CreateAsyncScope();
            var store = operation.ServiceProvider.GetService<IEtlRuntimeStore>();
            if (store is null)
            {
                if (Interlocked.Exchange(ref _missingStoreReported, 1) == 0)
                    logger.LogWarning("ETL execution requires a configured runtime store; no work has been acknowledged.");
                return;
            }
            var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
            var delivery = operation.ServiceProvider.GetService<IEtlResultDelivery>();
            // Rotate scope priority and work kind so preparation, partitions and configured delivery share capacity fairly.
            var ticket = (ulong)Interlocked.Increment(ref _workOffset);
            var start = (int)((ticket / 3) % (ulong)_scopes.Length);
            var preferredKind = (int)(ticket % 3);
            for (var offset = 0; offset < _scopes.Length; offset++)
            {
                var scope = _scopes[(start + offset) % _scopes.Length];
                for (var kindOffset = 0; kindOffset < 3; kindOffset++)
                {
                    switch ((preferredKind + kindOffset) % 3)
                    {
                        case 0:
                        {
                            var preparation = await store.ClaimPreparationAsync(scope, _workerId, cancellationToken);
                            if (preparation is null) continue;
                            await execution.PrepareAsync(preparation, cancellationToken);
                            return;
                        }
                        case 1:
                        {
                            var partition = await store.ClaimPartitionAsync(scope, null, _workerId, cancellationToken);
                            if (partition is null) continue;
                            await execution.ExecutePartitionAsync(partition, cancellationToken);
                            return;
                        }
                        case 2 when delivery is not null:
                        {
                            var lease = await store.ClaimDeliveryAsync(scope, _workerId, cancellationToken);
                            if (lease is null) continue;
                            var disposition = await delivery.DeliverAsync(lease, cancellationToken);
                            if (disposition is { } proof && Enum.IsDefined(proof))
                                await store.AcknowledgeDeliveryAsync(lease, proof, cancellationToken);
                            return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning("ETL attempt stopped after {FailureType}; durable work remains recoverable.", exception.GetType().Name);
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }
}
