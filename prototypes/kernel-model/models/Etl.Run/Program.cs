using System.Diagnostics;
using Kernel.Durable;
using Kernel.Etl;
using Kernel.Etl.Run;
using Kernel.Runtime;
using Kernel.Workflows;

if (args.Contains("--validate-inputs")) return ValidateInputs();
int size = args.Contains("--size") ? int.Parse(args[Array.IndexOf(args, "--size") + 1]) : 256;
bool duplicate = args.Contains("--mutate-count"), excess = args.Contains("--mutate-credit");
if (size < 65) { Console.Error.WriteLine("The tree comparison requires at least 65 leaves."); return 2; }
var tree = await Run(size, direct: false, fault: true, duplicate, excess);
var direct = await Run(size, direct: true, fault: false, duplicate, excess);
var expected = (long)size * (size + 1) / 2;
bool ok = true;
void Check(bool condition, string name) { Console.WriteLine($"{(condition ? "ok" : "FAIL")} {name}"); ok &= condition; }
foreach (var result in new[] { tree, direct })
{
    Check(result.Count == size && result.Sum == expected, $"{result.Name} selected correct count and sum once");
    Check(result.MaxPending <= 8, $"{result.Name} local credit <= 8 (observed {result.MaxPending})");
    Check(result.Partitions > 1 && result.ReferencesIntact, $"{result.Name} spread children across {result.Partitions} partitions and retained payload references");
}
Check(tree.Injected && tree.Recovered, "landed unknown outcome followed by fresh-owner recovery of the exact checkpoint");
Check(tree.RootMessages < direct.RootMessages && tree.RootTurns < direct.RootTurns, "tree reduces root messages and turns versus direct fan-in");
Console.WriteLine($"tree: leaves={size} instances={tree.Instances} root messages={tree.RootMessages} root turns={tree.RootTurns} seconds={tree.Seconds:F2}");
Console.WriteLine($"direct: leaves={size} instances={direct.Instances} root messages={direct.RootMessages} root turns={direct.RootTurns} seconds={direct.Seconds:F2}");
return ok ? 0 : 1;

static async Task<RunResult> Run(int size, bool direct, bool fault, bool duplicate, bool excess)
{
    const int partitions = 4;
    long time = 100;
    var store = new InMemoryStore(partitions, () => time);
    var wire = new FaultStore(store, fault);
    int rootTurns = 0, rootMessages = 0;
    bool replaced = false, recovered = false;
    Worker Make(int partition, string node)
    {
        Worker? owner = null;
        owner = new Worker(node, partition, wire, new WorkflowInstance([new EtlModel(duplicate, excess)]), new WorkerOptions
    {
        Partitions = partitions, Monotonic = () => time, PollLimit = 500, MaxBatch = 500,
        OnTurn = (actor, inbox, _) =>
        {
            if (actor.Id != "0/root") return;
            rootTurns++;
            rootMessages += inbox.Count(m => m.Kind == WorkflowKinds.ChildFinished);
            if (replaced && !recovered)
            {
                var slot = owner!.Instances.First(i => i.Id == actor.Id);
                var loaded = slot.State ?? slot.Row?.State;
                recovered = loaded is not null && Json.Write(Json.Read<EtlState>(loaded)) == Json.Write(wire.LostCheckpoint);
            }
        }
    });
        return owner;
    }
    async Task<Worker[]> Owners(string node)
    {
        var workers = Enumerable.Range(0, partitions).Select(i => Make(i, node)).ToArray();
        var host = new DurableHost(workers);
        foreach (var worker in workers) { await host.ActivateAsync(worker.Partition); await worker.PollAsync(); }
        return workers;
    }
    var stopwatch = Stopwatch.StartNew();
    var workers = await Owners("first");
    var spec = new StartSpec(EtlModel.ModelKey, "range", Json.Write(new RangeInput(0, size, "blob://dataset/1", 64, 8, direct)), History: History.Minimal);
    workers[0].Post(new Message("start", InstanceRef.In(0, "root"), Kinds.Start, Start: spec), fresh: true);
    for (int pass = 0; pass < 100000; pass++)
    {
        foreach (var worker in workers)
        {
            await worker.WokenAsync(null);
            await worker.RunAsync();
            if (wire.Injected && !replaced) break;
        }
        if (wire.Injected && !replaced)
        {
            var before = wire.LostCheckpoint!;
            var persisted = Json.Read<EtlState>(store.Instances["0/root"].State);
            if (Json.Write(before) != Json.Write(persisted) || persisted.Selected == 0 || persisted.Cursor == 0)
                throw new InvalidOperationException("landed checkpoint was not preserved");
            time += 11_000; // no graceful release: all old owners have lost their leases
            workers = await Owners("replacement");
            replaced = true;
            continue;
        }
        if (store.Instances.TryGetValue("0/root", out var row) && row.Status == InstanceStatus.Finished) break;
        if (pass == 99999) throw new TimeoutException("ETL did not complete");
    }
    stopwatch.Stop();
    var state = Json.Read<EtlState>(store.Instances["0/root"].State);
    var states = store.Instances.Values.Select(r => Json.Read<EtlState>(r.State)).ToList();
    var maxPending = states.Max(s => s.MaxPending);
    var partitionsUsed = store.Instances.Keys.Select(id => new InstanceRef(id).Partition).Distinct().Count();
    return new(direct ? "direct" : "tree", state.Count, state.Sum, maxPending, rootTurns, rootMessages, store.Instances.Count,
        partitionsUsed, states.All(s => s.Input.PayloadRef == "blob://dataset/1"), wire.Injected, recovered, stopwatch.Elapsed.TotalSeconds);
}

static int ValidateInputs()
{
    var model = new EtlModel();
    bool rejected = true;
    foreach (var input in new[] { new RangeInput(0, 1, "ref", 1, 8), new RangeInput(0, 1, "ref", 2, 0),
                 new RangeInput(-1, 1, "ref", 2, 1), new RangeInput(0, 0, "ref", 2, 1),
                 new RangeInput(int.MaxValue, 1, "ref", 2, 1) })
    {
        try { model.CreateState(new StartSpec(EtlModel.ModelKey, "range", Json.Write(input))); rejected = false; }
        catch (ArgumentOutOfRangeException) { }
    }
    var valid = (EtlState)model.CreateState(new StartSpec(EtlModel.ModelKey, "range", Json.Write(new RangeInput(0, int.MaxValue, "ref", int.MaxValue, 1))));
    bool ok = rejected && valid.Input.Count == int.MaxValue && EtlModel.SpanFor(int.MaxValue, 2) == 1_073_741_824;
    Console.WriteLine(ok ? "invalid inputs rejected; maximum legal range and span accepted" : "FAIL input validation");
    return ok ? 0 : 1;
}

record RunResult(string Name, long Count, long Sum, int MaxPending, int RootTurns, int RootMessages, int Instances, int Partitions, bool ReferencesIntact, bool Injected, bool Recovered, double Seconds);
