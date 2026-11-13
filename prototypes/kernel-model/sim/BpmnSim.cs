using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Model.State;
using Kernel.Bpmn;

namespace Kernel.Sim;

/// <summary>What was sent to BPMN instances, and what the token checks have read from the journal so far.</summary>
public sealed class BpmnOracle(int partitions)
{
    public readonly Dictionary<string, (string Process, JsonObject Variables, bool Posted)> Started = new(StringComparer.Ordinal);
    public readonly List<string> Orders = [];                                                       // b-early's correlation keys
    public readonly HashSet<string> Terminated = new(StringComparer.Ordinal);
    public readonly HashSet<string> SettlePublished = new(StringComparer.Ordinal);
    public readonly long[] Read = new long[partitions];
    public readonly Dictionary<string, HashSet<string>> Arrivals = new(StringComparer.Ordinal);   // instance -> tokens a join consumed
    public readonly Dictionary<string, HashSet<string>> Merged = new(StringComparer.Ordinal);     // instance -> tokens a join made
    public readonly Dictionary<string, List<string>> Joins = new(StringComparer.Ordinal);         // instance -> the joins that fired, in order
    public readonly Dictionary<string, HashSet<string>> Stopped = new(StringComparer.Ordinal);    // instance -> elements whose work it stopped
}

public sealed partial class World
{
    public readonly BpmnOracle Bpmn;
    public static long BpmnInstances;       // BPMN instances compared with a run without faults, over all runs

    private BpmnModel BpmnModelFor(Actor actor) => new(BpmnMix.Registry, new BpmnOptions
    {
        Flaws = Config.BpmnFlaws,
        Gate = async (job, cancel) => await Work(actor, job, cancel),
    });

    // ---- the generator's part ------------------------------------------------------------------------------------------

    public Act BpmnAct(Random r, List<Actor> alive, int n)
    {
        var keep = Time + 3_600_000;
        var roll = r.Next(100);
        if (roll < 60 || Bpmn.Started.Count == 0)
        {
            var process = BpmnMix.Processes[r.Next(BpmnMix.Processes.Length)];
            var key = "b" + n;
            var variables = BpmnMix.Input(process, key, r);
            if (process == "b-early") Bpmn.Orders.Add(key);
            var spec = BpmnModel.Spec(process, variables);
            if (r.Next(4) == 0)
            {
                var owner = alive[r.Next(alive.Count)];
                return new Act("post", owner.Id, M: new Message($"x{n}", InstanceRef.In(owner.Partition, key), Kinds.Start, Start: spec));
            }
            return new Act("add", M: new Message($"x{n}", InstanceRef.In(r.Next(Config.Partitions), key), Kinds.Start, Start: spec, KeepUntil: keep));
        }
        if (roll < 88 && Bpmn.Orders.Count > 0)     // a message for an order: before or after its catch subscribes, now and then twice
        {
            var key = BpmnModel.MessageKey("paid", Bpmn.Orders[r.Next(Bpmn.Orders.Count)]);
            Count(Store.Subscriptions.Any(s => s.Key == key) ? "bpmn message published to a waiting catch" : "bpmn message published before its catch");
            return new Act("publish", B: n, S: key);
        }
        var running = Store.Instances.Values.Where(i => i.Model == BpmnModel.ModelKey && i.Status == InstanceStatus.Running).Select(i => i.Id).Order(StringComparer.Ordinal).ToList();
        if (running.Count == 0) return new Act("tick", r.Next(50));
        return new Act("add", M: new Message($"x{n}", new InstanceRef(running[r.Next(running.Count)]), Kinds.Terminate, KeepUntil: keep));
    }

    /// <summary>What the oracle learns when an act is applied, so that a replay without the generator knows it too.</summary>
    private void BpmnApplied(Message m, bool posted)
    {
        if (m is { Kind: Kinds.Start, Start: { Model: BpmnModel.ModelKey } spec } && !Bpmn.Started.ContainsKey(m.Target.Id))
            Bpmn.Started[m.Target.Id] = (spec.Definition, Json.Read<BpmnStart>(spec.Input!).Variables ?? [], posted);
        if (m.Kind == Kinds.Terminate && Store.Instances.GetValueOrDefault(m.Target.Id)?.Model == BpmnModel.ModelKey) Bpmn.Terminated.Add(m.Target.Id);
    }

