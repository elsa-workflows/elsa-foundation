using Kernel.Native;

namespace Kernel.Sim;

// Native and NativeFlaws: native workflow instances in the mix (sim/NativeSim.cs), and the model broken on purpose.
public sealed record Config(int Nodes, int Partitions, Flaw Flaws, int Attempts = 3, int Group = 1, bool Upgrade = false, bool Native = false, NativeFlaw NativeFlaws = NativeFlaw.None);

/// <summary>One thing that happens. Concrete, so that a list of them replays without the random generator.</summary>
public sealed record Act(string Kind, int A = 0, int B = 0, string S = "", Message? M = null, bool Flag = false)
{
    public override string ToString() => Kind switch
    {
        "add" => $"outside adds {M!.Id} ({M.Kind} to {M.Target}{(S == "" ? "" : ", " + S)})",
        "post" => $"actor {A} is handed {M!.Id} in memory (start of {M.Target})",
        "publish" => $"outside publishes event e{B} for key {S}",
        "turn" => $"actor {A} runs a turn of {S}",
        "exec" => $"the database executes call {B} of actor {A}",
        "lose" => $"call {B} of actor {A} never reaches the database ({(Flag ? "deadlock victim" : "connection lost")})",
        "answer" => $"actor {A} gets the answer to call {B}{(Flag ? " as 'connection lost'" : "")}",
        "finish" => $"job {S} completes on actor {A}",
        "tick" => $"{A} ms pass",
        "skew" => $"the wall clock of node {A} jumps {B} ms",
        "crash" => $"node {A} crashes and restarts{(Flag ? "; the old process lives on" : "")}",
        "release" => $"actor {A} releases its partition",
        "balance" => $"node {A} balances: heartbeat, release the surplus, take up to its share",
        "pause" => $"node {A} pauses",
        "resume" => $"node {A} resumes",
        "project" => $"the projector of actor {A} runs a round",
        "sweep" => "the sweep runs",
        "register" => $"an outsider registers start subscription {S} for key {M!.Kind}",
        "catch-up" => $"the registrar of {S} reads the events of its key",
        "request" => $"a request on actor {A} starts {M!.Target} ({S}) and waits for its response",
        "resume-request" => $"a request on node {A} sends {M!.Id} (resume to {M.Target}) with a reply address and waits",
        "disconnect" => $"the caller of request {S} goes away{(Flag ? " and asks for a cancel" : "")}",
        "deliver" => $"the transport delivers reply {B}{(Flag ? " and keeps a copy to deliver again" : "")}",
        "drop" => $"the transport drops reply {B}",
        _ => $"actor {A}: {Kind}",
    };
}

/// <summary>A store call on the wire: the request executes at one moment, its answer arrives at another, or never.</summary>
public sealed class Call(string kind, string text, Func<object?> run, Action<object?> ok, Action<Exception> fail)
{
    public string Kind => kind;
    public string Text { get; private set; } = text;
    public bool Executed, Answered;
    private object? _result;
    private Exception? _error;

    public string Execute(Func<object?, string> describe)
    {
        Executed = true;
        try { _result = run(); }
        catch (Exception e) { _error = e; }
        return Text += " -> " + (_error?.Message ?? describe(_result));
    }

    public void Lose(bool transient)
    {
        Executed = true;
        _error = new StoreException(transient ? Failure.Transient : Failure.Unknown, transient ? "deadlock victim" : "connection lost");
    }

    public void Answer(bool lie)
    {
        Answered = true;
        if (lie) fail(new StoreException(Failure.Unknown, "connection lost"));
        else if (_error is not null) fail(_error);
        else ok(_result);
    }
}

public sealed class Actor(int id, int node, int partition)
{
    public int Id => id;
    public int Node => node;
    public int Partition => partition;
    public Worker Worker = null!;
    public Projector Projector = null!;
    public TrialGate Gate = null!;
    public bool Dead, Zombie;
    public int Version;
    public readonly List<Call> Calls = [];
    public readonly Dictionary<string, TaskCompletionSource<object?>> Jobs = new(StringComparer.Ordinal);
}

