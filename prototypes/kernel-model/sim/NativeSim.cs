using System.Collections.Concurrent;
using System.Text.Json.Nodes;
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

    public NativeOracle(int partitions) => Read = new long[partitions];
}

public sealed partial class World
{
    public readonly NativeOracle Native;
    public static long NativeReruns;        // External effects that ran again under the same committed attempt, over all runs
    public static long NativeRerunsFlagged; // of those, the ones whose step the kernel said may have run before (IStep.Redelivered)
    public static long NativeInstances;     // native instances compared with a run without faults, over all runs

    private NativeModel NativeModelFor(Actor actor) => new(NativeWorkflows.Registry, new NativeOptions
    {
        Flaws = Config.NativeFlaws,
        Gate = async (run, cancel) => await Work(actor, run, cancel),
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
    }

    private void Note(string instance, string entry)
    {
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
            else Faults.Add($"N6 {instance}: the External activity of frame {frame} ran again and its step was not told that it may have run before");
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
            if (row.Definition == "n-join2") { Interlocked.Increment(ref NativeInstances); continue; }   // a threshold join: which branch activities complete depends on how events were batched; variables only
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
        var worker = new Worker("reference", 0, store, [new NativeModel(NativeWorkflows.Registry)], new WorkerOptions { Monotonic = () => time });
        worker.TakeAsync().Wait();
        worker.PollAsync().Wait();
        var id = InstanceRef.In(0, "reference");
        worker.Post(new Message("start", id, Kinds.Start, Start: NativeModel.Spec(workflow, Modes.Balanced, input?.DeepClone().AsObject())), fresh: true);
        var published = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 200 && store.Instances.GetValueOrDefault(id.Id)?.Status is null or InstanceStatus.Running; round++)
        {
            worker.PollAsync().Wait();
            worker.LoadAsync().Wait();
            worker.MeetAsync().Wait();
            foreach (var ready in worker.Ready.ToList()) worker.Run(ready);
            worker.FlushAsync().Wait();
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
