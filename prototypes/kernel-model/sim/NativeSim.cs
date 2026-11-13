using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Kernel.Graph.Models;
using Kernel.Native;
using Kernel.Sim.Native;

namespace Kernel.Sim;

/// <summary>What was sent to native instances, and what the native checks have seen so far.</summary>
public sealed class NativeOracle
{
    public readonly Dictionary<string, (string Workflow, string Mode, bool Posted)> Started = new(StringComparer.Ordinal);
    public readonly List<(string Instance, List<string> Events)> Waiting = [];
    public readonly HashSet<string> Cancelled = new(StringComparer.Ordinal);
    public readonly HashSet<(string Instance, string Key)> SettlePublished = [];
    // From the journal, read incrementally: every committed journal row is read once.
    public readonly long[] Read;
    public readonly Dictionary<string, HashSet<string>> Completed = new(StringComparer.Ordinal);    // instance -> frames completed
    public readonly Dictionary<string, List<string>> CompletedNodes = new(StringComparer.Ordinal);  // instance -> nodes, in order
    public readonly HashSet<string> Joins = new(StringComparer.Ordinal);                             // instance|join entry
    public readonly HashSet<string> Attempts = new(StringComparer.Ordinal);                          // instance|frame:attempt
    public readonly Dictionary<string, string> ChildOf = new(StringComparer.Ordinal);                // child -> parent|frame (waited)
    public readonly Dictionary<string, int> Resumes = new(StringComparer.Ordinal);                   // child -> resumes of its parent
    public readonly Dictionary<string, int> Effects = new(StringComparer.Ordinal);                   // instance|frame -> side effects run
    // The graph checks (G1-G5): what the graph engine's journal entries said, and the instance revisions already read.
    public readonly HashSet<string> Fired = new(StringComparer.Ordinal);                             // instance|frame|node|round
    public readonly Dictionary<string, List<string>> CancelledNodes = new(StringComparer.Ordinal);   // instance|frame|round -> nodes of cancelled paths
    public readonly Dictionary<string, long> GraphRevisions = new(StringComparer.Ordinal);

    public NativeOracle(int partitions) => Read = new long[partitions];
}

public sealed partial class World
{
    public readonly NativeOracle Native;
    public static long NativeReruns;        // External effects that ran again under the same committed attempt, over all runs
    public static long NativeRerunsFlagged; // of those, the ones whose turn the kernel said may have run before (ITurn.Redelivered)
    public static long NativeInstances;     // native instances compared with a run without faults, over all runs

    private NativeModel NativeModelFor(Actor actor) => new(NativeWorkflows.Registry, new NativeOptions
    {
        Flaws = Config.NativeFlaws,
        Gate = async (job, cancel) => await Work(actor, job, cancel),
        OnExternal = (instance, frame, _, attempt, again) => External(instance.Id, frame, attempt, again),
    });

    // ---- the generator's part ------------------------------------------------------------------------------------------

    public Act NativeAct(Random r, List<Actor> alive, int n)
    {
        var keep = Time + 3_600_000;
        var roll = r.Next(100);
        if (roll < 35 || Native.Waiting.Count == 0)
        {
            var (workflow, events) = NativeWorkflows.Mix[r.Next(NativeWorkflows.Mix.Length)];
            var mode = r.Next(3) switch { 0 => Modes.Ephemeral, 1 => Modes.Balanced, _ => Modes.Durable };
            var key = "j" + n;
            var input = new JsonObject { ["key"] = key };
            if (workflow == "n-pub") input["target"] = Native.Waiting.Count > 0 ? Native.Waiting[r.Next(Native.Waiting.Count)].Instance.Split('/')[1] : "nobody";
            var spec = NativeModel.Spec(workflow, mode, input);
            if (r.Next(4) == 0)
            {
                var owner = alive[r.Next(alive.Count)];
                var posted = InstanceRef.In(owner.Partition, key);
                Native.Started[posted.Id] = (workflow, mode, true);
                if (events > 0) Native.Waiting.Add((posted.Id, [.. Enumerable.Range(1, events).Select(i => $"event:{key}-{i}")]));
                return new Act("post", owner.Id, M: new Message($"x{n}", posted, Kinds.Start, Start: spec));
            }
            var target = InstanceRef.In(r.Next(Config.Partitions), key);
            Native.Started[target.Id] = (workflow, mode, false);
            if (events > 0) Native.Waiting.Add((target.Id, [.. Enumerable.Range(1, events).Select(i => $"event:{key}-{i}")]));
            return new Act("add", M: new Message($"x{n}", target, Kinds.Start, Start: spec, KeepUntil: keep));
        }
        var (instance, names) = Native.Waiting[r.Next(Native.Waiting.Count)];
        var name = names[r.Next(names.Count)];
        if (roll < 80) return new Act("publish", B: n, S: name);       // through the event tables, before or after the wait
        if (roll < 95)                                                  // a direct resume by stimulus, without the tables
            return new Act("add", M: new Message($"x{n}", new InstanceRef(instance), NativeKinds.Resume, Json.Write(new ResumeStimulus(name["event:".Length..])), KeepUntil: keep));
        Native.Cancelled.Add(instance);
        return new Act("add", M: new Message($"x{n}", new InstanceRef(instance), NativeKinds.Cancel, KeepUntil: keep));
    }

