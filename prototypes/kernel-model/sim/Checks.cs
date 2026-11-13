namespace Kernel.Sim;

/// <summary>What was sent from outside, and therefore what must come of it.</summary>
public sealed class Oracle
{
    public readonly HashSet<string> Sent = new(StringComparer.Ordinal);                 // ids of outside messages
    public readonly Dictionary<string, List<string>> Counters = new();                 // target -> tokens from outside, in the order added
    public readonly HashSet<string> Ghosts = [];                                         // targets that were never given a start spec
    public readonly Dictionary<string, ParentInput> Parents = new();
    public readonly HashSet<string> Cancelled = [];                                      // parents that were sent a cancel
    public readonly Dictionary<string, List<string>> Waiters = new();
    public readonly Dictionary<string, (int Count, string Mode, bool Posted)> Sequences = new();
    public readonly Dictionary<string, List<string>> Runners = new();                  // target -> keys told to go
    public readonly Dictionary<string, bool> Fragiles = new();                          // target -> a bad message was sent
    public readonly Dictionary<string, List<string>> CWaiters = new();                 // waiters through correlation instances
    public readonly HashSet<string> Published = [];                                      // correlation instances that were sent an event
    public readonly Dictionary<string, (string Mode, bool Ephemeral, int PostedBy, bool Cancel)> Https = new();   // instances that answer requests
    public readonly HashSet<string> Trees = [];                                          // roots of trees started from outside
    public readonly Dictionary<string, List<string>> Events = new();                   // key -> events published
    public readonly List<SubscriptionRow> Starts = [];                                   // start subscriptions registered
    public readonly Dictionary<string, long> InMemory = new(StringComparer.Ordinal);      // decision 4: instances kept in memory, unwritten, and their first revision there
    public readonly HashSet<string> Deleted = [];                                        // instances that were sent a delete

    public bool Accepts(Message m) => m.Kind != "cancel" || Parents.ContainsKey(m.Target.Id) || (Runners.TryGetValue(m.Target.Id, out var keys) && keys.Contains(m.Read<string>()));

    public void Note(Message m, bool posted = false)
    {
        if (!Sent.Add(m.Id)) return;
        var target = m.Target.Id;
        if (m.Kind == "cancel" && Parents.ContainsKey(target)) Cancelled.Add(target);
        if (m.Kind == Kinds.Delete) Deleted.Add(target);
        switch (m.Start?.Model)
        {
            case "counter": List(Counters, target).Add(m.Read<Add>().Token); break;
            case "parent": Parents[target] = Json.Read<ParentInput>(m.Start.Input!); break;
            case "waiter": Waiters[target] = Json.Read<List<string>>(m.Start.Input!); break;
            case "cwaiter": CWaiters[target] = Json.Read<List<string>>(m.Start.Input!); break;
            case "correlation": Published.Add(target); break;
            case "tree": Trees.Add(target); break;
            case "http": Https[target] = (Json.Read<HttpInput>(m.Start.Input!).Mode, m.Start.Ephemeral, -1, false); break;
            case "sequence": var input = Json.Read<SequenceInput>(m.Start.Input!); Sequences[target] = (input.Count, input.Mode, posted); break;
            case "runner": List(Runners, target).Add(m.Read<string>()); break;
            case "fragile": Fragiles[target] = Fragiles.GetValueOrDefault(target) || m.Kind is "throw" or "spoil" or "die"; break;
            case null when m.Kind == "add": Ghosts.Add(target); break;
        }
    }

    public static List<string> List(Dictionary<string, List<string>> map, string key) => map.TryGetValue(key, out var list) ? list : map[key] = [];
}

