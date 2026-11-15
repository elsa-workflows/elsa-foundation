using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

long now = 100;
var store = new InMemoryStore(1, () => now);
var h = new Harness(store);
await h.Activate();
var first = new KeyRequest("order", "start-a", Kinds.Start, Start: new("probe", "parent"), KeepForMillis: 100);
var accepted = await store.AcceptAsync(first);
Harness.Check(accepted is { Outcome: "accepted", RunId: "g0/key/1", KeepUntil: 200 }, "first binding");
// Ordinary ingress cannot preclaim the authority's physical target, even with its returned creator scope.
var counterfeit = new Message(first.WireId, new(accepted.RunId!), Kinds.Start,
    Start: new("probe", "counterfeit", Creation: new(1, 0, "attacker")), TargetCreation: 0).WithInboxScope(long.MaxValue);
Harness.Check(await store.AddAsync(counterfeit), "ordinary and creator wire origins must coexist");
await h.Pump();
Harness.Check((await h.Row(accepted.RunId!)) is { Definition: "parent", Creation: 0, Retired: false }, "supplied start preclaimed binding");
var oldChild = (await h.State(accepted.RunId!)).Child;
Harness.Check(oldChild is not null && (await h.Row(oldChild)) is { Creation: 0, Status: InstanceStatus.Finished }, "physical child");
var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => await store.AcceptAsync(first))));
Harness.Check(replays.All(r => r == accepted), "lost acknowledgement / parallel receipt replay");
Harness.Check((await store.AcceptAsync(first with { Payload = "changed" })).Outcome == "conflict", "changed immutable request");
var delayed = new KeyRequest("order", "before-delete", "signal", Json.Write("old"), Due: 1000);
Harness.Check((await store.AcceptAsync(delayed)).RunId == accepted.RunId, "old command acceptance");
await h.Give(accepted.RunId!, "finish");
await h.Give(accepted.RunId!, Kinds.Delete);
Harness.Check((await h.Row(accepted.RunId!)) is { Creation: 0, Retired: true }, "retirement");
now = 199;
var next = first with { Operation = "start-b" };
Harness.Check((await store.AcceptAsync(next)).Outcome == "in-use", "before reuse boundary");
Harness.Check((await store.AcceptAsync(new("order", "after-delete", "signal"))).Outcome == "missing", "new command to retired run");
now = 200;
var races = await Task.WhenAll(Enumerable.Range(0,8).Select(i => Task.Run(async () => await store.AcceptAsync(next with { Operation = "race-" + i }))));
Harness.Check(races.Count(r => r.Outcome == "accepted") == 1 && races.Count(r => r.Outcome == "in-use") == 7, "one concurrent start wins");
var fresh = races.Single(r => r.Outcome == "accepted");
Harness.Check(fresh.RunId != accepted.RunId && await store.AcceptAsync(first) == accepted, "receipt remained pinned after rebinding");
Harness.Check((await store.AcceptAsync(delayed)).RunId == accepted.RunId, "delayed receipt retargeted");
var current = new KeyRequest("order", "after-rebind", "signal", Json.Write("new"));
Harness.Check((await store.AcceptAsync(current)).RunId == fresh.RunId, "different operation selects current run");
Harness.Check(store.Messages.Values.Any(m => m.Message.Id == delayed.WireId && m.Message.Target.Id == accepted.RunId), "durable queue lost old pinned target");
await h.ReplaceOwner();
now = 1000;
await h.Pump();
Harness.Check((await h.State(fresh.RunId!)).Signal == "new", "delayed command reached new run");
Harness.Check((await h.State(fresh.RunId!)).Child != oldChild, "new generation reused child address");
Harness.Check((await h.Row(accepted.RunId!)) is { Retired: true }, "old run revived");
Harness.Check(!store.Messages.Values.Any(m => m.Message.Id == delayed.WireId), "delayed command not drained by owner");
Harness.Check((await store.AcceptAsync(next with { Operation = "still-present" })).Outcome == "in-use", "finished/live present run displaced");
Console.WriteLine("PASS memory: protected pending creator, physical children, concurrent replay/conflict and competing starts, no-sweep boundary, pinned queued old command, retained receipt, distinct new command, fresh-owner delivery.");

await h.DisposeAsync();
await KeyedEphemeral.Run("memory", store, store, target => { now=target; return Task.CompletedTask; });
await AdverseChecks.Run("memory", store, store);
await KeyedMetadataChecks.Run("memory",store,store,target=>{now=target;return Task.CompletedTask;});
await UnicodeKeys.Run("memory",store,store,waitUntil:target=>{now=Math.Max(now,target);return Task.CompletedTask;});
if (Environment.GetEnvironmentVariable("KERNEL_RECREATE_PG") is { } pgConnection) await PgWitness.Run(pgConnection);

internal sealed class Harness(InMemoryStore store) : IAsyncDisposable
{
    private Worker _worker = Owner(store);
    private static Worker Owner(IKernelStore store) => new(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([new Probe()]));
    public async Task Activate() { await new DurableHost([_worker]).ActivateAsync(0); await _worker.PollAsync(); }
    public async Task ReplaceOwner() { await _worker.DisposeAsync(); _worker = Owner(store); await Activate(); }
    public async Task Pump() { await _worker.WokenAsync(null); for (int i=0;i<24;i++) await _worker.RunAsync(); }
    public async Task<InstanceRow?> Row(string id) => (await store.LoadAsync([id])).SingleOrDefault();
    public async Task<ProbeState> State(string id) => Json.Read<ProbeState>((await Row(id))!.State);
    public async Task Give(string id,string kind) { Check(await store.AddAsync(new(Guid.NewGuid().ToString("N"),new(id),kind)),"enqueue"); await Pump(); }
    public ValueTask DisposeAsync() => _worker.DisposeAsync();
    public static void Check(bool ok,string reason) { if (!ok) throw new InvalidOperationException(reason); }
}
internal sealed class ProbeState { public string? Child {get;set;} public string? Signal {get;set;} }
internal sealed class Probe : IExecutionModel
{
    public string Key => "probe"; public Type StateType => typeof(ProbeState); public int StateVersion => 1;
    public object CreateState(StartSpec spec) => new ProbeState();
    public object UpgradeState(string state,int version) => throw new NotSupportedException();
    public ValueTask<object?> ExecuteJobAsync(IJob job,CancellationToken ct) => throw new NotSupportedException();
    public ValueTask TurnAsync(ITurn turn,CancellationToken ct)
    {
        var state = (ProbeState)turn.State;
        foreach (var m in turn.Inbox) {
            if (m.Kind == Kinds.Start && turn.Instance.Definition == "parent") state.Child = turn.Start(new(Key,"child")).Id;
            if (m.Kind == Kinds.Start && turn.Instance.Definition == "child" || m.Kind == "finish") { turn.Record("finished"); turn.SetStatus(InstanceStatus.Finished); }
            if (m.Kind == "signal") state.Signal=m.Read<string>();
        }
        return ValueTask.CompletedTask;
    }
}