/// <summary>What a worker sees as its store: the shared in-memory store behind a wire the simulation controls.</summary>
public sealed class Wire(World world, Actor actor) : IKernelStore
{
    private ValueTask<T> Send<T>(string kind, string text, Func<ValueTask<T>> op)
    {
        if (world.Auto)
        {
            try
            {
                var done = op();
                world.Log?.Invoke($"        actor {actor.Id}: {text} -> {(done.IsCompletedSuccessfully ? Wire.Describe(done.Result) : "failed")}");
                return done;
            }
            catch (Exception e)
            {
                world.Log?.Invoke($"        actor {actor.Id}: {text} -> {e.Message}");
                return ValueTask.FromException<T>(e);
            }
        }
        var answer = new TaskCompletionSource<T>();     // continuations run inline when the simulation delivers the answer
        actor.Calls.Add(new Call(kind, text, () => op().Result, r => answer.SetResult((T)r!), answer.SetException));
        return new ValueTask<T>(answer.Task);
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) =>
        Send("take", $"take p{partition}", () => world.Take(partition, node, lease));

    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => world.Store.ReleaseAsync(partition, epoch);
    public ValueTask<Cluster> HeartbeatAsync(string node, TimeSpan window, CancellationToken cancellationToken = default) => world.Store.HeartbeatAsync(node, window);

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default) =>
        Send("commit", $"commit p{batch.Partition} e{batch.Epoch} @{batch.Position} [{string.Join(", ", batch.Turns.Select(Describe))}]"
            + (batch.Seen.Count > 0 ? $" seen [{string.Join(", ", batch.Seen)}]" : "") + (batch.Trial.Count > 0 ? $" on trial [{string.Join(", ", batch.Trial)}]" : ""),
            () => world.Store.CommitAsync(batch) is var landed && landed.Result.Landed && batch.Trial.Count > 0 ? Counted(landed, batch.Trial.Count) : landed);

    private static ValueTask<Committed> Counted(ValueTask<Committed> landed, int trials)
    {
        World.Count("put on trial", trials);
        return landed;
    }

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        Send("poll", $"poll p{partition} e{epoch}", () => world.Store.PollAsync(partition, epoch, horizon, limit));

    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        Send("load", $"load [{string.Join(", ", ids)}]", () => world.Store.LoadAsync(ids));

    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) =>
        Send("events", $"events for [{string.Join(", ", keys)}]", () => world.Store.FindEventsAsync(keys));

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => world.Store.AddAsync(message);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => world.Store.PublishAsync(@event);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => world.Store.FindSubscriptionsAsync(key);
    public ValueTask SubscribeAsync(SubscriptionRow subscription, CancellationToken cancellationToken = default) => world.Store.SubscribeAsync(subscription);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) =>
        Send("journal", $"journal p{partition} after {after}", () => world.Store.ReadJournalAsync(partition, after, take));
    public ValueTask<Projection> ProjectedAsync(int partition, CancellationToken cancellationToken = default) =>
        Send("projected", $"projection p{partition}", () => world.Store.ProjectedAsync(partition));
    public ValueTask<bool> ProjectAsync(int partition, long from, long to, IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken = default) =>
        Send("project", $"project p{partition} from {from} to {to}", () => world.Store.ProjectAsync(partition, from, to, rows));
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => world.Store.SweepAsync();
    public ValueTask<IReadOnlyList<ViewRow>> ReadViewsAsync(IReadOnlyCollection<string> instances, CancellationToken cancellationToken = default) => world.Store.ReadViewsAsync(instances);

    private static string Describe(TurnCommit turn) =>
        (turn.Row is { } row ? $"{row.Id} r{row.Revision} {row.Status}" : turn.Instance == "" ? "tickets" : $"{turn.Instance} (no row)")
        + string.Concat(turn.Consumed.Concat(turn.Done).Select(id => " -" + id)) + (turn.Sent.Count > 0 ? $" +{string.Join(" +", turn.Sent.Select(m => m.Id))}" : "");

    public static string Describe(object? result) => result switch
    {
        null => "refused",
        Lease lease => $"epoch {lease.Epoch}, position {lease.Position}",
        Committed committed => committed.Landed ? "landed" : "the fence matched nothing",
        Polled polled => $"[{string.Join(", ", polled.Messages.Select(m => m.Message.Id))}]",
        IReadOnlyList<InstanceRow> rows => $"[{string.Join(", ", rows.Select(r => $"{r.Id} r{r.Revision}"))}]",
        IReadOnlyList<EventRow> events => $"[{string.Join(", ", events.Select(e => e.Id))}]",
        IReadOnlyList<JournalRow> rows => $"[{string.Join(", ", rows.Select(j => j.Position))}]",
        _ => result.ToString()!,
    };
}

/// <summary>Several nodes against one store, a clock, and an oracle of what was sent.</summary>
public sealed partial class World
{
    public readonly Config Config;
    public readonly InMemoryStore Store;
    public readonly List<Actor> Actors = [];
    public readonly int[,] Current;
    public readonly long[] Skew;
    public readonly int[] Incarnation;
    public readonly int[] Versions;     // the counter model's version on each node: a rolling upgrade moves it from 1 to 2 at a restart
    public readonly Balancer[] Balancers;
    public readonly Dictionary<string, Requests> Processes = new(StringComparer.Ordinal);   // by process name: the requests open there
    public readonly List<(string Process, Request Request, string Instance)> Asked = [];
    public readonly List<(string Node, Reply Reply)> Deliveries = [];                   // on the transport between nodes
    public readonly List<SubscriptionRow> CatchingUp = [];                              // start subscriptions inserted, their read still owed
    private readonly TrialGate[] _gates;
    public readonly bool[] Paused;
    public readonly List<string> Faults = [];
    private readonly List<Task> _watched = [];
    public long Time = 1_000_000;
    public bool Auto, Deployed;
    public int? Dying;
    public Action<string>? Log;

