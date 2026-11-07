using System.Transactions;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    private static readonly EfWriteRetry OperationRetry = new(EfWriteRetry.DefaultMaxAttempts,
        exception => exception is not (EtlStoreException or SchemaWriteRefusedException or OperationCanceledException) &&
                     (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey) ||
                      EfRelationalExceptionClassifier.IsTransientWriteConflict(exception)),
        // Spread serialization-conflict bursts without increasing the operation's attempt budget.
        delay: number => TimeSpan.FromMilliseconds(Random.Shared.Next(1, Math.Min(1000, 10 << Math.Min(number, 7)) + 1)));

    private void PrepareOperation(string operation, EtlScope? scope, string? runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // This store owns its complete unit. Refuse before clearing caller tracking or issuing SQL.
            if (db.Database.CurrentTransaction is not null || Transaction.Current is not null ||
                db.Database.GetEnlistedTransaction() is not null)
                throw new EtlStoreException(operation, scope, runId,
                    new InvalidOperationException("ETL store operations do not participate in caller-owned transactions."));
            db.ChangeTracker.Clear();
        }
        catch (Exception exception) when (ShouldMapStoreFailure(exception))
        {
            throw new EtlStoreException(operation, scope, runId, exception);
        }
    }

    private async ValueTask<T> RunOperationAsync<T>(string operation, EtlScope? scope, string? runId,
        Func<ValueTask<T>> attempt, CancellationToken cancellationToken)
    {
        try
        {
            return await OperationRetry.RunAsync(db, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                db.ChangeTracker.Clear();
                try
                {
                    return await attempt();
                }
                catch
                {
                    // The attempt's await-using transaction has already disposed before this catch.
                    db.ChangeTracker.Clear();
                    throw;
                }
            }, static conflict => ValueTask.FromException<T>(conflict!), cancellationToken);
        }
        catch (Exception exception) when (ShouldMapStoreFailure(exception))
        {
            throw new EtlStoreException(operation, scope, runId, exception);
        }
    }

    private static bool ShouldMapStoreFailure(Exception exception) =>
        exception is not (EtlStoreException or SchemaWriteRefusedException) &&
        EfRelationalExceptionClassifier.IsStoreBoundaryFailure(exception);
}
