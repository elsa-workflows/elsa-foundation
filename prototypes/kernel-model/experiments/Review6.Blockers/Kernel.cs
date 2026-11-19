namespace Kernel.Review6;

/// <summary>The owner of one partition at one epoch, fenced through the real <c>TakeAsync</c>.</summary>
sealed class Owner(string node, int partition, Lease lease, Worker worker)
{
    public readonly string Node = node;
    public readonly int Partition = partition;
    public readonly long Epoch = lease.Epoch;
    public long Position = lease.Position;
    public readonly Worker Worker = worker;
}

/// <summary>
/// The kernel a case runs on: one real <see cref="InMemoryStore"/> (4 partitions, a harness clock), one owner per
/// partition, and every write a <see cref="CommitBatch"/> through the real <c>CommitAsync</c>, which checks the fence,
/// counts every consumed input and refuses a send whose key exists. Turns of the actor type run through the real
/// <see cref="WorkflowInstance"/>; a commit is built from a turn's outcome as Worker.cs 1450–1455 builds one.
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

    public Kernel(Report report, IEnumerable<IExecutionModel> models)
    {
        Report = report;
        Type = new WorkflowInstance(models);
        Store = new InMemoryStore(Partitions, () => Now);
        for (var p = 0; p < Partitions; p++) Become(p);
    }

    public void Line(string text) => Report.Line(text);

    private Owner Become(int partition)
    {
        var node = $"n{partition}-{++_nodes}";
        var lease = Real.Take(Store, partition, node, Lease) ?? throw new InvalidOperationException($"{node} could not take partition {partition}");
        return Owners[partition] = new Owner(node, partition, lease, new Worker(node, partition, Store, Type, new WorkerOptions { Partitions = Partitions }));
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
        var batch = new CommitBatch(owner.Partition, owner.Epoch, owner.Position, Lease, false, turns, [], []);
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