    public World(Config config)
    {
        Config = config;
        Native = new NativeOracle(config.Partitions);
        Store = new InMemoryStore(config.Partitions, () => Time, config.Flaws) { Refuses = row => row.State.Contains(Fragile.Sour) };
        Current = new int[config.Nodes, config.Partitions];
        Skew = new long[config.Nodes];
        Paused = new bool[config.Nodes];
        Incarnation = new int[config.Nodes];
        Versions = Enumerable.Range(0, config.Nodes).Select(node => config.Upgrade && node % 2 == 0 ? 1 : 2).ToArray();     // a deploy in progress
        Balancers = new Balancer[config.Nodes];
        _gates = Enumerable.Range(0, config.Nodes).Select(_ => new TrialGate()).ToArray();
        for (var node = 0; node < config.Nodes; node++)
        {
            for (var partition = 0; partition < config.Partitions; partition++)
                Spawn(node, partition);
            Balance(node);
        }
    }

    // One balancer per process: the workers of the node's current incarnation. Its heartbeat window spans two rounds of the settling loop.
    private void Balance(int node)
    {
        Processes[Of(node, 0).Worker.Node] = new Requests(Config.Flaws);
        Balancers[node] = new Balancer(Of(node, 0).Worker.Node, Store, Enumerable.Range(0, Config.Partitions).Select(p => Of(node, p).Worker).ToList(), TimeSpan.FromSeconds(25), Config.Flaws);
    }

    /// <summary>A process is alive while any of its workers is.</summary>
    public bool Running(string process) => Actors.Exists(a => !a.Dead && a.Worker.Node == process);