    // ---- reading the journal: G2, G3 -----------------------------------------------------------------------------------

    private void ScanBpmn()
    {
        for (var p = 0; p < Config.Partitions; p++)
            for (var position = Bpmn.Read[p] + 1; position <= Store.Partitions[p].Position; position++)
            {
                Bpmn.Read[p] = position;
                if (!Store.Journal.TryGetValue((p, position), out var row) || row.Revision == 0) continue;
                if (Store.Instances.GetValueOrDefault(row.Instance) is not { Model: BpmnModel.ModelKey } instance) continue;
                foreach (var entry in Json.Read<List<string>>(row.Entries)) NoteBpmn(instance, entry);
            }
    }

    private void NoteBpmn(InstanceRow instance, string entry)
    {
        var parts = entry.Split(' ');
        var id = instance.Id;
        // The kernel parks a job that was on trial when too many owners were lost (FINDINGS section 7, rule 3): its result is
        // an error, an uncaught one faults the process, and the fault is the kernel's. So is a caller's whose call failed so.
        if ((parts[0] == "e" && entry.Contains(" was on trial when ", StringComparison.Ordinal)) || (parts[0] == "r" && Parked.Contains(parts[2])))
        {
            if (Parked.Add(id)) Interlocked.Increment(ref Bystanders);
            Oracle.Fragiles[id] = true;     // so I14 counts its fault as the kernel's
            Count(parts[0] == "e" ? "bpmn job parked by the kernel" : "bpmn call failed by a parked child");
        }
        if (parts[0] == "x") (Bpmn.Stopped.TryGetValue(id, out var stopped) ? stopped : Bpmn.Stopped[id] = new(StringComparer.Ordinal)).Add(parts[2]);
        if (parts[0] != "j") return;
        var (element, merged) = (parts[1], parts[2]);
        var arrivals = parts.Length > 3 && parts[3] != "" ? parts[3].Split(',').Select(a => (Token: a[..a.LastIndexOf('/')], Flow: a[(a.LastIndexOf('/') + 1)..])).ToList() : [];
        Oracle.List(Bpmn.Joins, id).Add(element);
        // G2: a join fires at most once per round: no arrival is consumed twice, and no firing is recorded twice.
        if (!(Bpmn.Merged.TryGetValue(id, out var made) ? made : Bpmn.Merged[id] = new(StringComparer.Ordinal)).Add(merged)) Faults.Add($"G2 {id}: the firing of join {element} that made {merged} is in the journal twice");
        var seen = Bpmn.Arrivals.TryGetValue(id, out var set) ? set : Bpmn.Arrivals[id] = new(StringComparer.Ordinal);
        foreach (var a in arrivals)
            if (!seen.Add(a.Token)) Faults.Add($"G2 {id}: join {element} consumed arrival {a.Token} that an earlier firing consumed");
        // G3: a parallel join fires once every inbound flow has arrived, one arrival each; an inclusive join on arrivals it can take.
        var definition = BpmnMix.Registry.Get(instance.Definition).Definition;
        var inbound = definition.SequenceFlows.Where(f => f.TargetRef == element).Select(f => f.FlowId).Order(StringComparer.Ordinal).ToList();
        var flows = arrivals.Select(a => a.Flow).Order(StringComparer.Ordinal).ToList();
        var type = definition.Elements.First(e => e.ElementId == element).ElementType;
        if (flows.Distinct().Count() != flows.Count || flows.Count == 0 || flows.Except(inbound).Any()) Faults.Add($"G3 {id}: join {element} fired on [{string.Join(", ", flows)}]; its inbound flows are [{string.Join(", ", inbound)}]");
        else if (type == BpmnElementTypes.ParallelGateway && !flows.SequenceEqual(inbound)) Faults.Add($"G3 {id}: parallel join {element} fired on [{string.Join(", ", flows)}] of [{string.Join(", ", inbound)}]");
    }

