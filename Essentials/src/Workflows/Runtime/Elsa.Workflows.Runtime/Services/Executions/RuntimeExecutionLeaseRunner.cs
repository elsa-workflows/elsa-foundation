using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>The existing native drain's lease/heartbeat/release boundary, shared with model dispatch.</summary>
internal sealed class RuntimeExecutionLeaseRunner(
    IRuntimeExecutionOwnershipService ownershipService,
    IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory? heartbeatScopes,
    TimeProvider timeProvider)
{
    public async ValueTask<T> RunAsync<T>(string executionId, WorkflowExecutionPartition partition,
        Func<CancellationToken, ValueTask<T>> run, CancellationToken cancellationToken) where T : class
    {
        var lease = await ownershipService.AcquireAsync(executionId, cancellationToken);
        using (ownershipContext.Push(lease))
        using (var renewalStop = new CancellationTokenSource())
        using (var drainCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var renewalTask = RenewUntilStoppedAsync(lease, partition, renewalStop.Token, drainCancellation);
            T? result = null;
            Exception? runFailure = null;
            Exception? renewalFailure = null;
            Exception? releaseFailure = null;
            try
            {
                try { result = await run(drainCancellation.Token); }
                catch (Exception exception) { runFailure = exception; }
            }
            finally
            {
                await renewalStop.CancelAsync();
                try { await renewalTask; }
                catch (OperationCanceledException) when (renewalStop.IsCancellationRequested) { }
                catch (Exception exception) { renewalFailure = exception; }

                try
                {
                    var release = await ownershipService.ReleaseAsync(lease, CancellationToken.None);
                    if (!release.Succeeded && release.Status != RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied)
                        releaseFailure = new RuntimeExecutionOwnershipLostException(lease, "release", release.Status);
                }
                catch (Exception exception)
                {
                    releaseFailure = new RuntimeExecutionOwnershipLostException(lease, "release", transitionStatus: null, exception);
                }
            }

            // Preserve the native drain's failure precedence: lost authority wins over the work's failure.
            if (renewalFailure is not null) ExceptionDispatchInfo.Capture(renewalFailure).Throw();
            if (runFailure is not null) ExceptionDispatchInfo.Capture(runFailure).Throw();
            if (releaseFailure is not null) ExceptionDispatchInfo.Capture(releaseFailure).Throw();
            return result ?? throw new InvalidOperationException("Workflow execution draining completed without a result.");
        }
    }

    private async Task RenewUntilStoppedAsync(RuntimeExecutionLease lease, WorkflowExecutionPartition partition,
        CancellationToken stopToken, CancellationTokenSource drainCancellation)
    {
        var duration = lease.ExpiresAt - lease.AcquiredAt;
        var cadence = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
        while (true)
        {
            await Task.Delay(cadence, timeProvider, stopToken);
            RuntimeExecutionOwnershipTransitionResult heartbeat;
            try { heartbeat = await HeartbeatAsync(lease, partition, stopToken); }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                await drainCancellation.CancelAsync();
                throw new RuntimeExecutionOwnershipLostException(lease, "heartbeat", transitionStatus: null, exception);
            }
            if (heartbeat.Succeeded || heartbeat.Status == RuntimeExecutionOwnershipTransitionStatus.AlreadyApplied) continue;
            await drainCancellation.CancelAsync();
            throw new RuntimeExecutionOwnershipLostException(lease, "heartbeat", heartbeat.Status);
        }
    }

    private async ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease,
        WorkflowExecutionPartition partition, CancellationToken cancellationToken)
    {
        if (heartbeatScopes is null) return await ownershipService.HeartbeatAsync(lease, cancellationToken);
        await using var scope = await heartbeatScopes.CreateAsync(new PersistenceScope(partition.Value), cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<IRuntimeExecutionOwnershipService>().HeartbeatAsync(lease, cancellationToken);
    }
}