    // ---- reading the journal -------------------------------------------------------------------------------------------

    /// <summary>Reads the journal rows committed since the last call and checks what each says (N1, N2, N3).</summary>
    private void ScanJournal()
    {
        for (var p = 0; p < Config.Partitions; p++)
            for (var position = Native.Read[p] + 1; position <= Store.Partitions[p].Position; position++)
            {
                Native.Read[p] = position;
                if (!Store.Journal.TryGetValue((p, position), out var row) || row.Revision == 0) continue;
                if (Store.Instances.GetValueOrDefault(row.Instance)?.Model != NativeModel.ModelKey) continue;
                foreach (var entry in Json.Read<List<string>>(row.Entries)) Note(row.Instance, entry);
            }
        CheckGraphStates();
    }

    private void Note(string instance, string entry)
    {
        if (entry.StartsWith("g|", StringComparison.Ordinal))
        {
            NoteGraph(instance, entry);
            return;
        }
        var parts = entry.Split(':');
        switch (parts[0])
        {
            case "c":
                var frames = Native.Completed.TryGetValue(instance, out var set) ? set : Native.Completed[instance] = [];
                if (!frames.Add(parts[1])) Faults.Add($"N1 {instance}: activity frame {parts[1]} ({parts[2]}) completed twice in committed state");
                Oracle.List(Native.CompletedNodes, instance).Add(parts[2]);
                break;
            case "j":
                if (!Native.Joins.Add(instance + "|" + entry)) Faults.Add($"N2 {instance}: the join {entry[2..]} fired twice");
                break;
            case "a":
                Native.Attempts.Add($"{instance}|{parts[1]}:{parts[2]}");
                break;
            case "s":
                if (!Native.ChildOf.TryAdd(parts[2], instance + "|" + parts[1])) Faults.Add($"N3 child {parts[2]} was started twice");
                if (Native.ChildOf.Count(c => c.Value == instance + "|" + parts[1]) > 1) Faults.Add($"N3 {instance}: frame {parts[1]} started a second child");
                break;
            case "r":
                // r:wait:the child the wait is for:the child whose finish resumed it
                var (forChild, byChild) = (parts[2], parts[3]);
                if (forChild != byChild) Faults.Add($"N3 {instance}: the wait for child {forChild} was resumed by child {byChild}");
                if ((Native.Resumes[byChild] = Native.Resumes.GetValueOrDefault(byChild) + 1) > 1) Faults.Add($"N3 the finish of child {byChild} resumed its parent {instance} twice");
                break;
        }
    }