    /// <summary>The runtime's transport: a call inside one process; between nodes it may drop, delay or repeat.</summary>
    private sealed class Transport(World world, string self) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            if (node == self) world.Processes[node].Receive(reply);
            else world.Deliveries.Add((node, reply));
        }
    }

    public Actor Of(int node, int partition) => Actors[Current[node, partition]];
    public IEnumerable<Actor> Alive => Actors.Where(a => !a.Dead);
    private Actor? Live(int id) => id >= 0 && id < Actors.Count && Actors[id] is { Dead: false } actor && !Paused[actor.Node] ? actor : null;

    private void Spawn(int node, int partition)
    {
        var actor = new Actor(Actors.Count, node, partition);
        var wire = new Wire(this, actor);
        actor.Version = Versions[node];
        actor.Projector = new Projector(wire, partition, take: 5, Config.Flaws);    // a projector runs on every node, whoever owns the partition
        IExecutionModel[] models =
        [
            new Counter(Versions[node]), new Parent(Config.Flaws.HasFlag(Flaw.CancelKnownChildren)), new Child(), new Waiter(), new Sequence(), new Correlation(Config.Flaws.HasFlag(Flaw.CorrelationForgets)), new CWaiter(Config.Partitions),
            new Runner((job, cancel) => Work(actor, job, cancel)), new Fragile(() => Dying = node), new Http(actor.Id, (job, cancel) => Work(actor, job, cancel)),
            new Tree((job, cancel) => Work(actor, job, cancel)), new OnEvent(), NativeModelFor(actor),
        ];
        // A node's name is unique per process start: a restarted process is not its predecessor (finding F9).
        actor.Worker = new Worker(Config.Flaws.HasFlag(Flaw.StableNodeName) ? "node" + node : $"node{node}.{Incarnation[node]}", partition, wire, models, new WorkerOptions
        {
            Lease = TimeSpan.FromSeconds(10), Horizon = TimeSpan.FromSeconds(2), PollLimit = 3, MaxBatch = 4, MaxAttempts = Config.Attempts, TrialGroup = Config.Group, Gate = _gates[node],
            Monotonic = () => Time + node * 7919,       // every node counts at the database's rate from its own zero
            WallClock = () => Time + Skew[node],
            Flaws = Config.Flaws, Partitions = Config.Partitions, MaxInstances = 2,
            Transport = new Transport(this, Config.Flaws.HasFlag(Flaw.StableNodeName) ? "node" + node : $"node{node}.{Incarnation[node]}"),
            OnTurn = (instance, inbox, _) =>
            {
                Log?.Invoke($"        actor {actor.Id} turns {instance} with [{string.Join(", ", inbox.Select(m => m.Kind + " " + m.Id))}]");
                Alone(actor, instance.Id, "a turn of " + instance);
                // I16: while its committed row says suspended, the current owner gives an instance nothing.
                if (actor.Worker.Epoch == Store.Partitions[actor.Partition].Epoch && Store.Instances.GetValueOrDefault(instance.Id) is { Suspended: true })
                    Faults.Add($"I16 actor {actor.Id} ran a turn of {instance} while it was suspended: [{string.Join(", ", inbox.Select(m => m.Id))}]");
                foreach (var message in inbox)
                    if (message.Due > Time) Faults.Add($"I5 {message.Id} was delivered to {instance} {message.Due - Time} ms before it was due");
            },
        });
        Actors.Add(actor);
        Current[node, partition] = actor.Id;
    }

    /// <summary>The take, watched: taking from another owner means that owner was lost, the only thing that may raise attempts.</summary>
    public ValueTask<Lease?> Take(int partition, string node, TimeSpan lease)
    {
        var (owner, epoch) = (Store.Partitions[partition].Owner, Store.Partitions[partition].Epoch);
        var taken = Store.TakeAsync(partition, node, lease);
        var lost = taken.Result is not null && owner is not null && owner != node;
        if (lost) Count("owners lost");
        foreach (var row in Store.Messages.Values)
        {
            // I14, the counting half: one more attempt for exactly the rows that were on trial under the owner that was lost.
            var onTrial = lost && !row.Done && row.Trial == epoch && row.Message.Target.Partition == partition;
            if (onTrial) Count("attempts charged");
            var expected = _attempts.GetValueOrDefault(row.Message.Id) + (onTrial ? 1 : 0);
            if (row.Attempts != expected) Faults.Add($"I14 the take of partition {partition} by {node} left {row.Message.Id} at {row.Attempts} attempts; it should be {expected}");
            _attempts[row.Message.Id] = row.Attempts;
        }
        return taken;
    }

    // Invariant 14, the "alone" half: while rows are on trial at the current epoch of a partition this process owns, the
    // process starts nothing but them. A death kills every worker of the process, so alone means alone in the process.
    private void Alone(Actor actor, string instance, string what)
    {
        foreach (var sibling in Actors.Where(a => !a.Dead && a.Worker.Node == actor.Worker.Node))
        {
            var partition = Store.Partitions[sibling.Partition];
            if (!sibling.Worker.Owns || sibling.Worker.Epoch != partition.Epoch) continue;
            var trial = Store.Messages.Values.Where(m => !m.Done && m.Trial == partition.Epoch && m.Message.Target.Partition == sibling.Partition).ToList();
            if (trial.Count > 0 && !trial.Exists(m => m.Message.Target.Id == instance)) Faults.Add($"I14 {what} started on actor {actor.Id} while {trial[0].Message.Id} was on trial in partition {sibling.Partition} of the same process");
        }
    }

    private ValueTask<object?> Work(Actor actor, IJob job, CancellationToken cancel)
    {
        Alone(actor, job.Instance.Id, $"job {job.Key} of {job.Instance}");
        var done = new TaskCompletionSource<object?>();
        actor.Jobs[job.Instance.Id + "!" + job.Key] = done;
        cancel.Register(() => done.TrySetCanceled());
        return new ValueTask<object?>(done.Task);
    }

    private void Crash(int node, bool zombie)
    {
        Paused[node] = false;
        Incarnation[node]++;
        Versions[node] = Config.Upgrade && !Deployed && (Incarnation[node] + node) % 2 == 0 ? 1 : 2;     // a rolling deploy, with rollbacks
        _gates[node] = new TrialGate();     // a new process; a zombie keeps the old one
        foreach (var actor in Actors.Where(a => a.Node == node && !a.Dead).ToList())
        {
            // The old process is gone, or (zombie) lives on with its memory beside the new one under the same name.
            if (actor.Zombie || !zombie) actor.Dead = true;
            else actor.Zombie = true;
            if (Current[node, actor.Partition] == actor.Id) Spawn(node, actor.Partition);
        }
        Balance(node);
    }

    private void Watch(Task task)
    {
        if (task.IsFaulted) Faults.Add("BUG " + task.Exception!.InnerException);
        else if (!task.IsCompleted) _watched.Add(task);
    }

    /// <summary>Do it if it can be done now. Null means it was not enabled and nothing happened.</summary>
    public string? Apply(Act act)
    {
        var outcome = Do(act);
        foreach (var requests in Processes.Values) requests.Expire(Time);     // a caller's own deadline
        if (outcome is null || Dying is not { } node) return outcome;
        Dying = null;
        Crash(node, zombie: false);
        return outcome + $"; the turn killed node {node}";
    }

    private string? Do(Act act)
    {
        switch (act.Kind)
        {
            case "take" or "poll" or "load" or "flush" or "beat" or "meet" or "release":
            {
                if (Live(act.A) is not { } actor) return null;
                var (worker, calls) = (actor.Worker, actor.Calls.Count);
                switch (act.Kind)
                {
                    case "take" when !worker.Owns && !worker.Taking: Watch(worker.TakeAsync()); break;
                    case "poll" when worker.Owns && !worker.Polling: Watch(worker.PollAsync()); break;
                    case "load" when worker.Owns && !worker.Loading && worker.NeedsLoad: Watch(worker.LoadAsync()); break;
                    case "flush" when worker.Owns && !worker.Flushing && worker.NeedsFlush: Watch(worker.FlushAsync()); break;
                    case "beat" when worker.Owns && !worker.Flushing: Watch(worker.BeatAsync()); break;
                    case "meet" when worker.Owns && !worker.Meeting && worker.NeedsMeet: Watch(worker.MeetAsync()); break;
                    case "release" when worker.Owns: Watch(worker.ReleaseAsync()); break;
                    default: return null;
                }
                return actor.Calls.Count > calls ? $"call {actor.Calls.Count - 1}: {actor.Calls[^1].Text}" : "nothing to send";
            }
            case "balance":
            {
                if (Paused[act.A]) return null;
                var calls = Actors.Sum(a => a.Calls.Count);
                Watch(Balancers[act.A].RoundAsync());
                return $"{Actors.Sum(a => a.Calls.Count) - calls} takes sent";
            }
            case "turn":
            {
                if (Live(act.A) is not { } actor || !actor.Worker.Ready.Contains(act.S)) return null;
                actor.Worker.RunTurn(act.S);
                return "";
            }
            case "exec" or "lose" or "answer":
            {
                if (act.A >= Actors.Count || act.B >= Actors[act.A].Calls.Count) return null;
                var (actor, call) = (Actors[act.A], Actors[act.A].Calls[act.B]);
                if (act.Kind == "answer")
                {
                    if (!call.Executed || call.Answered || Live(act.A) is null) return null;
                    call.Answer(act.Flag);
                    return call.Text;
                }
                if (call.Executed || (act.Flag && call.Kind != "commit")) return null;
                if (act.Kind == "exec") return call.Execute(Wire.Describe);     // also for a dead actor: a request already on the wire
                call.Lose(act.Flag);
                return call.Text;
            }
            case "add":
            {
                var message = act.M!;
                if (Oracle.Sent.Contains(message.Id) && message.KeepUntil <= Time) return null;    // a client retries only within the window
                if (!Oracle.Accepts(message)) return null;
                var added = Store.AddAsync(message).Result;
                Oracle.Note(message);
                return added ? "added" : "duplicate key";
            }
            case "post":
            {
                if (Live(act.A) is not { } actor || Oracle.Sequences.ContainsKey(act.M!.Target.Id) || !actor.Worker.Post(act.M, fresh: true)) return null;
                Oracle.Note(act.M, posted: true);
                return "";
            }
            case "publish":
                Events.PublishAsync(Store, new EventRow(act.S, "e" + act.B, "e" + act.B, Time + 3_600_000), Config.Partitions).AsTask().Wait();
                Oracle.List(Oracle.Events, act.S).Add("e" + act.B);
                foreach (var start in Oracle.Starts.Where(s => s.Key == act.S)) Count(CatchingUp.Contains(start) ? "start event between the registrar's insert and its read" : "start event after the registrar's read");
                return "";
            case "register":
            {
                // The registrar inserts, and reads later (or never, if it is the flaw): a publish may come in between.
                var start = new SubscriptionRow(act.M!.Kind, "", act.S, OnEvent.Spec(act.S));
                Store.SubscribeAsync(start).AsTask().Wait();
                if (Oracle.Events.GetValueOrDefault(start.Key)?.Count > 0) Count("start event before the registrar's insert");
                if (!Oracle.Starts.Contains(start)) Oracle.Starts.Add(start);
                if (!Config.Flaws.HasFlag(Flaw.StartWithoutRead)) CatchingUp.Add(start);
                return "";
            }
            case "catch-up":
            {
                if (CatchingUp.Find(s => s.Wait == act.S) is not { } start) return null;
                Events.CatchUpAsync(Store, start, Config.Partitions).AsTask().Wait();
                CatchingUp.Remove(start);
                return "";
            }
            case "finish":
            {
                if (Live(act.A) is not { } actor || !actor.Jobs.Remove(act.S, out var work)) return null;
                return work.TrySetResult("done") ? "" : "it was cancelled already";
            }
            case "tick":
                Time += act.A;
                return "";
            case "skew":
                Skew[act.A] += act.B;
                return "";
            case "crash":
                Crash(act.A, act.Flag);
                return "";
            case "pause" or "resume":
                if (Paused[act.A] == (act.Kind == "pause")) return null;
                Paused[act.A] = act.Kind == "pause";
                return "";
            case "project":
            {
                if (Live(act.A) is not { } actor) return null;
                var calls = actor.Calls.Count;
                Watch(actor.Projector.RoundAsync());
                return actor.Calls.Count > calls ? $"call {calls}: {actor.Calls[calls].Text}" : "nothing to send";
            }
            case "sweep":
                return $"{Store.SweepAsync().Result} rows deleted";
            case "request":
            {
                if (Live(act.A) is not { } actor || !actor.Worker.Owns) return null;
                var request = act.M!.ReplyTo![(act.M.ReplyTo.IndexOf('|') + 1)..];
                var asked = act.M with { ReplyTo = ReplyTo.Of(actor.Worker.Node, request) };
                if (!actor.Worker.Post(asked, fresh: true, ambient: "amb:" + asked.Target.Id)) return null;
                Asked.Add((actor.Worker.Node, Processes[actor.Worker.Node].Open(request, Time + 30_000), asked.Target.Id));
                Oracle.Https[asked.Target.Id] = (act.S, asked.Start!.Ephemeral, actor.Id, false);
                return "";
            }
            case "resume-request":
            {
                if (Paused[act.A] || !Oracle.Https.ContainsKey(act.M!.Target.Id)) return null;
                var process = Of(act.A, 0).Worker.Node;
                var request = act.M.ReplyTo!;
                var asked = act.M with { ReplyTo = ReplyTo.Of(process, request) };
                var added = Store.AddAsync(asked).Result;
                Asked.Add((process, Processes[process].Open(request, Time + 30_000), asked.Target.Id));
                return added ? "added" : "duplicate key";
            }
            case "disconnect":
            {
                if (Asked.Find(a => a.Request.Id == act.S) is not { Request: not null } asked || asked.Request.Done || !Running(asked.Process)) return null;
                Processes[asked.Process].Disconnect(act.S);
                if (!act.Flag) return "";
                Oracle.Https[asked.Instance] = Oracle.Https[asked.Instance] with { Cancel = true };
                return Store.AddAsync(new Message("cancel-" + act.S, new InstanceRef(asked.Instance), "cancel", KeepUntil: Time + 3_600_000)).Result ? "cancel added" : "duplicate key";
            }
            case "deliver" or "drop":
            {
                if (act.B >= Deliveries.Count) return null;
                var (node, reply) = Deliveries[act.B];
                if (act.Kind == "drop" || !act.Flag) Deliveries.RemoveAt(act.B);
                if (act.Kind == "deliver" && Processes.TryGetValue(node, out var requests) && Running(node)) requests.Receive(reply);
                return $"{reply.Key} for {node}|{reply.Request}";
            }
            default:
                throw new InvalidOperationException(act.Kind);
        }
    }

}

