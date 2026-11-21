namespace Kernel.Review7;

/// <summary>The owner of one partition at one epoch: a real <see cref="Worker"/> that took it through the real <c>TakeAsync</c>.</summary>
sealed class Owner(Worker worker)
{
    public readonly Worker Worker = worker;
    public string Node => Worker.Node;
    public readonly long Epoch = worker.Epoch;
    public long Position = worker.Position;
}

/// <summary>
/// The kernel a case runs on: one real <see cref="InMemoryStore"/> (4 partitions, a harness clock), one owner per
/// partition, and every write a <see cref="CommitBatch"/> through the real <c>CommitAsync</c>, which checks the fence,
/// counts every consumed input and refuses a send whose key exists. Each owner is a real <see cref="Worker"/> taken
/// through its own <c>TakeAsync</c>, so its turns get the epoch's child issuer; its monotonic clock is the harness
/// clock, so every run is deterministic. Turns of the actor type run through the real <see cref="WorkflowInstance"/>;
/// a commit is built from a turn's outcome as Worker.cs 1450–1455 builds one.
/// </summary>
sealed class Kernel
{
    public const int Partitions = 4;
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);
    public long Now = 1000;
    public readonly InMemoryStore Store;
    public readonly WorkflowInstance Type;
    public readonly Report Report;
    public readonly Dictionary<int, Owner> Owners = [];
    /// <summary>Every message a landed commit stored, with its seq.</summary>
    public readonly List<(Message Message, long Seq)> Sent = [];
    private int _nodes;

    public Kernel(Report report, IEnumerable<IExecutionModel> models, Func<InstanceRow, bool>? refuses = null)
    {
        Report = report;
        Type = new WorkflowInstance(models);
        Store = new InMemoryStore(Partitions, () => Now) { Refuses = refuses };
        for (var p = 0; p < Partitions; p++) Become(p);
    }

    public void Line(string text) => Report.Line(text);

    /// <summary>A new node takes <paramref name="partition"/> through the real <c>Worker.TakeAsync</c>.</summary>
    public Owner Become(int partition)
    {
        var node = $"n{partition}-{++_nodes}";
        var before = Store.Partitions[partition].Epoch;
        var worker = new Worker(node, partition, Store, Type, new WorkerOptions { Partitions = Partitions, Lease = Lease, Monotonic = () => Now });
        Real.Take(worker);
        if (worker.Epoch <= before) throw new InvalidOperationException($"{node} could not take partition {partition}");
        return Owners[partition] = new Owner(worker);
    }

    /// <summary>The owner's lease runs out and another node takes the partition at the next epoch.</summary>
    public Owner TakeOver(int partition)
    {
        var old = Owners[partition];
        Now += (long)Lease.TotalMilliseconds + 1;
        var next = Become(partition);
        Line($"t={Now}: {old.Node}'s lease on partition {partition} has run out; {next.Node} takes it through the real Worker.TakeAsync, epoch {old.Epoch} -> {next.Epoch}");
        return next;
    }

    public Owner OwnerOf(string actor) => Owners[new InstanceRef(actor).Partition];

    /// <summary>An outside add through the real <c>AddAsync</c>; returns the stored row's key and seq.</summary>
    public (InboxKey Key, long Seq) AddOutside(Message message)
    {
        if (!Real.Add(Store, message)) throw new InvalidOperationException("outside add refused: " + message.Id);
        var key = message.AsOrdinary().InboxKey;
        return (key, Store.Messages[key].Seq);
    }

    /// <summary>One commit of <paramref name="actor"/>'s partition owner through the real <c>CommitAsync</c>.</summary>
    public (bool Landed, string? Error) Commit(string actor, IReadOnlyList<TurnCommit> turns)
    {
        var owner = OwnerOf(actor);
        var batch = new CommitBatch(owner.Worker.Partition, owner.Epoch, owner.Position, Lease, false, turns, [], []);
        Committed committed;
        try { committed = Real.Commit(Store, batch); }
        catch (StoreException e) { return (false, $"StoreException ({e.Failure}): {e.Message}"); }
        if (!committed.Landed) return (false, "fence");
        owner.Position = batch.Position + batch.Positions;
        var sent = turns.SelectMany(t => t.Sent).ToList();
        for (var i = 0; i < sent.Count; i++) Sent.Add((sent[i], committed.Seqs[i]));
        return (true, null);
    }

    /// <summary>A turn's outcome as Worker.cs 1450–1455 builds its commit: durable consumed rows, then the cancelled tickets.</summary>
    public static TurnCommit ToTurnCommit(TurnOutcome u, IReadOnlyList<InboxKey>? cancels = null) => new(u.Instance, u.Row, u.Expected,
        [.. u.Consumed.Where(i => Real.Durable(i) && i.Message.KeepUntil is null).Select(i => i.Message.InboxKey), .. cancels ?? u.Cancelled],
        u.Consumed.Where(i => Real.Durable(i) && i.Message.KeepUntil is not null).Select(i => i.Message.InboxKey).ToList(),
        u.Sent.Where(m => !u.Volatile.Contains(m.InboxKey)).ToList(), u.Subscribed, u.Unsubscribed, u.Journal, u.Unsubscribe,
        u.Held.Where(Real.Durable).Select(i => i.Message.InboxKey).ToList(), u.Delete);
}

/// <summary>A harness model with no metadata, for rows whose model never runs a turn in these cases.</summary>
sealed class StubModel : IExecutionModel
{
    public string Key => "stub";
    public Type StateType => typeof(Dictionary<string, string>);
    public int StateVersion => 1;
    public object CreateState(StartSpec start) => new Dictionary<string, string>();
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken) => throw new NotSupportedException("no model turn is modelled");
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>
/// A harness model turn that stands for an authored model's detached starts (no authored workflow drives it; K6). It
/// keeps the Native state as it is and starts <paramref name="count"/> detached children, spread over the partitions,
/// through the real <c>ITurn.Start</c> (Turn.cs 53–61), which calls the real <c>DurableTurn.StartChild</c>
/// (Turns.cs 364–376). Its key is Native's, so the actor type reads and writes the Native row as it is.
/// </summary>
sealed class Spawner(int stateVersion, int count) : IExecutionModel
{
    public string Key => NativeModel.ModelKey;
    public Type StateType => typeof(NativeState);
    public int StateVersion => stateVersion;
    public object CreateState(StartSpec start) => throw new NotSupportedException();
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++) turn.Start(new StartSpec(NativeModel.ModelKey, "detached"), Placement.Spread, detached: true);
        return default;
    }
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
}
