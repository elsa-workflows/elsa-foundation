using System.Data.Common;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelRetentionWorker(RetentionPass pass, BoundScope tenant, Func<CancellationToken, ValueTask<bool>> activate, Func<CancellationToken, ValueTask<bool>> refresh,
    Action unavailable,
    ILogger<KernelRetentionWorker> logger)
{
    private static readonly TimeSpan BetweenPages = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BetweenSweeps = TimeSpan.FromMinutes(1);

    internal async Task RunAsync(CancellationToken token)
    {
        PageToken? next = null;
        var activated = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan pause;
                try
                {
                    var enabled = activated ? await refresh(token) : await activate(token);
                    activated = true;
                    next = enabled ? (await pass.RunAsync(tenant, next, token)).Next : null;
                    pause = next is null ? BetweenSweeps : BetweenPages;
                }
                catch (Exception error) when (error is DbException { IsTransient: true } or TimeoutException)
                {
                    logger.LogWarning(error, "Kernel retention failed for tenant {Tenant}. The sweep will retry.", tenant.Tenant);
                    pause = BetweenSweeps;
                }

                await Task.Delay(pause, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            unavailable();
            logger.LogError(error, "Kernel retention stopped for tenant {Tenant}. Restart the shell after correcting the fault.", tenant.Tenant);
        }
    }
}