/// <summary>Chooses the next thing to happen from a seed.</summary>
public sealed class Generator(int seed, World world)
{
    private static readonly string[] Bad = ["throw", "spoil", "die", "dierun"];
    private readonly Random _random = new(seed);
    private readonly List<Message> _outside = [];
    private readonly List<(string Target, string Key)> _jobs = [];
    private int _serial;

    /// <summary>Scales lost calls, wrong answers, crashes and pauses. At 1 the random stream is the default one.</summary>
    public static double Faults = 1;

    public Act Next()
    {
        var r = _random;
        var alive = world.Alive.ToList();
        var actor = alive[r.Next(alive.Count)];
        if (Faults > 1 && r.NextDouble() < (Faults - 1) * 2.0 / 108)       // more owners lost
            return r.Next(2) == 0 ? new Act("crash", r.Next(world.Config.Nodes), Flag: r.Next(3) == 0) : new Act(r.Next(2) == 0 ? "pause" : "resume", r.Next(world.Config.Nodes));
        var roll = r.Next(108);
        switch (roll)
        {
            case < 3: return new Act("take", actor.Id);
            case < 6: return new Act("balance", r.Next(world.Config.Nodes));
            case < 16: return new Act("poll", actor.Id);
            case < 22: return new Act("load", actor.Id);
            case < 36:
            {
                var ready = alive.SelectMany(a => a.Worker.Ready.Select(id => (a.Id, id))).OrderBy(x => x.Id).ThenBy(x => x.id, StringComparer.Ordinal).ToList();
                if (ready.Count == 0) return new Act("poll", actor.Id);
                var pick = ready[r.Next(ready.Count)];
                return new Act("turn", pick.Id, S: pick.id);
            }
            case < 46: return new Act("flush", actor.Id);
            case < 47: return new Act("release", actor.Id);
            case < 48: return new Act("beat", actor.Id);
            case < 52: return new Act("meet", actor.Id);
            case < 82:
            {
                var open = world.Actors.SelectMany(a => a.Calls.Select((c, i) => (a, c, i))).Where(x => !x.c.Answered && !(x.c.Executed && x.a.Dead)).ToList();
                if (open.Count == 0) return new Act("tick", r.Next(50));
                var (a, call, index) = open[r.Next(open.Count)];
                var fault = r.Next(100);
                if (!call.Executed) return fault < 4 * Faults ? new Act("lose", a.Id, index, Flag: call.Kind == "commit" && fault < 2 * Faults) : new Act("exec", a.Id, index);
                return new Act("answer", a.Id, index, Flag: fault < 5 * Faults);
            }
            case < 90: return Outside(r, alive);
            case < 93: return new Act("tick", r.Next((int)(3000 * Faults)));      // long silences expire leases: the main cause of lost owners here
            case < 94 when world.Config.Upgrade: return new Act("crash", r.Next(world.Config.Nodes));   // a rolling deploy restarts nodes more often than crashes do
            case < 94: return new Act("skew", r.Next(world.Config.Nodes), r.Next(-5000, 5000));
            case < 96 when Faults < 1 && r.NextDouble() >= Faults: return new Act("tick", r.Next(50));     // fewer owners lost
            case < 95: return new Act("crash", r.Next(world.Config.Nodes), Flag: r.Next(3) == 0);
            case < 96: return new Act(r.Next(2) == 0 ? "pause" : "resume", r.Next(world.Config.Nodes));
            case < 97:
            {
                var running = alive.SelectMany(a => a.Jobs.Keys.Select(key => (a.Id, key))).OrderBy(x => x.Id).ThenBy(x => x.key, StringComparer.Ordinal).ToList();
                if (running.Count == 0) return new Act("tick", r.Next(50));
                var pick = running[r.Next(running.Count)];
                return new Act("finish", pick.Id, S: pick.key);
            }
            case < 99: return new Act("project", actor.Id);
            case < 100: return new Act("sweep");
            case < 103: return Asking(r, alive);
            case < 105: return Control(r);
            default:
            {
                if (world.Deliveries.Count == 0) return new Act("tick", r.Next(50));
                var (index, fault) = (r.Next(world.Deliveries.Count), r.Next(100));
                return fault < 25 ? new Act("drop", B: index) : new Act("deliver", B: index, Flag: fault < 40);
            }
        }
    }

