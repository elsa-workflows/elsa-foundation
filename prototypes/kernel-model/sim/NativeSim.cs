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
    // Round 5 C (N7): what each instance sent, by node/path (the key without its instance and attempt), for the comparison
    // with the fault-free reference run at the end; every (instance, key) seen, to count the resends; which key first went
    // out under each frame number, to count the frame numbers a replay handed on; and a record of every activation's key.
    public readonly Dictionary<string, HashSet<string>> Sent = new(StringComparer.Ordinal);         // instance -> node/path sent
    public readonly HashSet<string> KeysSeen = new(StringComparer.Ordinal);                          // instance|key
    public readonly HashSet<string> LandedKeys = new(StringComparer.Ordinal);                        // instance|key whose turn's commit the store landed
    public readonly Dictionary<string, string> ContentKeys = new(StringComparer.Ordinal);           // instance|node|content -> key, every sending, lost turns included
    public readonly Dictionary<string, string> FrameOwners = new(StringComparer.Ordinal);           // instance|frame:attempt -> node/path
    public readonly Dictionary<string, (string Key, string Content)> Outbound = new(StringComparer.Ordinal); // instance|node|path:attempt -> first key and content (a record for the witnesses)
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
    public static long NativeSameKeyReruns; // outbound effects (HTTP, email) sent again with the key of their first sending, over all runs
    public static long NativeReusedFrames;  // outbound effects whose frame number another activation had already sent under (a Balanced or Ephemeral replay over a longer inbox), over all runs
    public static long NativeRevisits;      // outbound effects from a node that had already sent under another iteration path in the same instance, over all runs

    private NativeModel NativeModelFor(Actor actor) => new(NativeWorkflows.Registry, new NativeOptions
    {
        Flaws = Config.NativeFlaws,
        AdmissionFlaws = Config.AdmissionFlaws,
        Gate = async (job, cancel) => await Work(actor, job, cancel),
        OnExternal = (instance, frame, _, attempt, again) => External(actor, instance.Id, frame, attempt, again),
        OnOutbound = s => Outbound(actor, s),
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
        instance = NativeCreation(instance);
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
        var key = NativeCreation(instance) + "|" + frame;
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
            var identity = NativeCreation(row.Id);
            if (Native.GraphRevisions.TryGetValue(identity, out var seen) && seen == row.Revision) continue;
            Native.GraphRevisions[identity] = row.Revision;
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
    private void External(Actor actor, string instance, string frame, int attempt, bool again)
    {
        ScanJournal();
        var key = NativeCreation(instance) + "|" + frame;
        Native.Effects[key] = Native.Effects.GetValueOrDefault(key) + 1;
        if (Native.Effects[key] > 1)
        {
            Interlocked.Increment(ref NativeReruns);
            if (again) Interlocked.Increment(ref NativeRerunsFlagged);
        }
        var observed = actor.TurnHistoryHint.GetValueOrDefault(instance);
        if (again != observed)
            Faults.Add($"N6 {instance}: delivery-history hint {again} disagrees with the metadata observed before its mark ({observed})");
        var mode = Native.Started.TryGetValue(instance, out var started) ? started.Mode : Modes.Balanced;
        if (mode == Modes.Durable && !Native.Attempts.Contains($"{key}:{Math.Max(attempt, 1)}"))
            Faults.Add($"N4 {instance}: the External activity of frame {frame} ran in Durable mode without a committed attempt");
    }

    /// <summary>
    /// N7 (round 5 C), the part seen at the host's transport: an outbound effect carries its effect key where its receiver
    /// looks (the HTTP header, the Message-ID). The identity checks are at the end (<see cref="NativeEnd"/>), against the
    /// fault-free reference run, so that they do not read the key's own components. Here the sweep also counts the resends
    /// (the same instance sending one key again: a lost turn result, a takeover), the frame numbers a replay handed to
    /// another activation and the nodes sent from twice, so that the mix is shown to produce the cases the key exists for.
    /// </summary>
    private void Outbound(Actor actor, OutboundSending s)
    {
        var (node, frame, path, attempt, key, effect) = (s.Node, s.Frame, s.IterationPath, s.Attempt, s.EffectKey, s.Effect);
        actor.PendingSendings.Add((s.Instance.Ref.Id, key));
        // The lost-turn half: an activation is named here by its node and the content it alone determines (the mix's rule:
        // bodies name the iteration), and that name has one key in every sending, in a turn that lands or in one that is
        // lost. A lost turn that sent another activation's valid key is seen here and nowhere else.
        if (NativeWorkflows.ContentNamesActivation.Contains(s.Node))
        {
            var named = $"{s.Instance.Ref.Id}|{s.Node}|{(effect switch { HttpSendEffect h => h.Request.Body?.ToJsonString() ?? "null", EmailSendEffect e => e.Mail.Subject + "|" + e.Mail.Body, _ => "" })}";
            if (!Native.ContentKeys.TryAdd(named, key) && Native.ContentKeys[named] != key)
                Faults.Add($"N7 {s.Instance.Ref.Id}: {s.Node} with '{named.Split('|', 3)[2]}' was sent under two keys, {Native.ContentKeys[named]} and {key}: a sending used another activation's key");
        }
        var what = effect switch
        {
            HttpSendEffect h => h.Request.Headers.TryGetValue(SendHttpRequest.IdempotencyKeyHeader, out var header) && header == key ? null
                : $"the HTTP request of {node} does not carry its effect key {key} in {SendHttpRequest.IdempotencyKeyHeader}",
            EmailSendEffect e => e.Mail.MessageId == SendEmail.MessageId(key) ? null
                : $"the email of {node} does not carry its effect key {key} as its Message-ID: '{e.Mail.MessageId}'",
            _ => $"an unknown outbound effect {effect.GetType().Name}",
        };
        var id = s.Instance.Ref.Id;
        if (what is not null) Faults.Add($"N7 {id}: {what}");
        var sent = Native.Sent.TryGetValue(id, out var set) ? set : Native.Sent[id] = new(StringComparer.Ordinal);
        var local = NativeReference.NodeAndPath(key);
        if (!Native.KeysSeen.Add($"{id}|{key}")) Interlocked.Increment(ref NativeSameKeyReruns);
        else if (sent.Any(k => k.StartsWith(node + "/", StringComparison.Ordinal))) Interlocked.Increment(ref NativeRevisits);
        sent.Add(local);
        if (!Native.FrameOwners.TryAdd($"{id}|{frame}:{attempt}", local) && Native.FrameOwners[$"{id}|{frame}:{attempt}"] != local) Interlocked.Increment(ref NativeReusedFrames);
        var content = effect switch { HttpSendEffect h => h.Request.Body?.ToJsonString() ?? "null", EmailSendEffect e => e.Mail.Subject + "|" + e.Mail.Body, _ => "" };
        Native.Outbound.TryAdd($"{CreationProtocol.Origin(id, s.Instance.Creation)}|{node}|{path}:{attempt}", (key, content));
    }

    /// <summary>
    /// N7 (round 5 C), the landed half: a second sending of a key is a redelivery only while the first one's turn did not
    /// land (its commit was lost, refused, or never made because its owner died). When both turns landed, the key left in
    /// two turns that are not a redelivery of each other: two activations under one key, whatever the reference run says
    /// (its one run takes every available event in one turn, so a collision that needs two turns hides from it). Called by
    /// the wire at the moment the store lands the commit, so a commit whose answer is then lost still counts.
    /// </summary>
    public void Landed((string Instance, string Key)[] sendings)
    {
        foreach (var group in sendings.GroupBy(s => $"{s.Instance}|{s.Key}"))
        {
            var (instance, key) = group.First();
            if (group.Count() > 1) Faults.Add($"N7 {instance}: the key {key} left twice in one landed turn");
            if (!Native.LandedKeys.Add(group.Key) && group.Count() == 1) Faults.Add($"N7 {instance}: the key {key} left in two turns that both landed and are not a redelivery of each other");
        }
    }

    /// <summary>
    /// N7 (round 5 C), the identity half, against the fault-free reference run of the same definition and input: the
    /// oracle takes no identity from the sending's own node, path or frame fields; it compares the key string, stripped of
    /// its instance origin and attempt, with the keys the reference run minted on its own. The reference sends every key
    /// once, so two activations never share one; and every key an instance sent here is one the reference sends, so one
    /// logical activation keeps its key across a lost turn and its replay, on whichever owner. What a resend carries as
    /// content is not compared: the receiver keeps the first sending (the owner's rule). The mix must hold to this: a
    /// workflow whose key set depends on event or timer order (a join whose outside inbound may arrive last, a While whose
    /// limit a sibling raises, a ForEach over a collection a sibling grows) would be a false red here, as n-join2 and
    /// n-graph would be for N5; those two are excepted, and none of them sends. An instance without a row at the end (an
    /// Ephemeral one that finished in memory) is not compared; the smoke's and the variants' Ephemeral runs cover it.
    /// </summary>
    private string? NativeKeys()
    {
        foreach (var (id, sent) in Native.Sent)
        {
            if (Store.Instances.GetValueOrDefault(id) is not { Model: NativeModel.ModelKey } row || row.Definition is "n-join2" or "n-graph") continue;
            var expected = NativeReference.Of(row.Definition, Json.Read<NativeState>(row.State).Variables.GetValueOrDefault("input"));
            if (expected.DuplicateKeys.Count > 0) return $"N7 {row.Definition}: a run without faults sends {expected.DuplicateKeys[0]} twice: two activations share one key";
            foreach (var local in sent)
                if (!expected.Keys.Contains(local)) return $"N7 {id} ({row.Definition}) sent {local}, which a run without faults never sends: an activation's key moved";
        }
        return null;
    }

    private string? NativeCheck()
    {
        ScanJournal();
        return null;    // what the journal says is reported through Faults, which Check reads first
    }

    private string NativeCreation(string instance) => CreationProtocol.Origin(instance, Store.Instances.GetValueOrDefault(instance)?.Creation ?? 0);

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
        if (NativeKeys() is { } keys) return keys;
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
            if (row.Definition is "n-join2" or "n-graph") { Interlocked.Increment(ref NativeInstances); continue; }   // a threshold join (how events were batched) or a race (which racer won) decides which activities complete; variables only
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
        [NativeFlaw.UnstableEffectKey] = "N7", [NativeFlaw.NodelessEffectKey] = "N7", [NativeFlaw.PathlessEffectKey] = "N7", [NativeFlaw.TurnLocalJoinOrdinal] = "N7",
        [NativeFlaw.FlatRetryGeneration] = "N7",   // the sweep's mix has no retry; the smoke's double-retry shape shows it bites (NATIVE.md)
        [NativeFlaw.FreshTurnSwapsLoopKeys] = "N7",
    };
}