    // ---- after every action: G1, G4, G5 against the committed rows -------------------------------------------------------

    private static bool LiveToken(BpmnToken t) => t.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin;

    private string? BpmnCheck()
    {
        ScanBpmn();
        if (Faults.Count > 0) return Faults[0];
        var subscriptions = Store.Subscriptions.Where(s => s.Start is null).ToLookup(s => s.Instance);
        var timers = Store.Messages.Values.Where(m => !m.Done && m.Message.Kind == BpmnKinds.Timer).ToLookup(m => m.Message.Target.Id, m => m.Message.Id);
        foreach (var row in Store.Instances.Values)
        {
            if (row.Model != BpmnModel.ModelKey || Parked.Contains(row.Id) || State<BpmnState>(row.Id) is not { } s) continue;
            var jobs = row.Jobs is { } j ? Json.Read<List<string>>(j) : [];
            if (row.Status == InstanceStatus.Cancelled)
            {
                // Terminated by the kernel: it cancelled the jobs and deleted the waits; the state stays as it was.
                if (jobs.Count > 0 || subscriptions[row.Id].Any()) return $"G5 {row.Id} was terminated and keeps jobs [{string.Join(", ", jobs)}] or subscriptions";
                continue;
            }
            // G5: no cancelled token keeps a stopped handle: the host reported every unit it stopped.
            if (s.Tokens.Tokens.FirstOrDefault(t => t.StoppedHandle is not null) is { } stopped) return $"G5 {row.Id}: cancelled token {stopped.TokenId} at {stopped.AtElementId} still holds stopped handle {stopped.StoppedHandle}";
            if (row.Status != InstanceStatus.Running)
            {
                // G4: an instance completes exactly when no live token and no work remain.
                if (row.Status == InstanceStatus.Finished && (s.Tokens.Tokens.Any(LiveToken) || s.Live.Count > 0 || s.Tokens.ActiveWork.Count > 0))
                    return $"G4 {row.Id} finished with live tokens [{string.Join(", ", s.Tokens.Tokens.Where(LiveToken).Select(t => t.AtElementId))}] or work [{string.Join(", ", s.Live.Keys)}]";
                if (jobs.Count > 0 || subscriptions[row.Id].Any() || timers[row.Id].Any()) return $"G5 {row.Id} ended {row.Status} and keeps jobs, subscriptions or timers";
                continue;
            }
            // G1, token conservation: the interpreter's work in flight, the host's ledger and the kernel's work are one set.
            var active = s.Tokens.ActiveWork.Select(w => w.TokenId).Order(StringComparer.Ordinal);
            if (!active.SequenceEqual(s.Live.Values.Select(w => w.Token).Order(StringComparer.Ordinal)))
                return $"G1 {row.Id}: the interpreter waits on tokens [{string.Join(", ", active)}]; the ledger holds [{string.Join(", ", s.Live.Values.Select(w => w.Token))}]";
            foreach (var (handle, w) in s.Live)
            {
                var there = w.Kind switch
                {
                    WorkKinds.Job => jobs.Contains(w.Key),
                    WorkKinds.Timer => timers[row.Id].Contains(w.Key),
                    WorkKinds.Message => subscriptions[row.Id].Any(x => x.Key == w.Key && x.Wait == handle),
                    _ => Store.Instances.ContainsKey(w.Key) || Store.Messages.Values.Any(m => !m.Done && m.Message.Target.Id == w.Key && m.Message.Kind == Kinds.Start),
                };
                if (!there) return $"G1 {row.Id}: unit {handle} ({w.Kind} {w.Key} at {w.Element}) is in the ledger and the kernel has no {w.Kind} for it";
            }
            var keys = s.Live.Values.Where(w => w.Kind == WorkKinds.Job).Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
            if (jobs.FirstOrDefault(k => !keys.Contains(k)) is { } job) return $"G1 {row.Id}: job {job} runs and the ledger has no unit for it";
            if (subscriptions[row.Id].FirstOrDefault(x => !s.Live.TryGetValue(x.Wait, out var w) || w.Kind != WorkKinds.Message) is { } sub) return $"G1 {row.Id}: subscription {sub.Key}/{sub.Wait} and the ledger has no catch for it";
            if (timers[row.Id].FirstOrDefault(t => !s.Live.Values.Any(w => w.Kind == WorkKinds.Timer && w.Key == t)) is { } timer) return $"G1 {row.Id}: timer {timer} is pending and the ledger has no unit for it";
        }
        return null;
    }

