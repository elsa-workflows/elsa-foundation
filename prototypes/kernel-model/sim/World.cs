namespace Kernel.Sim;

public sealed record Config(int Nodes, int Partitions, Flaw Flaws, int Attempts = 3, int Group = 1);

/// <summary>One thing that happens. Concrete, so that a list of them replays without the random generator.</summary>
public sealed record Act(string Kind, int A = 0, int B = 0, string S = "", Message? M = null, bool Flag = false)
{
    public override string ToString() => Kind switch
    {
        "add" => $"outside adds {M!.Id} ({M.Kind} to {M.Target}{(S == "" ? "" : ", " + S)})",
        "post" => $"actor {A} is handed {M!.Id} in memory (start of {M.Target})",
        "publish" => $"outside publishes event e{B} for key {S}",
        "step" => $"actor {A} runs a step of {S}",
        "exec" => $"the database executes call {B} of actor {A}",
        "lose" => $"call {B} of actor {A} never reaches the database ({(Flag ? "deadlock victim" : "connection lost")})",
        "answer" => $"actor {A} gets the answer to call {B}{(Flag ? " as 'connection lost'" : "")}",
        "finish" => $"the work of run {S} completes on actor {A}",
        "tick" => $"{A} ms pass",
        "skew" => $"the wall clock of node {A} jumps {B} ms",
        "crash" => $"node {A} crashes and restarts{(Flag ? "; the old process lives on" : "")}",
        "release" => $"actor {A} releases its partition",
        "pause" => $"node {A} pauses",
        "resume" => $"node {A} resumes",
        "project" => $"the projector reads partition {A}",
        "sweep" => "the sweep runs",
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
    public bool Dead, Zombie;
    public readonly List<Call> Calls = [];
    public readonly Dictionary<string, TaskCompletionSource<object?>> Runs = new(StringComparer.Ordinal);
}

/// <summary>What a worker sees as its store: the shared in-memory store behind a wire the simulation controls.</summary>
public sealed class Wire(World world, Actor actor) : IKernelStore
{
    private ValueTask<T> Send<T>(string kind, string text, Func<ValueTask<T>> op)
    {
        if (world.Auto)
        {
            try { return op(); }
            catch (Exception e) { return ValueTask.FromException<T>(e); }
        }
        var answer = new TaskCompletionSource<T>();     // continuations run inline when the simulation delivers the answer
        actor.Calls.Add(new Call(kind, text, () => op().Result, r => answer.SetResult((T)r!), answer.SetException));
        return new ValueTask<T>(answer.Task);
    }

    public ValueTask<Lease?> TakeAsync(int partition, string node, TimeSpan lease, CancellationToken cancellationToken = default) =>
        Send("take", $"take p{partition}", () => world.Take(partition, node, lease));

    public ValueTask ReleaseAsync(int partition, long epoch, CancellationToken cancellationToken = default) => world.Store.ReleaseAsync(partition, epoch);

    public ValueTask<Committed> CommitAsync(CommitBatch batch, CancellationToken cancellationToken = default) =>
        Send("commit", $"commit p{batch.Partition} e{batch.Epoch} @{batch.Position} [{string.Join(", ", batch.Steps.Select(Describe))}]"
            + (batch.Seen.Count > 0 ? $" seen [{string.Join(", ", batch.Seen)}]" : "") + (batch.Trial.Count > 0 ? $" on trial [{string.Join(", ", batch.Trial)}]" : ""),
            () => world.Store.CommitAsync(batch));

    public ValueTask<Polled> PollAsync(int partition, long epoch, TimeSpan horizon, int limit, CancellationToken cancellationToken = default) =>
        Send("poll", $"poll p{partition} e{epoch}", () => world.Store.PollAsync(partition, epoch, horizon, limit));

    public ValueTask<IReadOnlyList<InstanceRow>> LoadAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) =>
        Send("load", $"load [{string.Join(", ", ids)}]", () => world.Store.LoadAsync(ids));

