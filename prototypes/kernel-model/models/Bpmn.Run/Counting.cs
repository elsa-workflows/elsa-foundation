using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Bpmn.Run;

// Copied from models/Native.Run/Hosting.cs, so the two models count the same way and neither project depends on the other;
// the trace here names each statement of a batched command.

/// <summary>Counts what Entity Framework sends: commands, and the begin and end of transactions.</summary>
public sealed class Counting : DbCommandInterceptor
{
    public sealed class TransactionCounting : DbTransactionInterceptor
    {
        public int Begun, Ended;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) { Begun++; return new(result); }
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) { Begun++; return result; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { Ended++; return Task.CompletedTask; }
        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Ended++;
    }

    public readonly TransactionCounting Transactions = new();
    public int Commands;

    public List<string>? Trace;
    // The trace lists each statement of a command by verb and table: EF batches several into one command.
    private static readonly System.Text.RegularExpressions.Regex Statement = new(@"(INSERT INTO|UPDATE|DELETE FROM|SELECT\b.*?\bFROM)\s+\(?\s*(SELECT \* FROM\s+)?""?(\w+)""?", System.Text.RegularExpressions.RegexOptions.Singleline);
    private void Seen(DbCommand command)
    {
        Commands++;
        Trace?.Add(string.Join("; ", Statement.Matches(command.CommandText).Select(m => (m.Groups[1].Value.StartsWith("SELECT", StringComparison.Ordinal) ? "SELECT FROM" : m.Groups[1].Value) + " " + m.Groups[3].Value)));
    }
    public void Reset() => (Commands, Transactions.Begun, Transactions.Ended) = (0, 0, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Seen(command); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Seen(command); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Seen(command); return result; }
}

/// <summary>Passes everything through; counts commits and the bytes of instance state they write.</summary>
public sealed class Sizing(IKernelStore inner) : IKernelStore
{
    public int Commits, Rows;
    public long StateBytes, MaxStateBytes;

    public void Reset() => (Commits, Rows, StateBytes, MaxStateBytes) = (0, 0, 0, 0);

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default)
    {
        Commits++;
        foreach (var turn in batch.Turns)
            if (turn.Row is { } row)
            {
                Rows++;
                StateBytes += row.State.Length;
                MaxStateBytes = Math.Max(MaxStateBytes, row.State.Length);
            }
        return inner.CommitAsync(batch, cancellationToken);
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) => inner.TakeAsync(partition, node, lease, cancellationToken);
    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => inner.ReleaseAsync(partition, epoch, cancellationToken);
    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) => inner.PollAsync(partition, epoch, horizon, limit, cancellationToken);
    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => inner.AddAsync(message, cancellationToken);
    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => inner.LoadAsync(ids, cancellationToken);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => inner.PublishAsync(@event, cancellationToken);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => inner.FindSubscriptionsAsync(key, cancellationToken);
    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) => inner.FindEventsAsync(keys, cancellationToken);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => inner.ReadJournalAsync(partition, after, take, cancellationToken);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => inner.SweepAsync(cancellationToken);
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => inner.HeartbeatAsync(node, window, cancellationToken);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => inner.SubscribeAsync(subscription, cancellationToken);
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) => inner.ProjectedAsync(partition, cancellationToken);
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) => inner.ProjectAsync(partition, from, to, rows, cancellationToken);
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => inner.ReadViewsAsync(instances, cancellationToken);
}
