using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;

/// <summary>
/// Runs a distributed-runtime compare-and-swap attempt through the shared bounded retry. Each attempt starts and every
/// failure ends with a clear tracker, so a lost race never leaks staged rows into the next attempt or the next caller.
/// </summary>
internal static class EfDistributedCompareAndSwap
{
    /// <param name="mapFailure">The store's exception for a failure the retry does not absorb, or <c>null</c> to let it propagate unchanged.</param>
    /// <param name="exhausted">The store's exception once the budget runs out, given the last lost race.</param>
    public static ValueTask<T> RunAsync<T>(
        DbContext context,
        EfWriteRetry retry,
        Func<Task<T>> attempt,
        Func<Exception, Exception?> mapFailure,
        Func<Exception?, Exception> exhausted,
        CancellationToken cancellationToken) =>
        retry.RunAsync(
            context,
            async () =>
            {
                context.ChangeTracker.Clear();
                try
                {
                    return await attempt();
                }
                catch (Exception exception)
                {
                    context.ChangeTracker.Clear();
                    if (!retry.ShouldRetry(context, exception) && mapFailure(exception) is { } failure)
                        throw failure;
                    throw;
                }
            },
            lastContention => throw exhausted(lastContention),
            cancellationToken);
}