    private Act Outside(Random r, List<Actor> alive)
    {
        if (world.Config.Native && r.Next(100) < 40) return world.NativeAct(r, alive, _serial++);     // native instances (NativeSim.cs)
        var (n, p) = (_serial++, r.Next(world.Config.Partitions));
        long keep = world.Time + (r.Next(3) == 0 ? 5_000 : 3_600_000);
        Message To(string name, string kind, object? payload, StartSpec? start) =>
            new($"x{n}", InstanceRef.In(p, name), kind, payload is null ? null : Json.Write(payload), null, start, null, keep);
        switch (r.Next(100))
        {
            case < 4:
            {
                List<string> keys = r.Next(2) == 0 ? ["q" + r.Next(3)] : ["q0", "q" + r.Next(1, 3)];
                return new Act("add", M: To("cw" + n, Kinds.Start, null, new StartSpec("cwaiter", "w", Json.Write(keys))));
            }
            case < 7:
                return new Act("add", M: Correlation.Published("x" + n, "q" + r.Next(3), world.Config.Partitions, new Publish("e" + n, null, 3_600_000), keep));
            case < 34:
            {
                var forward = r.Next(4) == 0 ? InstanceRef.In(r.Next(world.Config.Partitions), "c" + r.Next(2)).Id : null;
                return new Act("add", M: To("c" + r.Next(2), "add", new Add("t" + n, forward, Hold: r.Next(5) == 0), Counter.Spec));
            }
            case < 44 when _outside.Count > 0:
                return new Act("add", M: _outside[r.Next(_outside.Count)], S: "a retry");
            case < 48: return new Act("add", M: To("ghost" + n, "add", new Add("t" + n), null));
            case < 56: return new Act("add", M: To("par" + n, Kinds.Start, null, new StartSpec("parent", "p", Json.Write(new ParentInput(r.Next(1, 4), r.Next(4) == 0 ? -1 : r.Next(4000), r.Next(2) == 0)), History: (History)r.Next(3))));
            case < 64:
            {
                List<string> keys = r.Next(2) == 0 ? ["k" + r.Next(3)] : ["k0", "k" + r.Next(1, 3)];
                return new Act("add", M: To("w" + n, Kinds.Start, null, new StartSpec("waiter", "w", Json.Write(keys))));
            }
            case < 71: return new Act("publish", B: n, S: r.Next(4) == 0 ? "s" + r.Next(2) : "k" + r.Next(3));
            case < 78: return new Act("add", M: To("s" + n, Kinds.Start, null, Sequence.Spec(r.Next(1, 6), Mode(r)) with { History = (History)r.Next(3) }));
            case < 84:
            {
                var owner = alive[r.Next(alive.Count)];
                return new Act("post", owner.Id, M: new Message($"x{n}", InstanceRef.In(owner.Partition, "v" + n), Kinds.Start, Start: Sequence.Spec(r.Next(1, 6), Mode(r))));
            }
            case < 92:
                return new Act("add", M: To("r" + r.Next(2), "go", "k" + n, Runner.Spec));
            case < 95 when _jobs.Count > 0:
            {
                var (target, key) = _jobs[r.Next(_jobs.Count)];
                return new Act("add", M: new Message($"x{n}", new InstanceRef(target), "cancel", Json.Write(key), KeepUntil: keep));
            }
            default:
                return new Act("add", M: To("f" + r.Next(3), r.Next(3) == 0 ? Bad[r.Next(4)] : "ok", null, Fragile.Spec));
        }
    }

