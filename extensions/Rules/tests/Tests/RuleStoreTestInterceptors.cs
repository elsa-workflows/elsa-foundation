using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Rules.Tests;

internal static class RuleStoreTestInterceptors
{
    internal sealed class SqliteClock(long utcTicks) : DbCommandInterceptor
    {
        private long _utcTicks = utcTicks;

        internal void Set(long utcTicks) => Interlocked.Exchange(ref _utcTicks, utcTicks);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("rules.utc-clock", StringComparison.Ordinal))
                return ValueTask.FromResult(result);

            var table = new DataTable();
            table.Columns.Add("Value", typeof(long));
            table.Rows.Add(Interlocked.Read(ref _utcTicks));
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }

    internal sealed class FirstTwoRunReads : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("elsa_rules_runtime_runs", StringComparison.Ordinal)
                && command.CommandText.Contains("ConcurrencyToken", StringComparison.Ordinal))
            {
                var read = Interlocked.Increment(ref _reads);
                if (read == 2)
                    _bothRead.TrySetResult();
                if (read <= 2)
                    await _bothRead.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    internal sealed class ExpireAfterSave(SqliteClock clock, long expiryUtcTicks) : SaveChangesInterceptor
    {
        private int _armed = 1;

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
                clock.Set(expiryUtcTicks);
            return ValueTask.FromResult(result);
        }
    }
}