    public ValueTask<IReadOnlyList<EventRow>> FindEventsAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) =>
        Send("events", $"events for [{string.Join(", ", keys)}]", () => world.Store.FindEventsAsync(keys));

    public ValueTask<bool> AddAsync(Message message, CancellationToken cancellationToken = default) => world.Store.AddAsync(message);
    public ValueTask PublishAsync(EventRow @event, CancellationToken cancellationToken = default) => world.Store.PublishAsync(@event);
    public ValueTask<IReadOnlyList<SubscriptionRow>> FindSubscriptionsAsync(string key, CancellationToken cancellationToken = default) => world.Store.FindSubscriptionsAsync(key);
    public ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take, CancellationToken cancellationToken = default) => world.Store.ReadJournalAsync(partition, after, take);
    public ValueTask<int> SweepAsync(CancellationToken cancellationToken = default) => world.Store.SweepAsync();

    private static string Describe(StepCommit step) =>
        (step.Row is { } row ? $"{row.Id} r{row.Revision} {row.Status}" : step.Instance == "" ? "tickets" : $"{step.Instance} (no row)")
        + string.Concat(step.Consumed.Concat(step.Done).Select(id => " -" + id)) + (step.Sent.Count > 0 ? $" +{string.Join(" +", step.Sent.Select(m => m.Id))}" : "");

    public static string Describe(object? result) => result switch
    {
        null => "refused",
        Lease lease => $"epoch {lease.Epoch}, position {lease.Position}",
        Committed committed => committed.Landed ? "landed" : "the fence matched nothing",
        Polled polled => $"[{string.Join(", ", polled.Messages.Select(m => m.Message.Id))}]",
        IReadOnlyList<InstanceRow> rows => $"[{string.Join(", ", rows.Select(r => $"{r.Id} r{r.Revision}"))}]",
        IReadOnlyList<EventRow> events => $"[{string.Join(", ", events.Select(e => e.Id))}]",
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
    public readonly long[] Skew, Projected;
    public readonly int[] Incarnation;
    public readonly bool[] Paused;
    public readonly List<string> Faults = [];
    private readonly List<Task> _watched = [];
    public long Time = 1_000_000;
    public bool Auto;
    public int? Dying;
    public Action<string>? Log;

    public World(Config config)
    {
        Config = config;
        Store = new InMemoryStore(config.Partitions, () => Time, config.Flaws) { Refuses = row => row.State.Contains(Fragile.Sour) };
        Current = new int[config.Nodes, config.Partitions];
        Skew = new long[config.Nodes];
        Paused = new bool[config.Nodes];
        Projected = new long[config.Partitions];
        Incarnation = new int[config.Nodes];
        for (var node = 0; node < config.Nodes; node++)
            for (var partition = 0; partition < config.Partitions; partition++)
                Spawn(node, partition);
    }

    public Actor Of(int node, int partition) => Actors[Current[node, partition]];
    public IEnumerable<Actor> Alive => Actors.Where(a => !a.Dead);
    private Actor? Live(int id) => id >= 0 && id < Actors.Count && Actors[id] is { Dead: false } actor && !Paused[actor.Node] ? actor : null;

    private void Spawn(int node, int partition)
    {
        var actor = new Actor(Actors.Count, node, partition);
        IExecutionModel[] models =
        [
            new Counter(), new Parent(), new Child(), new Waiter(), new Sequence(), new Correlation(Config.Flaws.HasFlag(Flaw.CorrelationForgets)), new CWaiter(Config.Partitions),
            new Runner((run, cancel) => Work(actor, run, cancel)), new Fragile(() => Dying = node),
        ];
        // A node's name is unique per process start: a restarted process is not its predecessor (finding F9).
        actor.Worker = new Worker(Config.Flaws.HasFlag(Flaw.StableNodeName) ? "node" + node : $"node{node}.{Incarnation[node]}", partition, new Wire(this, actor), models, new WorkerOptions
        {
            Lease = TimeSpan.FromSeconds(10), Horizon = TimeSpan.FromSeconds(2), PollLimit = 3, MaxBatch = 4, MaxAttempts = Config.Attempts, TrialGroup = Config.Group,
            Monotonic = () => Time + node * 7919,       // every node counts at the database's rate from its own zero
            WallClock = () => Time + Skew[node],
            Flaws = Config.Flaws,
            OnStep = (instance, inbox, _) =>
            {
                Log?.Invoke($"        actor {actor.Id} steps {instance} with [{string.Join(", ", inbox.Select(m => m.Kind + " " + m.Id))}]");
                Alone(actor, instance.Id, "a step of " + instance);
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
        foreach (var row in Store.Messages.Values)
        {
            // I14, the counting half: one more attempt for exactly the rows that were on trial under the owner that was lost.
            var onTrial = lost && !row.Done && row.Trial == epoch && row.Message.Target.Partition == partition;
            var expected = _attempts.GetValueOrDefault(row.Message.Id) + (onTrial ? 1 : 0);
            if (row.Attempts != expected) Faults.Add($"I14 the take of partition {partition} by {node} left {row.Message.Id} at {row.Attempts} attempts; it should be {expected}");
            _attempts[row.Message.Id] = row.Attempts;
        }
        return taken;
    }

    // Invariant 14, the "alone" half: while rows are on trial at the current epoch, the owner starts nothing but them.
    private void Alone(Actor actor, string instance, string what)
    {
        var partition = Store.Partitions[actor.Partition];
        if (actor.Worker.Epoch != partition.Epoch) return;
        var trial = Store.Messages.Values.Where(m => !m.Done && m.Trial == partition.Epoch && m.Message.Target.Partition == actor.Partition).ToList();
        if (trial.Count > 0 && !trial.Exists(m => m.Message.Target.Id == instance)) Faults.Add($"I14 {what} started on actor {actor.Id} while {trial[0].Message.Id} was on trial");
    }

    private ValueTask<object?> Work(Actor actor, IRun run, CancellationToken cancel)
    {
        Alone(actor, run.Instance.Id, $"run {run.Key} of {run.Instance}");
        var done = new TaskCompletionSource<object?>();
        actor.Runs[run.Instance.Id + "!" + run.Key] = done;
        cancel.Register(() => done.TrySetCanceled());
        return new ValueTask<object?>(done.Task);
    }

    private void Crash(int node, bool zombie)
    {
        Paused[node] = false;
        Incarnation[node]++;
        foreach (var actor in Actors.Where(a => a.Node == node && !a.Dead).ToList())
        {
            // The old process is gone, or (zombie) lives on with its memory beside the new one under the same name.
            if (actor.Zombie || !zombie) actor.Dead = true;
            else actor.Zombie = true;
            if (Current[node, actor.Partition] == actor.Id) Spawn(node, actor.Partition);
        }
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
        if (outcome is null || Dying is not { } node) return outcome;
        Dying = null;
        Crash(node, zombie: false);
        return outcome + $"; the step killed node {node}";
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
            case "step":
            {
                if (Live(act.A) is not { } actor || !actor.Worker.Ready.Contains(act.S)) return null;
                actor.Worker.Run(act.S);
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
                Events.PublishAsync(Store, new EventRow(act.S, "e" + act.B, "e" + act.B, Time + 3_600_000)).AsTask().Wait();
                return "";
            case "finish":
            {
                if (Live(act.A) is not { } actor || !actor.Runs.Remove(act.S, out var work)) return null;
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
                return Project(act.A, 5);
            case "sweep":
                return $"{Store.SweepAsync().Result} rows deleted";
            default:
                throw new InvalidOperationException(act.Kind);
        }
    }

    /// <summary>The projector: journal rows after its cursor, in position order. It must never find a hole behind it.</summary>
    private string Project(int partition, int take)
    {
        var rows = Store.ReadJournalAsync(partition, Projected[partition], take).Result;
        foreach (var row in rows)
        {
            if (row.Position != Projected[partition] + 1) Faults.Add($"I6 the projector of partition {partition} was at {Projected[partition]} and read {row.Position} next");
            Projected[partition] = row.Position;
        }
        return $"now at {Projected[partition]}";
    }
}

/// <summary>Chooses the next thing to happen from a seed.</summary>
public sealed class Generator(int seed, World world)
{
    private static readonly string[] Bad = ["throw", "spoil", "die", "dierun"];
    private readonly Random _random = new(seed);
    private readonly List<Message> _outside = [];
    private readonly List<(string Target, string Key)> _runs = [];
    private int _serial;

    public Act Next()
    {
        var r = _random;
        var alive = world.Alive.ToList();
        var actor = alive[r.Next(alive.Count)];
        var roll = r.Next(100);
        switch (roll)
        {
            case < 6: return new Act("take", actor.Id);
            case < 16: return new Act("poll", actor.Id);
            case < 22: return new Act("load", actor.Id);
            case < 36:
            {
                var ready = alive.SelectMany(a => a.Worker.Ready.Select(id => (a.Id, id))).OrderBy(x => x.Id).ThenBy(x => x.id, StringComparer.Ordinal).ToList();
                if (ready.Count == 0) return new Act("poll", actor.Id);
                var pick = ready[r.Next(ready.Count)];
                return new Act("step", pick.Id, S: pick.id);
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
                if (!call.Executed) return fault < 4 ? new Act("lose", a.Id, index, Flag: call.Kind == "commit" && fault < 2) : new Act("exec", a.Id, index);
                return new Act("answer", a.Id, index, Flag: fault < 5);
            }
            case < 90: return Outside(r, alive);
            case < 93: return new Act("tick", r.Next(3000));
            case < 94: return new Act("skew", r.Next(world.Config.Nodes), r.Next(-5000, 5000));
            case < 95: return new Act("crash", r.Next(world.Config.Nodes), Flag: r.Next(3) == 0);
            case < 96: return new Act(r.Next(2) == 0 ? "pause" : "resume", r.Next(world.Config.Nodes));
            case < 97:
            {
                var running = alive.SelectMany(a => a.Runs.Keys.Select(key => (a.Id, key))).OrderBy(x => x.Id).ThenBy(x => x.key, StringComparer.Ordinal).ToList();
                if (running.Count == 0) return new Act("tick", r.Next(50));
                var pick = running[r.Next(running.Count)];
                return new Act("finish", pick.Id, S: pick.key);
            }
            case < 99: return new Act("project", r.Next(world.Config.Partitions));
            default: return new Act("sweep");
        }
    }

    private Act Outside(Random r, List<Actor> alive)
    {
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
                return new Act("add", M: To("c" + r.Next(2), "add", new Add("t" + n, forward), Counter.Spec));
            }
            case < 44 when _outside.Count > 0:
                return new Act("add", M: _outside[r.Next(_outside.Count)], S: "a retry");
            case < 48: return new Act("add", M: To("ghost" + n, "add", new Add("t" + n), null));
            case < 56: return new Act("add", M: To("par" + n, Kinds.Start, null, new StartSpec("parent", "p", Json.Write(new ParentInput(r.Next(1, 4), r.Next(4000))))));
            case < 64:
            {
                List<string> keys = r.Next(2) == 0 ? ["k" + r.Next(3)] : ["k0", "k" + r.Next(1, 3)];
                return new Act("add", M: To("w" + n, Kinds.Start, null, new StartSpec("waiter", "w", Json.Write(keys))));
            }
            case < 71: return new Act("publish", B: n, S: "k" + r.Next(3));
            case < 78: return new Act("add", M: To("s" + n, Kinds.Start, null, Sequence.Spec(r.Next(1, 6), Mode(r))));
            case < 84:
            {
                var owner = alive[r.Next(alive.Count)];
                return new Act("post", owner.Id, M: new Message($"x{n}", InstanceRef.In(owner.Partition, "v" + n), Kinds.Start, Start: Sequence.Spec(r.Next(1, 6), Mode(r))));
            }
            case < 92:
                return new Act("add", M: To("r" + r.Next(2), "go", "k" + n, Runner.Spec));
            case < 95 when _runs.Count > 0:
            {
                var (target, key) = _runs[r.Next(_runs.Count)];
                return new Act("add", M: new Message($"x{n}", new InstanceRef(target), "cancel", Json.Write(key), KeepUntil: keep));
            }
            default:
                return new Act("add", M: To("f" + r.Next(3), r.Next(3) == 0 ? Bad[r.Next(4)] : "ok", null, Fragile.Spec));
        }
    }

    private static string Mode(Random r) => r.Next(3) switch { 0 => Sequence.Ephemeral, 1 => Sequence.Balanced, _ => Sequence.Durable };

    /// <summary>Remember what was really sent, so that later choices (a retry, a cancel) can refer to it.</summary>
    public void Applied(Act act)
    {
        if (act is not { Kind: "add", S: "", M: { } message }) return;
        _outside.Add(message);
        if (message.Kind == "go") _runs.Add((message.Target.Id, message.Read<string>()));
    }
}