    // HTTP: a request that starts an instance on the receiving node; a waiting instance; a request that resumes one,
    // perhaps from another node; a caller that goes away.
    private Act Asking(Random r, List<Actor> alive)
    {
        var n = _serial++;
        var roll = r.Next(100);
        if (roll < 45)
        {
            var owners = alive.Where(a => a.Worker.Owns).ToList();
            if (owners.Count == 0) return new Act("tick", r.Next(50));
            var owner = owners[r.Next(owners.Count)];
            var mode = Http.Starts[r.Next(Http.Starts.Length)];
            var spec = Http.Spec(mode, ephemeral: mode == Http.Respond && r.Next(2) == 0);
            return new Act("request", owner.Id, S: mode, M: new Message($"x{n}", InstanceRef.In(owner.Partition, "h" + n), Kinds.Start, Start: spec, ReplyTo: "|q" + n));
        }
        if (roll < 60)
            return new Act("add", M: new Message($"x{n}", InstanceRef.In(r.Next(world.Config.Partitions), "hw" + n), Kinds.Start, Start: Http.Spec(Http.Wait), KeepUntil: world.Time + 3_600_000));
        if (roll < 85)
        {
            var waiting = world.Oracle.Https.Where(h => h.Value.Mode == Http.Wait).Select(h => h.Key).Order(StringComparer.Ordinal).ToList();
            if (waiting.Count == 0) return new Act("tick", r.Next(50));
            return new Act("resume-request", r.Next(world.Config.Nodes), M: new Message($"x{n}", new InstanceRef(waiting[r.Next(waiting.Count)]), "resume", KeepUntil: world.Time + 3_600_000, ReplyTo: "q" + n));
        }
        var open = world.Asked.Where(a => !a.Request.Done).Select(a => a.Request.Id).ToList();
        return open.Count == 0 ? new Act("tick", r.Next(50)) : new Act("disconnect", S: open[r.Next(open.Count)], Flag: r.Next(2) == 0);
    }

