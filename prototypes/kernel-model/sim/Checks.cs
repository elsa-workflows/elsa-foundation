namespace Kernel.Sim;

/// <summary>What was sent from outside, and therefore what must come of it.</summary>
public sealed class Oracle
{
    public readonly HashSet<string> Sent = new(StringComparer.Ordinal);                 // ids of outside messages
    public readonly Dictionary<string, List<string>> Counters = new();                 // target -> tokens from outside, in the order added
    public readonly HashSet<string> Ghosts = [];                                         // targets that were never given a start spec
    public readonly Dictionary<string, int> Parents = new();
    public readonly Dictionary<string, List<string>> Waiters = new();
    public readonly Dictionary<string, (int Count, string Mode, bool Posted)> Sequences = new();
    public readonly Dictionary<string, List<string>> Runners = new();                  // target -> keys told to go
    public readonly Dictionary<string, bool> Fragiles = new();                          // target -> a bad message was sent

    public bool Accepts(Message m) => m.Kind != "cancel" || (Runners.TryGetValue(m.Target.Id, out var keys) && keys.Contains(m.Read<string>()));

    public void Note(Message m, bool posted = false)
    {
        if (!Sent.Add(m.Id)) return;
        var target = m.Target.Id;
        switch (m.Start?.Model)
        {
            case "counter": List(Counters, target).Add(m.Read<Add>().Token); break;
            case "parent": Parents[target] = Json.Read<ParentInput>(m.Start.Input!).Children; break;
            case "waiter": Waiters[target] = Json.Read<List<string>>(m.Start.Input!); break;
            case "sequence": var input = Json.Read<SequenceInput>(m.Start.Input!); Sequences[target] = (input.Count, input.Mode, posted); break;
            case "runner": List(Runners, target).Add(m.Read<string>()); break;
            case "fragile": Fragiles[target] = Fragiles.GetValueOrDefault(target) || m.Kind != "ok"; break;
            case null when m.Kind == "add": Ghosts.Add(target); break;
        }
    }

    public static List<string> List(Dictionary<string, List<string>> map, string key) => map.TryGetValue(key, out var list) ? list : map[key] = [];
}