    /// <summary>
    /// G1, G2, G3 and G4 from the graph engine's journal entries (models/Native, Flowchart.Trace). The graph each entry is
    /// checked against is the definition's, not the engine's state: which connections reach a node.
    /// </summary>
    private void NoteGraph(string instance, string entry)
    {
        var p = entry.Split('|');
        var (kind, frame, flowId) = (p[1], p[2], p[3]);
        var key = instance + "|" + frame;
        if (Store.Instances.GetValueOrDefault(instance) is not { } row) return;
        var flow = NativeWorkflows.Registry.Get(row.Definition).Nodes[flowId];
        switch (kind)
        {
            case "x":
                Oracle.List(Native.CancelledNodes, key + "|" + p[5]).Add(p[4]);
                break;
            case "f":
            {
                var (node, round) = (p[4], p[5]);
                var live = p[6].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var dead = p[7].Split(',', StringSplitOptions.RemoveEmptyEntries);
                // A merge passes every token on, so a node behind one may fire once per token in a round and be released
                // by the graph going quiet with only the merge's side answered: G2 and G3 hold where no merge lies upstream.
                var graph = Flowchart.GraphOf(flow);
                var merge = flow.Gateways.Any(g => g.Value == Gateway.Merge && graph.CanReachForward(g.Key, node));
                if (live.Length == 0) Faults.Add($"G1 {instance}: a dead path ran node {node} of {flowId} (round {round}): every arrival it took was dead");
                if (!merge && !Native.Fired.Add($"{key}|{node}|{round}")) Faults.Add($"G2 {instance}: node {node} of {flowId} fired twice in round {round}");
                var inbound = flow.Connections.Where(c => c.Target == node).Select(c => c.Source).Distinct().ToList();
                if (merge || inbound.Count <= 1) break;
                // A first-wins race cancels its losers, so the join behind them is owed an arrival nobody will make; the
                // engine releases it when the graph goes quiet. That is the one way an inbound may go unanswered: from a
                // cancelled node, or from a node only a cancelled branch leads to.
                var cut = CutOff(flow, Native.CancelledNodes.GetValueOrDefault(key + "|" + round) ?? []);
                var missing = inbound.Except(live).Except(dead).Where(m => !cut.Contains(m)).ToList();
                if (missing.Count > 0) Faults.Add($"G3 {instance}: the join {node} of {flowId} fired in round {round} before {string.Join(", ", missing)} arrived, live or dead");
                break;
            }
            case "c":
                if (int.Parse(p[4]) > 0) Faults.Add($"G4 {instance}: the graph {flowId} completed with {p[4]} live paths");
                break;
        }
    }