public sealed partial class World
{
    public readonly Oracle Oracle = new();
    private readonly Dictionary<string, (long Revision, InstanceStatus Status)> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _late = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gone = new(StringComparer.Ordinal);    // deleted, and seen gone
    private readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);   // instances whose row first appeared after their parent had ended
    private readonly Dictionary<(Type, string), object> _parsed = new();
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);  // per message, as last seen
    public readonly HashSet<string> Parked = new(StringComparer.Ordinal);               // instances parked by the rule of invariant 14
    public static long Bystanders;      // how many of those had done nothing wrong, over all runs
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Seen = new();     // what happened, over all runs: coverage

    public static void Count(string what, int times = 1) => Seen.AddOrUpdate(what, times, (_, n) => n + times);

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
        if (Config.Native) ScanJournal();      // the native checks read the journal (NativeSim.cs)
        if (Faults.Count > 0) return Faults[0];
        _watched.RemoveAll(t => t.IsCompletedSuccessfully);
        if (_watched.Find(t => t.IsFaulted) is { } bug) return "BUG " + bug.Exception!.InnerException;
        var waiting = Store.Messages.Values.Where(m => !m.Done).ToLookup(m => m.Message.Target.Id, m => m.Message);
        // I14, the counting half: attempts change only in a take (see World.Take).
        foreach (var row in Store.Messages.Values)
            if (row.Attempts != _attempts.GetValueOrDefault(row.Message.Id)) return $"I14 the attempts of {row.Message.Id} changed to {row.Attempts} outside a take";
        return Journal() ?? Memory() ?? Counters(waiting) ?? Trees(waiting) ?? Waits() ?? Sequences(waiting) ?? Jobs() ?? Correlated() ?? Answers();
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
            if (count[p] + Store.Deleted[p] != position || top[p] != position) return $"I6 partition {p} is at position {position} and its journal has {count[p]} rows up to {top[p]}, {Store.Deleted[p]} deleted";
            if (Store.Projection[p] > position) return $"I6 the projector of partition {p} is at {Store.Projection[p]}, past position {position}";
        }
        // I6 for the view: each view row counts exactly the journal rows of its instance up to its projector's position.
        var projected = Store.Journal.Values.Where(j => j.Revision > 0 && j.Position <= Store.Projection[j.Partition]).GroupBy(j => j.Instance)
            .ToDictionary(g => g.Key, g => (Revision: g.Max(j => j.Revision), Commits: g.Count()), StringComparer.Ordinal);
        var deletions = Store.Journal.Values.Where(j => j.Revision < 0).ToDictionary(j => j.Instance, j => j, StringComparer.Ordinal);
        foreach (var id in deletions.Keys.Where(id => Oracle.Deleted.Contains(id) && !Store.Instances.ContainsKey(id))) _gone.Add(id);   // also those created and deleted between two checks
        foreach (var (id, view) in Store.Views)
            if (deletions.TryGetValue(id, out var deletion) && deletion.Position > Store.Projection[deletion.Partition]) continue;    // deleted; the projector has not got there yet
            else if (!projected.TryGetValue(id, out var expected) || expected != (view.Revision, view.Commits))
                return $"I6 the view of {id} says revision {view.Revision} after {view.Commits} commits; the journal up to the projector says {expected.Revision} after {expected.Commits}";
        if (projected.Keys.FirstOrDefault(id => !Store.Views.ContainsKey(id)) is { } unseen) return $"I6 the projector passed the journal rows of {unseen} and has no view of it";
        foreach (var (id, revisions) in commits)
        {
            revisions.Sort();
            if (Store.Instances.GetValueOrDefault(id) is not { } row) return $"I2 the journal has a commit of {id} and there is no row";
            if (row.History == History.Minimal) return $"I6 {id} keeps minimal history and has journal rows";
            // Decision 4: an Ephemeral instance that waited in memory is written at the revision it had there, so its
            // journal starts at that revision, never before the first one it had in memory, and has no gap after it.
            var first = Oracle.InMemory.TryGetValue(id, out var kept) && revisions[0] >= kept ? revisions[0] : 1;
            if (revisions.Count != row.Revision - first + 1 || revisions[0] != first || revisions[^1] != row.Revision) return $"I2 {id} is at revision {row.Revision} and the journal has its commits [{string.Join(", ", revisions)}]";
        }
        foreach (var row in Store.Instances.Values)
        {
            if (!commits.ContainsKey(row.Id) && row.History != History.Minimal) return $"I2 {row.Id} has a row and no journal row";
            // I18: a state is read at its own version or upgraded; no model writes back a state older than the row's.
            if (_versions.TryGetValue(row.Id, out var version) && row.StateVersion < version) return $"I18 {row.Id} went from state version {version} back to {row.StateVersion}";
            _versions[row.Id] = row.StateVersion;
            // I19: a rebind is the instance's from the next turn on, in its row and in every turn that follows.
            if (row.Model == "counter" && State<CounterState>(row.Id) is { } bound && (bound.Wrong.Count > 0 || row.Definition != (bound.Bound ?? Counter.Spec.Definition)))
                return $"I19 {row.Id} is bound to {bound.Bound ?? Counter.Spec.Definition}, its row says {row.Definition}{(bound.Wrong.Count > 0 ? ", and a turn " + bound.Wrong[0] : "")}";
            if (row.Model == "counter" && row.StateVersion == 2 && State<CounterState>(row.Id) is { } counted && counted.Count != counted.Tokens.Count)
                return $"I18 {row.Id} at state version 2 counts {counted.Count} tokens and holds {counted.Tokens.Count}";
            if (_known.TryGetValue(row.Id, out var before) && (row.Revision < before.Revision || (before.Status != InstanceStatus.Running && row.Status != before.Status)))
                return $"I10 {row.Id} went from revision {before.Revision} {before.Status} to revision {row.Revision} {row.Status}";
            if (!_known.ContainsKey(row.Id) && row.Parent is { } up && Store.Instances.GetValueOrDefault(up) is { Status: not InstanceStatus.Running }) _late.Add(row.Id);
            _known[row.Id] = (row.Revision, row.Status);
            if (row.Status != InstanceStatus.Faulted) continue;
            // I14: parked for losing owners only at the threshold; otherwise only an instance that really fails is parked.
            var poison = row.Fault is { } fault && fault.StartsWith("message ") ? fault[8..fault.IndexOf(" was on trial")] : null;
            if (poison is not null && _attempts.GetValueOrDefault(poison) < Config.Attempts) return $"I14 {row.Id} was parked before the threshold: {row.Fault}";
            if (poison is null && !Oracle.Fragiles.GetValueOrDefault(row.Id)) return $"I14 {row.Id} did nothing wrong and was parked: {row.Fault}";
            if (poison is not null && Parked.Add(row.Id) && !Oracle.Fragiles.GetValueOrDefault(row.Id)) Interlocked.Increment(ref Bystanders);
        }
        // I22: a row disappears only when its instance was deleted, and a deleted instance never comes back.
        if (_known.Keys.FirstOrDefault(id => !Store.Instances.ContainsKey(id) && !_gone.Contains(id)) is { } gone)
        {
            if (!Oracle.Deleted.Contains(gone) || !deletions.ContainsKey(gone)) return $"I10 the row of {gone} disappeared";
            _gone.Add(gone);
        }
        if (_gone.FirstOrDefault(Store.Instances.ContainsKey) is { } back) return $"I22 {back} was deleted and has a row again";
        return Oracle.Ghosts.FirstOrDefault(Store.Instances.ContainsKey) is { } ghost ? $"I10 {ghost} was created by a message without a start spec" : null;
    }

    // I9: at its current epoch, what an owner holds equals the committed rows plus its own uncommitted turns.
    private string? Memory()
    {
        foreach (var actor in Alive)
        {
            var (worker, partition) = (actor.Worker, Store.Partitions[actor.Partition]);
            if (!worker.Owns || worker.Epoch != partition.Epoch) continue;
            // I21: no more idle instances in memory than the bound.
            if (worker.Idle > 2) return $"I21 actor {actor.Id} holds {worker.Idle} idle instances; the bound is 2";
            if (!worker.Flushing && worker.Position != partition.Position)
                return $"I9 actor {actor.Id} believes partition {actor.Partition} is at {worker.Position}; it is at {partition.Position}";
            var unwritten = worker.Unwritten.ToHashSet(StringComparer.Ordinal);
            var busyNow = worker.Instances.Where(i => i.Busy).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in unwritten)       // decision 4: in memory only, so never a row (unless the commit that writes it is in flight)
                if (Store.Instances.ContainsKey(id) && !busyNow.Contains(id))
                    return $"I9 actor {actor.Id} holds {id} in memory only, and it has a row";
            foreach (var (id, row, state, busy) in worker.Instances)
            {
                if (unwritten.Contains(id)) continue;
                if (row is { Model: "counter", StateVersion: 2 } && actor.Version == 1) Count("a version-1 owner holds a counter row at version 2");
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
            if (Parked.Contains(target)) continue;
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
            if (!Parked.Contains(target) && State<CounterState>(target) is { } state && !InOrder(state.Tokens, tokens))
                return $"I11 {target} applied [{string.Join(", ", state.Tokens)}]; {sender} sent it [{string.Join(", ", tokens)}] in that order";
        return null;
    }

    // I2, I3 for a parent and its children: each pending child is at exactly one stage, and a message stands for that stage.
    private string? Trees(ILookup<string, Message> waiting)
    {
        foreach (var (id, (children, delay, _)) in Oracle.Parents)
        {
            if (Parked.Contains(id)) continue;
            if (State<ParentState>(id) is not { } state)
            {
                if (!Store.Instances.ContainsKey(id) && !waiting[id].Any()) return $"I3 the start of {id} is lost";
                continue;
            }
            if (state.Done.Find(d => d.StartsWith('!')) is { } unexpected) return $"I4 {id} was told {unexpected}";
            if (state.Pending.Count + state.Done.Count != children || state.Done.Distinct().Count() != state.Done.Count)
                return $"I4 {id} should have {children} children; pending [{string.Join(", ", state.Pending)}], done [{string.Join(", ", state.Done)}]";
            if ((Store.Instances[id].Status != InstanceStatus.Running) != (state.Pending.Count == 0)) return $"I2 {id} is {Store.Instances[id].Status} with {state.Pending.Count} children pending";
            if (Store.Instances[id].Status == InstanceStatus.Cancelled && !Oracle.Cancelled.Contains(id)) return $"I2 {id} was cancelled and nobody asked";
            foreach (var child in state.Pending)
            {
                // A child is at one stage, wherever it lives: not created (its start waits), running (its timer waits, if
                // it has one), or ended (its child-finished waits for the parent). A cancel or a stale timer may also wait.
                var row = Store.Instances.GetValueOrDefault(child);
                var start = waiting[child].Count(m => m.Kind == Kinds.Start);
                var timer = waiting[child].Count(m => m.Kind == "timer");
                var finished = waiting[id].Count(m => m.Kind == WorkflowKinds.ChildFinished && m.Read<ChildFinished>().Child == child);
                var stage = row is null ? (start, timer, finished) == (1, 0, 0)
                    : row.Status == InstanceStatus.Running ? (start, timer, finished) == (0, delay >= 0 ? 1 : 0, 0)
                    : (start, finished) == (0, 1) && timer <= (row.Status == InstanceStatus.Cancelled ? 1 : 0);
                if (!stage) return $"I2 child {child} of {id}: row {row?.Status.ToString() ?? "none"}, {start} start, {timer} timer, {finished} child-finished waiting";
            }
            foreach (var child in state.Done)
                if (Store.Instances.GetValueOrDefault(child) is not { Status: InstanceStatus.Finished or InstanceStatus.Faulted or InstanceStatus.Cancelled } || (!Parked.Contains(child) && waiting[child].Any(m => !Kinds.IsControl(m.Kind) && m.Kind is not ("timer" or "cancel")))) return $"I2 {id} counts {child} as done and it is not";
        }
        return null;
    }

    // I8: the subscriptions of an instance are exactly the waits in its committed state. I13: an event is delivered once.
    private string? Waits()
    {
        var rows = Store.Subscriptions.Where(s => s.Start is null).ToLookup(s => s.Instance, s => s.Key);
        // I20: an event starts at most one instance per start subscription, and that instance takes exactly that event.
        foreach (var row in Store.Instances.Values.Where(i => i.Model == "onevent"))
            if (State<OnEventState>(row.Id) is { } got && (got.Got.Count > 1 || got.Got.Exists(id => !row.Id.EndsWith(id.Replace("ev:", "on:").Replace("::", ":")))))
                return $"I20 {row.Id} was started by [{string.Join(", ", got.Got)}]";
        // A running tree leaf waits; nothing else that is no waiter may, and a terminated leaf's wait is gone with its commit.
        if (rows.FirstOrDefault(g => !Oracle.Waiters.ContainsKey(g.Key) && Store.Instances.GetValueOrDefault(g.Key) is not { Model: "tree" or Kernel.Native.NativeModel.ModelKey, Status: InstanceStatus.Running }) is { } stray)
            return $"I8 {stray.Key} has a subscription and is {(Store.Instances.GetValueOrDefault(stray.Key) is { } row ? row.Status.ToString() : "no waiter")}";
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
                if (!posted && mode != Sequence.Ephemeral && !waiting[id].Any() && !_gone.Contains(id)) return $"I3 the start of {id} is lost";
                continue;
            }
            if (Parked.Contains(id)) continue;
            var done = State<SequenceState>(id)!.Done;
            if (mode == Sequence.Ephemeral) return $"I2 {id} is ephemeral and has a row";
            if (!done.SequenceEqual(Enumerable.Range(1, done.Count)) || done.Count > count) return $"I4 {id} did activities [{string.Join(", ", done)}] of {count}";
            if ((row.Status == InstanceStatus.Finished) != (done.Count == count)) return $"I2 {id} is {row.Status} after {done.Count} of {count} activities";
            var next = waiting[id].Count(m => m.Kind == "continue");
            if (mode == Sequence.Durable && next != (row.Status == InstanceStatus.Running ? 1 : 0)) return $"I2 {id} is {row.Status} after {done.Count} of {count} activities with {next} continue messages waiting";
        }
        return null;
    }

    // I15: a caller gets at most one response and at most one end, and a response only from a turn that committed or had
    // nothing to commit. A turn had nothing to commit only if its instance is ephemeral and was never written.
    private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
    private string? Answers()
    {
        foreach (var (process, request, instance) in Asked)
        {
            if (request.Responses + request.Ends > 1) return $"I15 request {request.Id} on {process} got {request.Responses} responses and {request.Ends} ends";
            if (request.Answer is not string answer || !_answered.Add(request.Id)) continue;
            var committed = Store.Journal.Values.Any(j => j.Instance == instance && j.Entries.Contains($"\"{answer}\""));
            var unwritten = Oracle.Https[instance].Ephemeral && !Store.Instances.ContainsKey(instance);
            if (!committed && !unwritten) return $"I15 request {request.Id} was answered {answer}, which no commit of {instance} wrote";
        }
        foreach (var (id, (_, _, posted, _)) in Oracle.Https)
            if (State<HttpState>(id) is { } state && state.Ambient.Find(a => a != $"seen@{posted}") is { } stray) return $"I15 {id} was started by actor {posted} and a turn saw the ambient as {stray}";
        return null;
    }

    // I13 through correlation instances: no wait is given two events, and one that was given an event has finished.
    private string? Correlated()
    {
        foreach (var id in Oracle.CWaiters.Keys)
        {
            if (Parked.Contains(id) || State<WaiterState>(id) is not { } state) continue;
            if (state.Got.Count > 1) return $"I13 {id} was given [{string.Join(", ", state.Got)}]";
            if ((Store.Instances[id].Status == InstanceStatus.Finished) != (state.Got.Count == 1)) return $"I2 {id} is {Store.Instances[id].Status} having been given {state.Got.Count} events";
        }
        return null;
    }

    // The correlation instances of a waiter's keys; null when one of them was parked (then the wait is dead with it).
    private List<string>? Hubs(string waiter) =>
        Oracle.CWaiters[waiter].Select(key => Correlation.Of(key, Config.Partitions).Id).ToList() is var hubs && hubs.Exists(Parked.Contains) ? null : hubs;

    // I12: a job's result is applied exactly once; a cancelled job's result never is.
    private string? Jobs()
    {
        foreach (var (id, _) in Oracle.Runners)
        {
            if (State<RunnerState>(id) is not { } state) continue;
            foreach (var key in state.Log.Select(l => l[(l.IndexOf(' ') + 1)..]).Distinct())
            {
                int start = state.Log.IndexOf("start " + key), result = state.Log.IndexOf("result " + key), cancel = state.Log.IndexOf("cancel " + key);
                if (state.Log.Count(l => l == "result " + key) > 1) return $"I12 the result of job {key} of {id} was applied twice: [{string.Join(", ", state.Log)}]";
                if (result >= 0 && (start < 0 || start > result)) return $"I12 {id} got a result for job {key}, which it had not started: [{string.Join(", ", state.Log)}]";
                if (result >= 0 && cancel >= 0 && cancel < result) return $"I12 {id} got the result of job {key} after cancelling it: [{string.Join(", ", state.Log)}]";
            }
        }
        return null;
    }

    // ---- the end: faults stop, the system runs until idle, and everything sent must have come to its conclusion --------

    public string? Settle()
    {
        Deployed = true;
        foreach (var start in CatchingUp) Events.CatchUpAsync(Store, start, Config.Partitions).AsTask().Wait();     // a registrar retries until it has read
        CatchingUp.Clear();
        for (var node = 0; node < Config.Nodes; node++)     // the deployment finishes: every node still at version 1 restarts at 2
            if (Versions[node] == 1) Crash(node, zombie: false);
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
            }
            for (var n = 0; n < Config.Nodes; n++) Watch(Runtimes[(n + round) % Config.Nodes].BalanceAsync());   // every node beats and asks for its share
            foreach (var (node, reply) in Deliveries.ToList())      // the transport delivers what it still carries
                if (Processes.TryGetValue(node, out var requests) && Running(node)) requests.Receive(reply);
            Deliveries.Clear();
            foreach (var requests in Processes.Values) requests.Expire(Time);
            foreach (var row in Store.Instances.Values.Where(i => i.Suspended).ToList())     // the outsider resumes whatever is suspended
                Store.AddAsync(new Message($"resume-{row.Id}.{row.Revision}", new InstanceRef(row.Id), Kinds.Resume, KeepUntil: Time + 3_600_000)).AsTask().Wait();
            for (var pass = 0; pass < 8; pass++)
                foreach (var (node, partition) in slots)
                {
                    var actor = Of(node, partition);
                    var worker = actor.Worker;
                    if (!worker.Owns) continue;
                    if (!actor.Runtime.Owns(partition)) Faults.Add($"BUG actor {actor.Id} owns partition {partition} and its runtime does not know it");
                    // An eager host: every pass reads the inbox, and runs whatever is ready before it flushes. A process that
                    // dies (now, or before the pass) cancels it: no turn after that, and no flush.
                    using (_pass = new CancellationTokenSource())
                    {
                        if (Dying is not null) _pass.Cancel();
                        Watch(actor.Runtime.WakeAsync(InstanceRef.In(partition, "")).AsTask());     // over the runtime's wake signal
                        Watch(actor.Runtime.PassAsync(partition, _pass.Token).AsTask());
                    }
                    _pass = null;
                    if (Dying is { } dying)     // a turn or a background job killed the process
                    {
                        Dying = null;
                        Crash(dying, zombie: false);
                        continue;
                    }
                    foreach (var key in actor.Jobs.Keys.ToList())
                        if (actor.Jobs.Remove(key, out var work)) work.TrySetResult("done");
                }
            if (Check() is { } violation) return violation;
            // I15, decision 6: after a pass, an owner holds no caller past its deadline, so no expired caller pins an instance.
            foreach (var (node, partition) in slots)
                if (Of(node, partition).Worker is { Owns: true } owner && owner.Callers.FirstOrDefault(c => ReplyTo.Parse(c).Deadline <= owner.Now) is { } late)
                    return $"I15 actor {Of(node, partition).Id} still holds the reply address {late} past its deadline";
            if (Store.Messages.Values.Any(m => !m.Done && m.Message.Kind != "expire") || slots.Select(s => Of(s.Node, s.Partition)).Any(a => a.Worker.Owns && (a.Worker.NeedsFlush || a.Worker.Ready.Any() || a.Jobs.Count > 0))) continue;
            if (Unfair() is not null) continue;
            if (published && (!Config.Native || NativeMeet() == 0)) return End();    // native waits that appeared since get their events too
            if (Meet() is { } missed) return missed;
            published = true;
        }
        var left = Store.Messages.Values.Where(m => !m.Done).Select(m => $"{m.Message.Id} ({m.Message.Kind} to {m.Message.Target}, seen {m.Seen}, attempts {m.Attempts})").Take(5);
        return $"IDLE not idle after 150 rounds without faults; waiting: {string.Join("; ", left)}{(Unfair() is { } unfair ? "; " + unfair : "")}";
    }

    // Fair share, once faults stop: every partition has one owner at its current epoch, and no node holds more than its share.
    private string? Unfair()
    {
        var share = (Config.Partitions + Config.Nodes - 1) / Config.Nodes;
        var owned = new int[Config.Nodes];
        for (var p = 0; p < Config.Partitions; p++)
        {
            var owners = Enumerable.Range(0, Config.Nodes).Where(n => Of(n, p).Worker.Owns && Of(n, p).Worker.Epoch == Store.Partitions[p].Epoch).ToList();
            if (owners.Count != 1) return $"partition {p} has {owners.Count} owners";
            owned[owners[0]]++;
        }
        return owned.Max() > share ? $"the nodes own [{string.Join(", ", owned)}] partitions; the share is {share}" : null;
    }

    // I13: an event and a subscription that are both committed must have met. Then every wait left gets an event.
    private string? Meet()
    {
        foreach (var (id, input) in Oracle.Parents)     // a parent whose children wait for ever is cancelled
            if (input.DelayMs < 0 && Store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Running && Oracle.Cancelled.Add(id))
                Store.AddAsync(new Message("cancel-" + id, new InstanceRef(id), "cancel", KeepUntil: Time + 3_600_000)).AsTask().Wait();
        foreach (var root in Oracle.Trees)      // only the roots: the kernel must reach the rest
            if (Store.Instances.GetValueOrDefault(root) is { } row && (row.Status == InstanceStatus.Running || row.Children is not null))
                Store.AddAsync(new Message("end-" + root, new InstanceRef(root), Kinds.Terminate, KeepUntil: Time + 3_600_000)).AsTask().Wait();
        foreach (var (id, _) in Oracle.Https.Where(h => h.Value.Mode == Http.Wait))   // whatever still waits is resumed, with nobody asking
            if (Store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Running) Store.AddAsync(new Message("last-" + id, new InstanceRef(id), "resume", KeepUntil: Time + 3_600_000)).AsTask().Wait();
        var left = Oracle.Waiters.Keys.Where(id => Store.Instances.GetValueOrDefault(id)?.Status == InstanceStatus.Running)
            .Select(id => (Id: id, State<WaiterState>(id)!.Waits)).Where(w => w.Waits.Count > 0).ToList();
        foreach (var (id, waits) in left)
            if (Store.EventRows.Values.FirstOrDefault(e => waits.Contains(e.Key) && e.KeepUntil >= Time) is { } e)
                return $"I13 event {e.Id} for key {e.Key} and the wait of {id} are both committed and never met";
        foreach (var (id, waits) in left)
            Events.PublishAsync(Store, new EventRow(waits[0], "last-" + id, null, Time + 3_600_000), Config.Partitions).AsTask().Wait();
        // The same through correlation instances: a subscription and an event for one key, both committed, must have met.
        foreach (var (id, keys) in Oracle.CWaiters)
        {
            if (Parked.Contains(id) || Hubs(id) is not { } hubs || Store.Instances.GetValueOrDefault(id)?.Status != InstanceStatus.Running) continue;
            if (hubs.Find(Oracle.Published.Contains) is { } hub) return $"I13 the wait of {id} and an event sent to {hub} never met";
            Store.AddAsync(Correlation.Published("last-" + id, keys[0], Config.Partitions, new Publish("last-" + id, null, 3_600_000), Time + 3_600_000)).AsTask().Wait();
        }
        // Native waits do not ask for kept events (the owner's default of 12 November), so an event published before
        // one committed is not owed to it: they get, once, what is published at idle (NativeSim.cs).
        if (Config.Native) NativeMeet();
        return null;
    }

    private string? End()
    {
        if (Store.Messages.Values.FirstOrDefault(m => !m.Done && m.Message.Kind != "expire") is { } message) return $"END message {message.Message.Id} was never consumed";
        // I20 at the end: every event and every start subscription of its key, both committed, made exactly one instance.
        var started = Store.Instances.Values.Where(i => i.Model == "onevent").Select(i => i.Id).Concat(_gone.Where(id => id.Contains("/on:"))).Select(id => id[(id.IndexOf('/') + 1)..]).ToHashSet();
        Count("instances deleted", _gone.Count);
        foreach (var start in Oracle.Starts)
            foreach (var e in Oracle.Events.GetValueOrDefault(start.Key) ?? [])
                if (!started.Remove($"on:{e}:{start.Wait}")) return $"I20 event {e} for key {start.Key} and start subscription {start.Wait} are both committed and no instance was started";
        if (started.Count > 0) return $"I20 instances [{string.Join(", ", started)}] were started by no event and subscription";
        if (Store.Instances.Values.FirstOrDefault(i => i.Model == "onevent" && i.Status != InstanceStatus.Finished) is { } open) return $"I20 {open.Id} ended as {open.Status}";
        if (Store.Subscriptions.FirstOrDefault(s => s.Start is null && !Parked.Contains(s.Instance)) is { } subscription) return $"END the subscription of {subscription.Instance} to {subscription.Key} is left behind";
        foreach (var (id, (_, delay, spread)) in Oracle.Parents)
        {
            if (Parked.Contains(id)) continue;
            if (Store.Instances.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished or InstanceStatus.Cancelled }) return $"END parent {id} did not finish";
            foreach (var child in State<ParentState>(id)!.Done)
            {
                var ended = Store.Instances[child].Status;
                if (!Parked.Contains(child) && ended == InstanceStatus.Finished && State<ChildState>(child) is not { Fired: 1 }) return $"I5 the timer of child {child} fired {State<ChildState>(child)?.Fired} times";
                if (ended == InstanceStatus.Cancelled) Count("child cancelled by its parent" + (child[..child.IndexOf('/')] != id[..id.IndexOf('/')] ? ", in another partition" : ""));
            }
        }
        foreach (var id in Oracle.Waiters.Keys)
            if (!Parked.Contains(id) && !_gone.Contains(id) && Store.Instances.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished }) return $"END waiter {id} did not finish";
        foreach (var id in Oracle.CWaiters.Keys)
            if (!Parked.Contains(id) && Hubs(id) is not null && Store.Instances.GetValueOrDefault(id) is not { Status: InstanceStatus.Finished }) return $"END waiter {id} (through correlation) did not finish";
        foreach (var (id, (count, mode, posted)) in Oracle.Sequences)
        {
            var row = Store.Instances.GetValueOrDefault(id);
            if (!Parked.Contains(id) && !_gone.Contains(id) && (row is null ? !posted && mode != Sequence.Ephemeral : row.Status != InstanceStatus.Finished)) return $"END sequence {id} ({mode}{(posted ? ", handed over in memory" : "")}) ended as {row?.Status.ToString() ?? "no row"}";
        }
        foreach (var (id, keys) in Oracle.Runners)
        {
            var log = State<RunnerState>(id)?.Log ?? [];
            if (!Parked.Contains(id) && Store.Instances.GetValueOrDefault(id)?.Status != InstanceStatus.Cancelled && keys.Find(k => !log.Contains("start " + k) || !(log.Contains("result " + k) || log.Contains("cancel " + k))) is { } key)
                return $"I12 job {key} of {id} has neither a result nor a cancel: [{string.Join(", ", log)}]";
        }
        foreach (var row in Store.Instances.Values.Where(i => i.Model == "tree" && i.Parent is not null && i.Status == InstanceStatus.Cancelled))
            Count("tree descendant cancelled" + (row.Id[..row.Id.IndexOf('/')] != row.Parent![..row.Parent!.IndexOf('/')] ? ", in another partition" : ", with its parent") + (_late.Contains(row.Id) ? ", created after its parent ended" : ""));
        Count("instances evicted", (int)Actors.Sum(a => a.Worker.Evicted));
        // I17: a terminate reaches every descendant, in any partition, including those whose start was on its way.
        if (Store.Instances.Values.FirstOrDefault(i => i.Model == "tree" && i.Status == InstanceStatus.Running) is { } alive)
            return $"I17 tree instance {alive.Id} still runs after its root was terminated";
        if (Store.Instances.Values.FirstOrDefault(i => i.Model == "tree" && i.Children is not null) is { } parent)
            return $"I17 terminated tree instance {parent.Id} still lists children {parent.Children}";
        if (Store.Instances.Values.FirstOrDefault(i => i.Suspended) is { } suspended) return $"END {suspended.Id} is still suspended after its resume";
        foreach (var (process, request, instance) in Asked)
        {
            if (!request.Done && Running(process)) return $"I15 request {request.Id} on {process} for {instance} still waits";
            Count("request " + (request.Answer is not null ? "answered" : request.Error ?? $"ended {request.Status}") + (Oracle.Https[instance].PostedBy < 0 ? ", resume" : ", start"));
        }
        foreach (var (id, (mode, ephemeral, posted, cancel)) in Oracle.Https)
        {
            var row = Store.Instances.GetValueOrDefault(id);
            if (row is null ? posted < 0 : row.Status is not (InstanceStatus.Finished or InstanceStatus.Faulted) && !(cancel && row.Status == InstanceStatus.Cancelled))
                return $"END {id} ({mode}) ended as {row?.Status.ToString() ?? "no row"}{(cancel ? " after a cancel" : "")}";
        }
        foreach (var (id, bad) in Oracle.Fragiles)
            if (!Parked.Contains(id) && Store.Instances.GetValueOrDefault(id)?.Status != (bad ? InstanceStatus.Faulted : InstanceStatus.Running)) return $"I14 {id} ended as {Store.Instances.GetValueOrDefault(id)?.Status.ToString() ?? "no row"}";
        for (var p = 0; p < Config.Partitions; p++)
        {
            for (var round = 0; round < 1000 && Store.Projection[p] < Store.Partitions[p].Position; round++) Watch(Of(round % Config.Nodes, p).Projector.RoundAsync());
            if (Store.Projection[p] != Store.Partitions[p].Position) return $"I6 the projector of partition {p} stopped at {Store.Projection[p]} of {Store.Partitions[p].Position}";
        }
        if (Config.Native && NativeEnd() is { } native) return native;
        return Faults.FirstOrDefault() ?? Check();
    }
}