    // ---- the end -------------------------------------------------------------------------------------------------------

    /// <summary>At idle: every catch still waiting gets its message, once. Returns how many it published.</summary>
    private int BpmnMeet()
    {
        var published = 0;
        foreach (var s in Store.Subscriptions.Where(s => s.Key.StartsWith("msg:", StringComparison.Ordinal)).ToList())
        {
            if (Store.Instances.GetValueOrDefault(s.Instance) is not { Model: BpmnModel.ModelKey, Status: InstanceStatus.Running } || !Bpmn.SettlePublished.Add(s.Key)) continue;
            Events.PublishAsync(Store, new EventRow(s.Key, $"last-{s.Key}", null, Time + 3_600_000), Config.Partitions).AsTask().Wait();
            published++;
        }
        return published;
    }

    /// <summary>G4 and G5 at the end, and the result of every finished instance against a run without faults.</summary>
    private string? BpmnEnd()
    {
        if (BpmnCheck() is { } violation) return violation;
        foreach (var (id, (process, _, posted)) in Bpmn.Started)
        {
            var row = Store.Instances.GetValueOrDefault(id);
            if (row is null && !posted) return $"G4 BPMN instance {id} ({process}) was started from outside and has no row";
            if (row is null || Parked.Contains(id)) continue;
            if (row.Status == InstanceStatus.Cancelled ? !Bpmn.Terminated.Contains(id) : row.Status != InstanceStatus.Finished) return $"G4 BPMN instance {id} ({process}) ended as {row.Status}";
        }
        foreach (var row in Store.Instances.Values.Where(r => r.Model == BpmnModel.ModelKey && !Parked.Contains(r.Id)))
        {
            if (row.Status == InstanceStatus.Running) return $"G4 BPMN instance {row.Id} ({row.Definition}) still runs at idle";
            if (row.Parent is not null && row.Status == InstanceStatus.Cancelled)
            {
                // G5 for a call activity: its child was terminated because the call was stopped, or its parent was terminated.
                var parent = Store.Instances.GetValueOrDefault(row.Parent);
                if (!(Bpmn.Stopped.GetValueOrDefault(row.Parent)?.Contains("call") ?? false) && parent?.Status != InstanceStatus.Cancelled && !Bpmn.Terminated.Contains(row.Id))
                    return $"G5 child {row.Id} was cancelled and its parent {row.Parent} ({parent?.Status}) neither stopped its call nor was terminated";
                continue;
            }
            if (row.Status != InstanceStatus.Finished || row.Parent is not null) continue;
            var (_, input, _) = Bpmn.Started[row.Id];
            var s = State<BpmnState>(row.Id)!;
            var actual = BpmnReference.Describe(s.Variables);
            var joins = string.Join(" ", Bpmn.Joins.GetValueOrDefault(row.Id) ?? []);
            // A boundary timer won when it stopped the work it races: then the run without faults is the one where it wins.
            var stopped = Bpmn.Stopped.GetValueOrDefault(row.Id) ?? [];
            var timed = row.Definition switch { "b-boundary" => stopped.Contains("slow"), "b-call" => stopped.Contains("call"), _ => false };
            var expected = BpmnReference.Of(row.Definition, input, timerFirst: timed);
            if (actual != expected.Variables) return $"G6 {row.Id} ({row.Definition}) ended with {actual}; without faults it ends with {expected.Variables}";
            if (joins != expected.Joins) return $"G2 {row.Id} ({row.Definition}) fired joins [{joins}]; without faults it fires [{expected.Joins}]";
            Interlocked.Increment(ref BpmnInstances);
        }
        return null;
    }
}

