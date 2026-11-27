using System.Data.Common;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed record KernelRetentionPolicy(TimeSpan? Finished, TimeSpan? Cancelled, TimeSpan? Faulted)
{
    internal bool Enabled => Finished.HasValue || Cancelled.HasValue || Faulted.HasValue;

    internal TimeSpan? Window(ActorHeader header) => header.Status switch
    {
        ActorStatus.Finished => Finished,
        ActorStatus.Cancelled => Cancelled,
        ActorStatus.Faulted => Faulted,
        _ => null
    };
}

internal sealed class KernelRetentionWorker(RetentionPass pass, BoundScope tenant, ILogger<KernelRetentionWorker> logger)
{
    private static readonly TimeSpan BetweenPages = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BetweenSweeps = TimeSpan.FromMinutes(1);

    internal async Task RunAsync(CancellationToken token)
    {
        PageToken? next = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan pause;
                try
                {
                    next = (await pass.RunAsync(tenant, next, token)).Next;
                    pause = next is null ? BetweenSweeps : BetweenPages;
                }
                catch (Exception error) when (error is DbException or TimeoutException)
                {
                    logger.LogWarning(error, "Kernel retention failed for tenant {Tenant}. The sweep will retry.", tenant.Tenant);
                    pause = BetweenSweeps;
                }

                await Task.Delay(pause, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
