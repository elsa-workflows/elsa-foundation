using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Native.Run;

/// <summary>One node's owner of one partition, with the native model; its live output goes over the network's transport.</summary>
public sealed class Owner
{
    public Owner(string node, int partition, IKernelStore store, WorkflowRegistry registry, Network network, Func<long>? monotonic = null, NativeOptions? native = null, TimeSpan? lease = null)
    {
        Requests = network.Node(node);
        Model = new NativeModel(registry, new NativeOptions { Flaws = native?.Flaws ?? NativeFlaw.None, Gate = native?.Gate, OnExternal = native?.OnExternal });
        Worker = new Worker(node, partition, store, new WorkflowInstance([Model, new Kernel.Sim.Sequence()]), new WorkerOptions
        {
            Monotonic = monotonic ?? (static () => Environment.TickCount64),
            Lease = lease ?? Defaults.Lease,
            Transport = network.From(node),
        });
        Host = new DurableHost([Worker]);
        Http = new HttpHost(node, Requests, new HttpRoutes(registry), store);
    }

    public Worker Worker { get; }
    public DurableHost Host { get; }
    public Requests Requests { get; }
    public NativeModel Model { get; }
    public HttpHost Http { get; }

    public async Task StartAsync()
    {
        await Host.ActivateAsync(Worker.Partition);
        await Worker.PollAsync();   // caught up: from here on it hands over in memory
    }

    public InstanceStatus? Status(string id) => Worker.Instances.FirstOrDefault(i => i.Id == id).Row?.Status;

    public NativeState? State(string id) => Worker.Instances.FirstOrDefault(i => i.Id == id) is { State: { } json } ? Json.Read<NativeState>(json)
        : Worker.Instances.FirstOrDefault(i => i.Id == id).Row is { } row ? Json.Read<NativeState>(row.State) : null;

    /// <summary>
    /// Run what is ready until nothing is, or until <paramref name="until"/> holds and nothing is busy. Polls once at the
    /// start when asked: work started on the owner needs no poll, so none is counted.
    /// </summary>
    public async Task PumpAsync(bool poll = false, Func<bool>? until = null, int timeoutMs = 10_000, bool keepPolling = false)
    {
        if (poll) await Worker.WokenAsync(null);     // the first pass reads the inbox
        var deadline = Environment.TickCount64 + timeoutMs;
        for (var idle = 0; ;)
        {
            var busy = await Worker.RunAsync() == TimeSpan.Zero;     // one pass
            if (until is null)
            {
                idle = busy ? 0 : idle + 1;
                if (idle >= 2) return;
                continue;
            }
            if (until() && !busy) return;
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("the pump did not reach its condition; in memory:\n  " + string.Join("\n  ",
                    Worker.Instances.Select(i => $"{i.Id} {i.Row?.Status} r{i.Row?.Revision} busy={i.Busy} {i.State ?? i.Row?.State}"))
                    + "\n  messages: " + string.Join(", ", Worker.Messages.Select(m => m.Id)));
            if (!busy) await Task.Delay(1);
            if (!busy && keepPolling) await Worker.WokenAsync(null);    // the next pass reads the inbox
        }
    }
}

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
    private void Seen(DbCommand command) { Commands++; Trace?.Add(command.CommandText.ReplaceLineEndings(" ")[..Math.Min(140, command.CommandText.Length)]); }
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
    public bool CreationIdentityAvailable => inner.CreationIdentityAvailable;
    public bool CommitClockAvailable => inner.CommitClockAvailable;
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