/// <summary>Set from the command line: BPMN instances in the mix, and the host's deliberate flaws.</summary>
public static class BpmnSwitch
{
    public static bool On;
    public static BpmnFlaw Flaws;
}

/// <summary>The spike's cases as the simulation starts them (BPMN.md, section 5).</summary>
public static class BpmnMix
{
    public static readonly ProcessRegistry Registry = BpmnCases.Registry();
    public static readonly string[] Processes = ["b-parallel", "b-exclusive", "b-inclusive", "b-boundary", "b-early", "b-call", "b-loop"];

    public static JsonObject Input(string process, string key, Random r) => process switch
    {
        "b-exclusive" => new JsonObject { ["amount"] = r.Next(200) },
        "b-inclusive" => new JsonObject { ["express"] = r.Next(2) == 0, ["insured"] = r.Next(2) == 0 },
        "b-early" or "b-call" => new JsonObject { ["order"] = key },
        _ => [],
    };
}

/// <summary>The same process and input, run alone on one worker with no faults: what a faulty run must end with.</summary>
public static class BpmnReference
{
    private static readonly ConcurrentDictionary<string, (string Variables, string Joins)> Cache = new(StringComparer.Ordinal);

    public static string Describe(Dictionary<string, JsonNode?> variables) =>
        string.Join(", ", variables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value?.ToJsonString() ?? "null"}"));

    /// <summary><paramref name="timerFirst"/>: the jobs a boundary timer races never finish, so the timer wins.</summary>
    public static (string Variables, string Joins) Of(string process, JsonObject input, bool timerFirst) =>
        Cache.GetOrAdd($"{process}|{input.ToJsonString()}|{timerFirst}", _ => Run(process, input, timerFirst));

    private static (string, string) Run(string process, JsonObject input, bool timerFirst)
    {
        var time = 1_000_000L;
        var store = new InMemoryStore(1, () => time);
        var model = new BpmnModel(BpmnMix.Registry, new BpmnOptions
        {
            Gate = async (job, cancel) =>
            {
                if (timerFirst && Json.Read<JobArgs>(job.Arguments!).Service is "slow" or "child") await Task.Delay(Timeout.Infinite, cancel);
            },
        });
        var worker = new Worker("reference", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => time });
        new DurableHost([worker]).ActivateAsync(0).AsTask().Wait();
        worker.PollAsync().Wait();
        var id = InstanceRef.In(0, "reference");
        worker.Post(new Message("start", id, Kinds.Start, Start: BpmnModel.Spec(process, input.DeepClone().AsObject())), fresh: true);
        var published = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 1000 && store.Instances.GetValueOrDefault(id.Id)?.Status is null or InstanceStatus.Running; round++)
        {
            worker.WokenAsync(null).AsTask().Wait();
            worker.RunAsync().AsTask().Wait();
            foreach (var s in store.Subscriptions.Where(s => s.Instance == id.Id).ToList())
                if (published.Add(s.Key)) Events.PublishAsync(store, new EventRow(s.Key, "e-" + s.Key, null, time + 3_600_000)).AsTask().Wait();
            time += 10;     // slowly: a job finishes well before a boundary timer of 300 ms, unless timerFirst holds it
        }
        var row = store.Instances[id.Id];
        if (row.Status != InstanceStatus.Finished) throw new InvalidOperationException($"the reference run of {process} ended as {row.Status}");
        var joins = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position)
            .SelectMany(j => Json.Read<List<string>>(j.Entries)).Where(e => e.StartsWith("j ", StringComparison.Ordinal)).Select(e => e.Split(' ')[1]);
        return (Describe(Json.Read<BpmnState>(row.State).Variables), string.Join(" ", joins));
    }
}