    /// <summary>The cancelled nodes and every node all of whose inbound connections come from them, transitively.</summary>
    private static HashSet<string> CutOff(Kernel.Native.Node flow, IEnumerable<string> cancelled)
    {
        var cut = new HashSet<string>(cancelled, StringComparer.Ordinal);
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var node in flow.Children.Select(c => c.Id).Where(n => !cut.Contains(n)))
            {
                var sources = flow.Connections.Where(c => c.Target == node).Select(c => c.Source).ToList();
                if (sources.Count > 0 && sources.All(cut.Contains)) grew = cut.Add(node) || grew;
            }
        }
        return cut;
    }

    /// <summary>
    /// G1, G4 and G5 on the committed state of every native instance with a flowchart that changed since it was last
    /// read: a path that is live has its child; a graph with no live path and nothing running has completed; no path
    /// lives on in a race scope that is over, and no child runs on a cancelled path.
    /// </summary>
    private void CheckGraphStates()
    {
        foreach (var row in Store.Instances.Values)
        {
            if (row.Model != NativeModel.ModelKey || !NativeWorkflows.Graphs.Contains(row.Definition)) continue;
            if (Native.GraphRevisions.TryGetValue(row.Id, out var seen) && seen == row.Revision) continue;
            Native.GraphRevisions[row.Id] = row.Revision;
            if (row.Status != InstanceStatus.Running || Parked.Contains(row.Id)) continue;
            var state = Json.Read<NativeState>(row.State);
            var nodes = NativeWorkflows.Registry.Get(row.Definition).Nodes;
            foreach (var frame in state.Frames.Values.Where(f => nodes[f.Node].Type == typeof(Flowchart)))
            {
                if (Flowchart.ReadEngineState(frame.State) is not { } graph) continue;
                var running = state.Frames.Values.Where(f => f.Parent == frame.Id).Select(f => (f.Id, f.Tag))
                    .Concat(state.Agenda.Where(w => !w.Resume && w.Parent == frame.Id).Select(w => (Id: w.Frame, w.Tag))).ToList();
                var runningPaths = running.Select(r => Flowchart.PathOf(r.Tag)).ToHashSet(StringComparer.Ordinal);
                var live = graph.ExecutionPaths.Where(p => p.Status is ExecutionPathStatus.Active or ExecutionPathStatus.Waiting).ToList();
                if (live.Count == 0 && running.Count == 0) Faults.Add($"G4 {row.Id}: the graph {frame.Node} has no live path and has not completed");
                // A live path is a token on its way: its node runs, or it waits at a join that something running can
                // still release. Nothing running and a live path left is a token lost (the mix parks no path by policy).
                if (live.Count > 0 && running.Count == 0) Faults.Add($"G1 {row.Id}: the graph {frame.Node} has live paths ({string.Join(", ", live.Select(p => p.ExecutionPathId + " at " + p.CurrentNodeId))}) and nothing runs");
                var withChild = graph.ActiveChildren.Select(c => c.ExecutionPathId).ToHashSet(StringComparer.Ordinal);
                foreach (var path in live.Where(p => p.Status == ExecutionPathStatus.Active && !withChild.Contains(p.ExecutionPathId)))
                    Faults.Add($"G1 {row.Id}: path {path.ExecutionPathId} of {frame.Node} is active at {path.CurrentNodeId} and has no child");
                foreach (var child in graph.ActiveChildren.Where(c => !runningPaths.Contains(c.ExecutionPathId)))
                    Faults.Add($"G1 {row.Id}: path {child.ExecutionPathId} of {frame.Node} waits for node {child.NodeId}, which does not run");
                var over = graph.Scopes.Where(s => s.Kind == ExecutionScopeKind.Race && s.Status != ExecutionScopeStatus.Active).Select(s => s.ExecutionScopeId).ToHashSet(StringComparer.Ordinal);
                foreach (var path in live.Where(p => over.Contains(p.ExecutionScopeId)))
                    Faults.Add($"G5 {row.Id}: path {path.ExecutionPathId} at {path.CurrentNodeId} of {frame.Node} lives on in the race scope {path.ExecutionScopeId}, which is over");
                var cancelled = graph.ExecutionPaths.Where(p => p.Status == ExecutionPathStatus.Canceled).Select(p => p.ExecutionPathId).ToHashSet(StringComparer.Ordinal);
                foreach (var (id, tag) in running.Where(r => cancelled.Contains(Flowchart.PathOf(r.Tag) ?? "")))
                    Faults.Add($"G5 {row.Id}: child {id} of {frame.Node} runs on the cancelled path {Flowchart.PathOf(tag)}");
            }
        }
    }

    /// <summary>N4: a side effect that is not replay-safe runs, in Durable mode, only under an attempt the journal has committed.</summary>
    private void External(string instance, string frame, int attempt, bool again)
    {
        ScanJournal();
        var key = instance + "|" + frame;
        Native.Effects[key] = Native.Effects.GetValueOrDefault(key) + 1;
        if (Native.Effects[key] > 1)
        {
            Interlocked.Increment(ref NativeReruns);
            if (again) Interlocked.Increment(ref NativeRerunsFlagged);
            else Faults.Add($"N6 {instance}: the External activity of frame {frame} ran again and its turn was not told that it may have run before");
        }
        var mode = Native.Started.TryGetValue(instance, out var started) ? started.Mode : Modes.Balanced;
        if (mode == Modes.Durable && !Native.Attempts.Contains($"{key}:{Math.Max(attempt, 1)}"))
            Faults.Add($"N4 {instance}: the External activity of frame {frame} ran in Durable mode without a committed attempt");
    }

    private string? NativeCheck()
    {
        ScanJournal();
        return null;    // what the journal says is reported through Faults, which Check reads first
    }

    // ---- the end ---------------------------------------------------------------------------------------------------------

    /// <summary>At idle: every native wait that is still subscribed gets its event, once. Returns how many it published.</summary>
    private int NativeMeet()
    {
        var published = 0;
        foreach (var s in Store.Subscriptions.Where(s => s.Key.StartsWith("event:", StringComparison.Ordinal)).ToList())
        {
            if (Store.Instances.GetValueOrDefault(s.Instance) is not { Model: NativeModel.ModelKey, Status: InstanceStatus.Running } || !Native.SettlePublished.Add((s.Instance, s.Key))) continue;
            Events.PublishAsync(Store, new EventRow(s.Key, $"last-{s.Instance}-{s.Key}", null, Time + 3_600_000)).AsTask().Wait();
            published++;
        }
        return published;
    }

    /// <summary>N1, N3 and N5 at the end: everything finished, children once, and the same result as a run without faults.</summary>
    private string? NativeEnd()
    {
        ScanJournal();
        if (Faults.FirstOrDefault() is { } fault) return fault;
        foreach (var (id, (_, mode, posted)) in Native.Started)
        {
            var row = Store.Instances.GetValueOrDefault(id);
            if (row is null && !posted && mode != Modes.Ephemeral) return $"N5 native instance {id} ({mode}) was started from outside and has no row";
            if (row is not null && !Parked.Contains(id) && row.Status is not (InstanceStatus.Finished or InstanceStatus.Cancelled))
                return $"N5 native instance {id} ended as {row.Status}";
            if (row?.Status == InstanceStatus.Cancelled && !Native.Cancelled.Contains(id)) return $"N5 {id} was cancelled and nobody cancelled it";
        }
        foreach (var row in Store.Instances.Values.Where(r => r.Model == NativeModel.ModelKey))
        {
            if (Parked.Contains(row.Id) || row.Status != InstanceStatus.Finished) continue;
            var state = Json.Read<NativeState>(row.State);
            if (state.Frames.Count > 0 || state.Agenda.Count > 0 || state.Waits.Count > 0) return $"N5 {row.Id} finished with frames, work or waits left";
            var expected = NativeReference.Of(row.Definition, state.Variables.GetValueOrDefault("input"));
            var actual = NativeReference.Describe(state.Variables);
            if (actual != expected.Variables) return $"N5 {row.Id} ({row.Definition}) ended with {actual}; without faults it ends with {expected.Variables}";
            if (row.Definition is "n-join2" or "n-graph") { Interlocked.Increment(ref NativeInstances); continue; }   // a threshold join: which branch activities complete depends on how events were batched; variables only
            var types = NativeReference.Types(row.Definition, Native.CompletedNodes.GetValueOrDefault(row.Id) ?? []);
            if (types != expected.Types) return $"N1 {row.Id} ({row.Definition}) completed [{types}]; without faults it completes [{expected.Types}]";
            Interlocked.Increment(ref NativeInstances);
        }
        foreach (var (child, parent) in Native.ChildOf)
        {
            var owner = parent.Split('|')[0];
            if (Store.Instances.GetValueOrDefault(owner) is { Status: InstanceStatus.Finished } && !Parked.Contains(owner) && Native.Resumes.GetValueOrDefault(child) != 1)
                return $"N3 child {child} of {owner} resumed it {Native.Resumes.GetValueOrDefault(child)} times";
        }
        return null;
    }
}