    // The kernel's own commands, from outside: start a tree, terminate a tree node or a runner, suspend or resume anything.
    private Act Control(Random r)
    {
        var roll = r.Next(13);
        if (roll == 12)     // an ended instance is deleted
        {
            var ended = world.Store.Instances.Values.Where(i => i.Status != InstanceStatus.Running && i.Model is "sequence" or "waiter" or "onevent").Select(i => i.Id).Order(StringComparer.Ordinal).ToList();
            if (ended.Count == 0) return new Act("tick", r.Next(50));
            return new Act("add", M: new Message($"x{_serial++}", new InstanceRef(ended[r.Next(ended.Count)]), Kinds.Delete, KeepUntil: world.Time + 3_600_000));
        }
        if (roll == 10)     // a parent is cancelled: it cancels its children, wherever they are
        {
            var parents = world.Oracle.Parents.Keys.Where(id => world.Store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Running).Order(StringComparer.Ordinal).ToList();
            if (parents.Count == 0) return new Act("tick", r.Next(50));
            return new Act("add", M: new Message($"x{_serial++}", new InstanceRef(parents[r.Next(parents.Count)]), "cancel", KeepUntil: world.Time + 3_600_000));
        }
        if (roll < 3) return new Act("add", M: new Message($"x{_serial}", InstanceRef.In(r.Next(world.Config.Partitions), "tr" + _serial++), Kinds.Start, Start: Tree.Spec(r.Next(1, 3), r.Next(1, 3)), KeepUntil: world.Time + 3_600_000));
        if (roll == 6)      // a counter moves to another version of its definition
        {
            var counters = world.Store.Instances.Values.Where(i => i.Model == "counter" && i.Status == InstanceStatus.Running).Select(i => i.Id).Order(StringComparer.Ordinal).ToList();
            if (counters.Count == 0) return new Act("tick", r.Next(50));
            return new Act("add", M: new Message($"x{_serial++}", new InstanceRef(counters[r.Next(counters.Count)]), "rebind", Json.Write("c" + r.Next(2, 9)), KeepUntil: world.Time + 3_600_000));
        }
        if (roll < 6)
        {
            var ends = world.Store.Instances.Values.Where(i => i.Status == InstanceStatus.Running && i.Model is "tree" or "runner").Select(i => i.Id).Order(StringComparer.Ordinal).ToList();
            if (ends.Count == 0) return new Act("tick", r.Next(50));
            return new Act("add", M: new Message($"x{_serial++}", new InstanceRef(ends[r.Next(ends.Count)]), Kinds.Terminate, KeepUntil: world.Time + 3_600_000));
        }
        if (roll == 7)      // a definition that starts an instance on every event of a key
        {
            var which = r.Next(3);
            return new Act("register", S: "d" + which, M: new Message("", default, "s" + which % 2));
        }
        if (roll == 8 && world.CatchingUp.Count > 0) return new Act("catch-up", S: world.CatchingUp[r.Next(world.CatchingUp.Count)].Wait);
        var rows = world.Store.Instances.Values.Where(i => i.Status == InstanceStatus.Running).Select(i => i.Id).Order(StringComparer.Ordinal).ToList();
        if (rows.Count == 0) return new Act("tick", r.Next(50));
        var target = rows[r.Next(rows.Count)];
        var kind = world.Store.Instances[target].Suspended == (r.Next(4) == 0) ? Kinds.Suspend : Kinds.Resume;
        return new Act("add", M: new Message($"x{_serial++}", new InstanceRef(target), kind, KeepUntil: world.Time + 3_600_000));
    }

    private static string Mode(Random r) => r.Next(3) switch { 0 => Sequence.Ephemeral, 1 => Sequence.Balanced, _ => Sequence.Durable };

    /// <summary>Remember what was really sent, so that later choices (a retry, a cancel) can refer to it.</summary>
    public void Applied(Act act)
    {
        if (act is not { Kind: "add", S: "", M: { } message }) return;
        _outside.Add(message);
        if (message.Kind == "go") _jobs.Add((message.Target.Id, message.Read<string>()));
    }
}
