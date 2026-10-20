using Elsa.Etl.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Renews in separate service scopes so an EF context never runs concurrent operations.</summary>
internal sealed class EtlClaimHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _renewal;
    private int _stopped;
    private int _disposed;
    public CancellationToken Token => _stop.Token;

    public EtlClaimHeartbeat(IServiceScopeFactory scopes,
        Func<IEtlRuntimeStore, CancellationToken, ValueTask<EtlStoreStatus>> renew,
        TimeProvider clock, ILogger logger, CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _renewal = RenewAsync(scopes, renew, clock, logger);
    }

    private async Task RenewAsync(IServiceScopeFactory scopes,
        Func<IEtlRuntimeStore, CancellationToken, ValueTask<EtlStoreStatus>> renew,
        TimeProvider clock, ILogger logger)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var status = await renew(scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>(), _stop.Token);
                    if (status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay))
                    {
                        await _stop.CancelAsync();
                        return;
                    }
                }
                // The store feature enforces at least five seconds per lease.
                await Task.Delay(TimeSpan.FromSeconds(1), clock, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning("ETL claim renewal stopped after {FailureType}; its durable work remains recoverable.", exception.GetType().Name);
            await _stop.CancelAsync();
        }
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
            await _stop.CancelAsync();
        await _renewal;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _stop.Dispose();
    }
}