/// <summary>Set from the command line: native instances in the mix, and the model's deliberate flaws.</summary>
public static class NativeSwitch
{
    public static bool On;
    public static NativeFlaw Flaws;

    /// <summary>The check each of the graph engine's flaws is for; the mutation check fails if another catches it first.</summary>
    public static readonly Dictionary<NativeFlaw, string> ExpectedCheck = new()
    {
        [NativeFlaw.NoDeadPaths] = "G3", [NativeFlaw.NoRoundReset] = "G2", [NativeFlaw.DeadPathRunsNode] = "G1", [NativeFlaw.CancelLeavesPaths] = "G5",
    };
}

/// <summary>The same workflow and input, run alone on one worker with no faults: what a faulty run must end with.</summary>
public static class NativeReference
{
    private static readonly ConcurrentDictionary<string, (string Variables, string Types)> Cache = new(StringComparer.Ordinal);

    public static string Describe(Dictionary<string, JsonNode?> variables) =>
        string.Join(", ", variables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value?.ToJsonString() ?? "null"}"));

    public static string Types(string workflow, IEnumerable<string> nodes)
    {
        var all = NativeWorkflows.Registry.Get(workflow).Nodes;
        return string.Join(", ", nodes.Select(n => all[n].Type.Name).GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
    }

    public static (string Variables, string Types) Of(string workflow, JsonNode? input) =>
        Cache.GetOrAdd(workflow + "|" + input?.ToJsonString(), _ => Run(workflow, input as JsonObject));

    private static (string, string) Run(string workflow, JsonObject? input)
    {
        var time = 1_000_000L;
        var store = new InMemoryStore(1, () => time);
        var worker = new Worker("reference", 0, store, new WorkflowInstance([new NativeModel(NativeWorkflows.Registry)]), new WorkerOptions { Monotonic = () => time });
        new DurableHost([worker]).ActivateAsync(0).AsTask().Wait();
        worker.PollAsync().Wait();
        var id = InstanceRef.In(0, "reference");
        worker.Post(new Message("start", id, Kinds.Start, Start: NativeModel.Spec(workflow, Modes.Balanced, input?.DeepClone().AsObject())), fresh: true);
        var published = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 200 && store.Instances.GetValueOrDefault(id.Id)?.Status is null or InstanceStatus.Running; round++)
        {
            worker.WokenAsync(null).AsTask().Wait();      // one pass that reads the inbox
            worker.RunAsync().AsTask().Wait();
            foreach (var s in store.Subscriptions.Where(s => s.Instance == id.Id).ToList())
                if (published.Add(s.Key)) Events.PublishAsync(store, new EventRow(s.Key, "e-" + s.Key, null, time + 3_600_000)).AsTask().Wait();
            time += 1_000;
        }
        var row = store.Instances[id.Id];
        if (row.Status != InstanceStatus.Finished) throw new InvalidOperationException($"the reference run of {workflow} ended as {row.Status}");
        var nodes = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position)
            .SelectMany(j => Json.Read<List<string>>(j.Entries)).Where(e => e.StartsWith("c:", StringComparison.Ordinal)).Select(e => e.Split(':')[2]);
        return (Describe(Json.Read<NativeState>(row.State).Variables), Types(workflow, nodes));
    }
}