public sealed partial class World
{
    public readonly Oracle Oracle = new();
    private readonly Dictionary<string, (long Revision, InstanceStatus Status)> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type, string), object> _parsed = new();

    private T? State<T>(string id) where T : class =>
        Store.Instances.GetValueOrDefault(id) is not { State: var json } || json == "null" ? null
        : (T)(_parsed.TryGetValue((typeof(T), json), out var state) ? state : _parsed[(typeof(T), json)] = Json.Read<T>(json));

    private static bool InOrder(List<string> applied, List<string> sent)
    {
        var last = -1;
        foreach (var token in applied)
        {
            var at = sent.IndexOf(token);
            if (at < 0) continue;
            if (at < last) return false;
            last = at;
        }
        return true;
    }

    /// <summary>The invariants of the sketch, against the rows, the owners' memory and the oracle. Null when all hold.</summary>
    public string? Check()
    {
        if (Store.Ghost.Count > 0) return Store.Ghost[0];
        if (Faults.Count > 0) return Faults[0];
        _watched.RemoveAll(t => t.IsCompletedSuccessfully);
        if (_watched.Find(t => t.IsFaulted) is { } bug) return "BUG " + bug.Exception!.InnerException;
        var waiting = Store.Messages.Values.Where(m => !m.Done).ToLookup(m => m.Message.Target.Id, m => m.Message);
        return Journal() ?? Memory() ?? Counters(waiting) ?? Trees(waiting) ?? Waits() ?? Sequences(waiting) ?? Runs();
    }

    // I6: no gap in a partition's journal. I2, I10: an instance's row and its journal rows agree; a row never goes back.
    private string? Journal()
    {
        var count = new long[Config.Partitions];
        var top = new long[Config.Partitions];
        var commits = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        foreach (var row in Store.Journal.Values)
        {
            count[row.Partition]++;
            top[row.Partition] = Math.Max(top[row.Partition], row.Position);
            if (row.Revision > 0) (commits.TryGetValue(row.Instance, out var list) ? list : commits[row.Instance] = []).Add(row.Revision);
        }
        for (var p = 0; p < Config.Partitions; p++)
        {
            var position = Store.Partitions[p].Position;
            if (count[p] != position || top[p] != position) return $"I6 partition {p} is at position {position} and its journal has {count[p]} rows up to {top[p]}";
            if (Projected[p] > position) return $"I6 the projector of partition {p} is at {Projected[p]}, past position {position}";
        }
        foreach (var (id, revisions) in commits)
        {
            revisions.Sort();
            if (Store.Instances.GetValueOrDefault(id) is not { } row) return $"I2 the journal has a commit of {id} and there is no row";
            if (revisions.Count != row.Revision || revisions[^1] != row.Revision) return $"I2 {id} is at revision {row.Revision} and the journal has its commits [{string.Join(", ", revisions)}]";
        }
        foreach (var row in Store.Instances.Values)
        {
            if (!commits.ContainsKey(row.Id)) return $"I2 {row.Id} has a row and no journal row";
            if (_known.TryGetValue(row.Id, out var before) && (row.Revision < before.Revision || (before.Status != InstanceStatus.Running && row.Status != before.Status)))
                return $"I10 {row.Id} went from revision {before.Revision} {before.Status} to revision {row.Revision} {row.Status}";
            _known[row.Id] = (row.Revision, row.Status);
            if (row.Status == InstanceStatus.Faulted && !Oracle.Fragiles.GetValueOrDefault(row.Id))
                return $"I14 {row.Id} did nothing wrong and was parked: {row.Fault}";
        }
        if (_known.Keys.FirstOrDefault(id => !Store.Instances.ContainsKey(id)) is { } gone) return $"I10 the row of {gone} disappeared";
        return Oracle.Ghosts.FirstOrDefault(Store.Instances.ContainsKey) is { } ghost ? $"I10 {ghost} was created by a message without a start spec" : null;
    }

    // I9: at its current epoch, what an owner holds equals the committed rows plus its own uncommitted steps.
    private string? Memory()
    {
        foreach (var actor in Alive)
        {
            var (worker, partition) = (actor.Worker, Store.Partitions[actor.Partition]);
            if (!worker.Owns || worker.Epoch != partition.Epoch) continue;
            if (!worker.Flushing && worker.Position != partition.Position)
                return $"I9 actor {actor.Id} believes partition {actor.Partition} is at {worker.Position}; it is at {partition.Position}";
            foreach (var (id, row, state, busy) in worker.Instances)
            {
                if (busy) continue;
                if (row != Store.Instances.GetValueOrDefault(id))
                    return $"I9 actor {actor.Id} holds {id} at revision {row?.Revision.ToString() ?? "none"}; its row is at {Store.Instances.GetValueOrDefault(id)?.Revision.ToString() ?? "none"}";
                if (state is not null && state != row?.State) return $"I9 actor {actor.Id}: the live state of {id} is {state} and its row says {row?.State}";
            }
            var held = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, durable, taken) in worker.Messages)
            {
                held.Add(id);
                if (durable && !taken && Store.Messages.GetValueOrDefault(id) is not { Done: false }) return $"I9 actor {actor.Id} holds message {id}, which is not a row any more";
            }
            if (worker.Flushing) continue;
            foreach (var row in Store.Messages.Values)
                if (row.Seen == partition.Epoch && !row.Done && row.Message.Target.Partition == actor.Partition && !held.Contains(row.Message.Id))
                    return $"I3 message {row.Message.Id} is marked seen at epoch {partition.Epoch} and its owner, actor {actor.Id}, does not hold it";
        }
        return null;
    }

    // I3, I4, I11, I2 for counters: every token sent is waiting or applied, exactly once, in the order sent.
    private string? Counters(ILookup<string, Message> waiting)
    {
        var sent = Oracle.Counters.ToDictionary(c => c.Key, c => c.Value.ToList());
        var forwards = new List<(string Sender, string Target, List<string> Tokens)>();
        foreach (var row in Store.Instances.Values)
        {
            if (row.Model != "counter" || State<CounterState>(row.Id) is not { } state) continue;
            foreach (var group in state.Forwarded.GroupBy(f => f.Target))
            {
                forwards.Add((row.Id, group.Key, group.Select(f => f.Token).ToList()));
                Oracle.List(sent, group.Key).AddRange(forwards[^1].Tokens);
            }
        }
        foreach (var (target, tokens) in sent)
        {
            var state = State<CounterState>(target);
            var applied = state?.Tokens ?? [];
            if (applied.Count != applied.Distinct().Count() || state?.Ids.Count != state?.Ids.Distinct().Count())
                return $"I4 {target} applied a message twice: [{string.Join(", ", applied)}]";
            var rows = waiting[target].Where(m => m.Kind == "add").Select(m => m.Read<Add>().Token).ToList();
            foreach (var token in tokens)
            {
                var n = rows.Count(t => t == token) + (applied.Contains(token) ? 1 : 0);
                if (n == 0) return $"I3 {token} for {target} is neither waiting nor applied";
                if (n > 1) return $"I4 {token} for {target} is {(applied.Contains(token) ? "applied and waiting again" : "waiting twice")}";
            }
            if (applied.Find(t => !tokens.Contains(t)) is { } stray) return $"I2 {target} applied {stray}, which no committed sender has sent";
            if (Oracle.Counters.TryGetValue(target, out var outside) && !InOrder(applied, outside))
                return $"I11 {target} applied [{string.Join(", ", applied)}]; they were sent from outside as [{string.Join(", ", outside)}]";
        }
        foreach (var (sender, target, tokens) in forwards)
            if (State<CounterState>(target) is { } state && !InOrder(state.Tokens, tokens))
                return $"I11 {target} applied [{string.Join(", ", state.Tokens)}]; {sender} sent it [{string.Join(", ", tokens)}] in that order";
        return null;
    }

    // I2, I3 for a parent and its children: each pending child is at exactly one stage, and a message stands for that stage.
    private string? Trees(ILookup<string, Message> waiting)
    {
        foreach (var (id, children) in Oracle.Parents)
        {
            if (State<ParentState>(id) is not { } state)
            {
                if (!Store.Instances.ContainsKey(id) && !waiting[id].Any()) return $"I3 the start of {id} is lost";
                continue;
            }
            if (state.Done.Find(d => d.StartsWith('!')) is { } unexpected) return $"I4 {id} was told {unexpected}";
            if (state.Pending.Count + state.Done.Count != children || state.Done.Distinct().Count() != state.Done.Count)
                return $"I4 {id} should have {children} children; pending [{string.Join(", ", state.Pending)}], done [{string.Join(", ", state.Done)}]";
            if ((Store.Instances[id].Status == InstanceStatus.Finished) != (state.Pending.Count == 0)) return $"I2 {id} is {Store.Instances[id].Status} with {state.Pending.Count} children pending";
            foreach (var child in state.Pending)
            {
                var row = Store.Instances.GetValueOrDefault(child);
                var start = waiting[child].Count(m => m.Kind == Kinds.Start);
                var timer = waiting[child].Count(m => m.Kind == "timer");
                var finished = waiting[id].Count(m => m.Kind == Kinds.ChildFinished && m.Read<ChildFinished>().Child == child);
                var stage = row is null ? (start, timer, finished) == (1, 0, 0) : row.Status == InstanceStatus.Running ? (start, timer, finished) == (0, 1, 0) : (start, timer, finished) == (0, 0, 1);
                if (!stage) return $"I2 child {child} of {id}: row {row?.Status.ToString() ?? "none"}, {start} start, {timer} timer, {finished} child-finished waiting";
            }
            foreach (var child in state.Done)
                if (Store.Instances.GetValueOrDefault(child) is not { Status: InstanceStatus.Finished } || waiting[child].Any()) return $"I2 {id} counts {child} as done and it is not";
        }
        return null;
    }

    // I8: the subscriptions of an instance are exactly the waits in its committed state. I13: an event is delivered once.
    private string? Waits()
    {
        var rows = Store.Subscriptions.ToLookup(s => s.Instance, s => s.Key);
        if (rows.FirstOrDefault(g => !Oracle.Waiters.ContainsKey(g.Key)) is { } stray) return $"I8 {stray.Key} has a subscription and is no waiter";
        foreach (var id in Oracle.Waiters.Keys)
        {
            var state = State<WaiterState>(id);
            if (!rows[id].Order().SequenceEqual((state?.Waits ?? []).Order()))
                return $"I8 {id} waits for [{string.Join(", ", state?.Waits ?? [])}] and its subscriptions are [{string.Join(", ", rows[id])}]";
            if (state is null) continue;
            if (state.Got.Count > 1) return $"I13 {id} was given [{string.Join(", ", state.Got)}]";
            if ((Store.Instances[id].Status == InstanceStatus.Finished) != (state.Got.Count == 1)) return $"I2 {id} is {Store.Instances[id].Status} having been given {state.Got.Count} events";
        }
        return null;
    }

    private string? Sequences(ILookup<string, Message> waiting)
    {
        foreach (var (id, (count, mode, posted)) in Oracle.Sequences)
        {
            if (Store.Instances.GetValueOrDefault(id) is not { } row)
            {
                if (!posted && mode != Sequence.Ephemeral && !waiting[id].Any()) return $"I3 the start of {id} is lost";
                continue;
            }
            var done = State<SequenceState>(id)!.Done;
            if (mode == Sequence.Ephemeral) return $"I2 {id} is ephemeral and has a row";
            if (!done.SequenceEqual(Enumerable.Range(1, done.Count)) || done.Count > count) return $"I4 {id} did activities [{string.Join(", ", done)}] of {count}";
            if ((row.Status == InstanceStatus.Finished) != (done.Count == count)) return $"I2 {id} is {row.Status} after {done.Count} of {count} activities";
            var next = waiting[id].Count(m => m.Kind == "continue");
            if (mode == Sequence.Durable && next != (row.Status == InstanceStatus.Running ? 1 : 0)) return $"I2 {id} is {row.Status} after {done.Count} of {count} activities with {next} continue messages waiting";
        }
        return null;
    }

    // I12: a run's result is applied exactly once; a cancelled run's result never is.
    private string? Runs()
    {
        foreach (var (id, _) in Oracle.Runners)
        {
            if (State<RunnerState>(id) is not { } state) continue;
            foreach (var key in state.Log.Select(l => l[(l.IndexOf(' ') + 1)..]).Distinct())
            {
                int start = state.Log.IndexOf("start " + key), result = state.Log.IndexOf("result " + key), cancel = state.Log.IndexOf("cancel " + key);
                if (state.Log.Count(l => l == "result " + key) > 1) return $"I12 the result of run {key} of {id} was applied twice: [{string.Join(", ", state.Log)}]";
                if (result >= 0 && (start < 0 || start > result)) return $"I12 {id} got a result for run {key}, which it had not started: [{string.Join(", ", state.Log)}]";
                if (result >= 0 && cancel >= 0 && cancel < result) return $"I12 {id} got the result of run {key} after cancelling it: [{string.Join(", ", state.Log)}]";
            }
        }
        return null;
    }

    // ---- the end: faults stop, the system runs until idle, and everything sent must have come to its conclusion --------

    public string? Settle()
    {
        foreach (var actor in Actors) actor.Dead |= actor.Zombie;
        Array.Clear(Paused);
        for (var again = true; again;)      // what is on the wire arrives
        {
            again = false;
            foreach (var actor in Actors.ToList())
                for (var i = 0; i < actor.Calls.Count; i++)
                {
                    var call = actor.Calls[i];
                    if (call.Executed && (call.Answered || actor.Dead)) continue;
                    if (!call.Executed) call.Execute(Wire.Describe);
                    if (!actor.Dead) call.Answer(false);
                    again = true;
                }
        }
        Auto = true;
        var slots = Enumerable.Range(0, Config.Nodes).SelectMany(n => Enumerable.Range(0, Config.Partitions).Select(p => (Node: n, Partition: p))).ToList();
        var published = false;
        for (var round = 0; round < 150; round++)
        {
            Time += 11_000;     // past any lease and any timer
            for (var p = 0; p < Config.Partitions; p++)
            {
                foreach (var (node, _) in slots.Where(s => s.Partition == p && Of(s.Node, p).Worker.Owns)) Watch(Of(node, p).Worker.BeatAsync());   // a stale owner learns that it is one
                if (!slots.Any(s => s.Partition == p && Of(s.Node, p).Worker.Owns)) Watch(Of((p + round) % Config.Nodes, p).Worker.TakeAsync());
            }
            for (var pass = 0; pass < 8; pass++)
                foreach (var (node, partition) in slots)
                {
                    var actor = Of(node, partition);
                    var worker = actor.Worker;
                    if (!worker.Owns) continue;
                    Watch(worker.PollAsync());     // an eager host: it runs whatever is ready before it flushes
                    Watch(worker.LoadAsync());
                    Watch(worker.MeetAsync());
                    foreach (var id in worker.Ready.Order(StringComparer.Ordinal).ToList())
                    {
                        worker.Run(id);
                        if (Dying is not { } dying) continue;
                        Dying = null;
                        Crash(dying, zombie: false);
                        break;
                    }
                    if (actor.Dead) continue;
                    Watch(worker.FlushAsync());
                    foreach (var key in actor.Runs.Keys.ToList())
                        if (actor.Runs.Remove(key, out var work)) work.TrySetResult("done");
                }
            if (Check() is { } violation) return violation;
            if (Store.Messages.Values.Any(m => !m.Done) || slots.Select(s => Of(s.Node, s.Partition)).Any(a => a.Worker.Owns && (a.Worker.NeedsFlush || a.Worker.Ready.Any() || a.Runs.Count > 0))) continue;
            if (published) return End();
            if (Meet() is { } missed) return missed;
            published = true;
        }
        var left = Store.Messages.Values.Where(m => !m.Done).Select(m => $"{m.Message.Id} ({m.Message.Kind} to {m.Message.Target}, seen {m.Seen}, attempts {m.Attempts})").Take(5);
        return $"IDLE not idle after 150 rounds without faults; waiting: {string.Join("; ", left)}";
    }

    // I13: an event and a subscription that are both committed must have met. Then every wait left gets an event.
    private string? Meet()
    {
        var left = Oracle.Waiters.Keys.Where(id => Store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Running)
            .Select(id => (Id: id, State<WaiterState>(id)!.Waits)).Where(w => w.Waits.Count > 0).ToList();
        foreach (var (id, waits) in left)
            if (Store.EventRows.Values.FirstOrDefault(e => waits.Contains(e.Key) && e.KeepUntil >= Time) is { } e)
                return $"I13 event {e.Id} for key {e.Key} and the wait of {id} are both committed and never met";
        foreach (var (id, waits) in left)
            Events.PublishAsync(Store, new EventRow(waits[0], "last-" + id, null, Time + 3_600_000)).AsTask().Wait();
        return null;
    }

    private string? End()
    {
        if (Store.Messages.Values.FirstOrDefault(m => !m.Done) is { } message) return $"END message {message.Message.Id} was never consumed";
        if (Store.Subscriptions.FirstOrDefault() is { } subscription) return $"END the subscription of {subscription.Instance} to {subscription.Key} is left behind";
        foreach (var (id, children) in Oracle.Parents)
        {
            if (Store.Instances.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished }) return $"END parent {id} did not finish";
            foreach (var child in State<ParentState>(id)!.Done)
                if (State<ChildState>(child) is not { Fired: 1 }) return $"I5 the timer of child {child} fired {State<ChildState>(child)?.Fired} times";
        }
        foreach (var id in Oracle.Waiters.Keys)
            if (Store.Instances.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished }) return $"END waiter {id} did not finish";
        foreach (var (id, (count, mode, posted)) in Oracle.Sequences)
        {
            var row = Store.Instances.GetValueOrDefault(id);
            if (row is null ? !posted && mode != Sequence.Ephemeral : row.Status != InstanceStatus.Finished) return $"END sequence {id} ({mode}{(posted ? ", handed over in memory" : "")}) ended as {row?.Status.ToString() ?? "no row"}";
        }
        foreach (var (id, keys) in Oracle.Runners)
        {
            var log = State<RunnerState>(id)?.Log ?? [];
            if (keys.Find(k => !log.Contains("start " + k) || !(log.Contains("result " + k) || log.Contains("cancel " + k))) is { } key)
                return $"I12 run {key} of {id} has neither a result nor a cancel: [{string.Join(", ", log)}]";
        }
        foreach (var (id, bad) in Oracle.Fragiles)
            if (Store.Instances.GetValueOrDefault(id)?.Status != (bad ? InstanceStatus.Faulted : InstanceStatus.Running)) return $"I14 {id} ended as {Store.Instances.GetValueOrDefault(id)?.Status.ToString() ?? "no row"}";
        for (var p = 0; p < Config.Partitions; p++)
        {
            Project(p, int.MaxValue);
            if (Projected[p] != Store.Partitions[p].Position) return $"I6 the projector of partition {p} stopped at {Projected[p]} of {Store.Partitions[p].Position}";
        }
        return Faults.FirstOrDefault() ?? Check();
    }
}
