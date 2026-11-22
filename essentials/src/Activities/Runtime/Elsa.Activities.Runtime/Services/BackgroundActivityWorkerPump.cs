using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Owns every running worker task until host shutdown; persistence remains the record of owed work.</summary>
internal sealed class BackgroundActivityWorkerPump(
    IPersistenceScopeRunner scopeRunner,
    BackgroundActivityWorker worker,
    IOptions<BackgroundActivityWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<BackgroundActivityWorkerPump> logger) : IBackgroundTask, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _ownerId = $"background-worker:{Guid.NewGuid():N}";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        options.Value.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => _stopping.CancelAsync();

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        var token = lifetime.Token;
        var running = new List<Task>();
        var settings = options.Value;
        settings.Validate();
        try
        {
            while (!token.IsCancellationRequested)
            {
                running.RemoveAll(task => task.IsCompleted);
                try
                {
                    await scopeRunner.RunAsync(async (persistenceScope, operation, operationToken) =>
                    {
                        var store = operation.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>();
                        var queue = operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
                        foreach (var item in await store.ListResultReadyAsync(settings.ResultSweepSize, cancellationToken: operationToken))
                            await queue.EnqueueAsync(item.Request.CoordinatorWorkItem, operationToken);

                        // Bound discovery to one admission per persistence scope on each sweep.
                        if (running.Count >= settings.MaxConcurrency)
                            return;
                        var claim = await store.ClaimNextAsync(_ownerId, timeProvider.GetUtcNow(), settings.LeaseDuration, operationToken);
                        if (claim is not null)
                        {
                            // Tracked Task.Run is deliberate: an activity may do synchronous CPU work before its
                            // first await. It must not hold this sweep or serialize its sibling activity bodies.
                            running.Add(Task.Run(() => RunClaimAsync(persistenceScope, claim, token), CancellationToken.None));
                        }
                    }, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    logger.LogWarning("Background activity discovery failed; durable work will be retried on the next sweep");
                }
                await Task.Delay(settings.PollInterval, timeProvider, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            await lifetime.CancelAsync();
            // A body that ignores cancellation may delay graceful shutdown, but cannot submit a result afterward.
            // The host's shutdown deadline can terminate the process; its lease then becomes reclaimable.
            await Task.WhenAll(running);
        }
    }

    private async Task RunClaimAsync(PersistenceScope scope, BackgroundActivityWorkClaim claim, CancellationToken cancellationToken)
    {
        try
        {
            await worker.ExecuteClaimAsync(scope, claim, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            logger.LogWarning("Background work {WorkId} could not finish delivery; durable claim or receipt recovery will retry", claim.WorkId);
        }
    }

    public void Dispose() => _stopping.Dispose();
}