/// <summary>What a run without faults ends with: its variables, the types it completed, the effect keys it sent (by node/path) and any key it sent twice.</summary>
public sealed record Reference(string Variables, string Types, HashSet<string> Keys, List<string> DuplicateKeys);

/// <summary>The same workflow and input, run alone on one worker with no faults: what a faulty run must end with.</summary>
public static class NativeReference
{
    private static readonly ConcurrentDictionary<string, Reference> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The key without its instance origin and attempt: <c>node/path</c>, the same for every instance of a definition and
    /// in every mode. Whatever follows the attempt's digits stays (a mutation's suffix), so that a key minted outside the
    /// committed facts is judged as what it is.
    /// </summary>
    public static string NodeAndPath(string key) => key.Split('/') is { Length: >= 3 } parts ? parts[^3] + "/" + parts[^2] + parts[^1].TrimStart("0123456789".ToCharArray()) : key;

    private static NativeFlaw KeyFlaws => NativeSwitch.Flaws & (NativeFlaw.UnstableEffectKey | NativeFlaw.NodelessEffectKey | NativeFlaw.PathlessEffectKey);

    public static string Describe(Dictionary<string, JsonNode?> variables) =>
        string.Join(", ", variables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value?.ToJsonString() ?? "null"}"));

    public static string Types(string workflow, IEnumerable<string> nodes)
    {
        var all = NativeWorkflows.Registry.Get(workflow).Nodes;
        return string.Join(", ", nodes.Select(n => all[n].Type.Name).GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
    }

    public static Reference Of(string workflow, JsonNode? input) =>
        Cache.GetOrAdd(workflow + "|" + input?.ToJsonString() + "|" + (int)KeyFlaws, _ => Run(workflow, input as JsonObject));   // the mutation check runs every flaw in one process

    private static Reference Run(string workflow, JsonObject? input)
    {
        var time = 1_000_000L;
        var store = new InMemoryStore(1, () => time);
        // The reference carries the effect-key mutations (and only those), so that a mutated key is judged against itself:
        // a key the reference sends twice is a collision, a key the run sends and the reference never does has moved.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        var model = new NativeModel(NativeWorkflows.Registry, new NativeOptions
        {
            Flaws = KeyFlaws,     // the join-ordinal and retry flaws are left out: the reference must stay the fault-free truth about them
            OnOutbound = s =>
            {
                var local = NodeAndPath(s.EffectKey); if (!keys.Add(local)) duplicates.Add(local);
                // The lost-turn half's rule enforces itself: a node listed in ContentNamesActivation sends every body once here.
                var body = s.Effect switch { HttpSendEffect h => h.Request.Body?.ToJsonString() ?? "null", EmailSendEffect e => e.Mail.Subject + "|" + e.Mail.Body, _ => "" };
                if (NativeWorkflows.ContentNamesActivation.Contains(s.Node) && !bodies.Add(s.Node + "|" + body))
                    throw new InvalidOperationException($"{workflow}: node {s.Node} is listed in ContentNamesActivation and sends '{body}' twice in a run without faults; its body does not name its activation");
            },
        });
        var worker = new Worker("reference", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => time });
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
        return new(Describe(Json.Read<NativeState>(row.State).Variables), Types(workflow, nodes), keys, duplicates);
    }
}
