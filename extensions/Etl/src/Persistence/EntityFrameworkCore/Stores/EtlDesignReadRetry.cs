using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Runs a design-store read again after a transient conflict. On SQL Server a read-committed read can be the victim of a
/// deadlock between publications, and a read is safe to repeat. A publication attempt's own reads are not run through
/// this, because its retry runs the whole unit again. It reuses the write retry for its rule that a transient conflict in
/// a transaction still open on the context is rethrown, not retried.
/// </summary>
internal static class EtlDesignReadRetry
{
    private static readonly EfWriteRetry Retry = new(EfWriteRetry.DefaultMaxAttempts, EfWriteConflict.Transient);

    public static ValueTask<T> RunAsync<T>(DbContext context, Func<Task<T>> read, CancellationToken cancellationToken) =>
        Retry.RunAsync(context, async () => await read(), conflict =>
        {
            ExceptionDispatchInfo.Throw(conflict!);
            return ValueTask.FromResult(default(T)!);
        }, cancellationToken);
}
