using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Bpmn.Model.State;

namespace Kernel.Bpmn.Run;

/// <summary>Every case of <see cref="BpmnCases"/> on the in-memory store, one owner (BPMN.md, section 3).</summary>
public static class CaseSmoke
{
    private const int Deadline = 1000;      // the boundary timers here: a job that should win does not lose to them on a loaded machine
    private static int _failures;
    private static int _n;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failures++;
    }

    /// <summary>
    /// Jobs of these services wait until released, and ignore a cancel, as work that cannot be stopped does: a cancelled
    /// one still runs and returns a result once released. Every other job runs at once.
    /// </summary>
    private sealed class Gates
    {
        public readonly ConcurrentDictionary<string, TaskCompletionSource> Held = new(StringComparer.Ordinal);
        public readonly HashSet<string> Services = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, bool> Cancelled = new(StringComparer.Ordinal);    // the service's job was told to stop
        public readonly ConcurrentDictionary<string, bool> Ran = new(StringComparer.Ordinal);          // and ran to its result anyway

        private TaskCompletionSource Of(string service) => Held.GetOrAdd(service, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async ValueTask Gate(IJob job, CancellationToken cancel)
        {
            var service = Json.Read<JobArgs>(job.Arguments!).Service;
            if (!Services.Contains(service)) return;
            using var _ = cancel.Register(() => Cancelled[service] = true);
            await Of(service).Task;
            if (Cancelled.ContainsKey(service)) Ran[service] = true;
        }

        public void Release(string service) => Of(service).TrySetResult();

        public void Clear()
        {
            Services.Clear();
            Held.Clear();
            Cancelled.Clear();
            Ran.Clear();
        }
    }

    public static async Task<int> RunAsync()
    {
        var gates = new Gates();
        var store = new InMemoryStore(1, () => Environment.TickCount64);
        var owner = new Owner("n1", 0, store, BpmnCases.Registry(Deadline), options: new BpmnOptions { Gate = gates.Gate });
        await owner.StartAsync();

        async Task<(BpmnState State, List<string> Journal, InstanceRef Id)> Run(string process, JsonObject? variables = null, Func<InstanceRef, Task>? meanwhile = null)
        {
            var id = InstanceRef.In(0, $"{process}-{++_n}");
            owner.Worker.Post(new Message($"m{_n}", id, Kinds.Start, Start: BpmnModel.Spec(process, variables)), fresh: true);
            if (meanwhile is not null) await meanwhile(id);
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted && store.Messages.Values.All(m => m.Done));
            var journal = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).SelectMany(j => Json.Read<List<string>>(j.Entries)).ToList();
            var state = owner.State(id.Id)!;
            Check(owner.Status(id.Id) == InstanceStatus.Finished, $"{id.Id} finished ({owner.Status(id.Id)}); journal: {string.Join(" ", journal)}");
            Check(state.Live.Count == 0 && state.Tokens.Tokens.All(t => t.Status == BpmnTokenStatus.Consumed && t.StoppedHandle is null), "no live work, no live or cancelled token left");
            return (state, journal, id);
        }
        static string? Var(BpmnState s, string name) => s.Variables.GetValueOrDefault(name)?.ToJsonString();
        static List<string> Joins(List<string> journal, string element) => journal.Where(e => e.StartsWith($"j {element} ", StringComparison.Ordinal)).ToList();

        Console.WriteLine("parallel: a fork into two service tasks (jobs) and a join");
        {
            var (s, journal, _) = await Run("b-parallel");
            Check(Var(s, "a") == "\"A\"" && Var(s, "b") == "\"B\"", "both jobs' results are variables");
            Check(journal.Count(e => e.StartsWith("w ", StringComparison.Ordinal) && e.Contains(" job ", StringComparison.Ordinal)) == 2, "two jobs started");
            Check(Joins(journal, "join") is [var j] && j.Split(' ')[3].Split(',').Length == 2, $"the join fired once with two arrivals: {string.Join(" ", Joins(journal, "join"))}");
        }

        Console.WriteLine("exclusive: the first flow that holds (amount > 100, then amount > 50), else the default");
        foreach (var (amount, size) in new[] { (150, "big"), (70, "medium"), (50, "small") })
        {
            var (s, _, _) = await Run("b-exclusive", new JsonObject { ["amount"] = amount });
            Check(Var(s, "size") == $"\"{size}\"", $"amount {amount}: size {Var(s, "size")}");
        }

        Console.WriteLine("inclusive: every flow that holds, else the default; the join waits for what can still arrive");
        foreach (var (express, insured, done) in new[] { (true, false, "express"), (false, true, "insured"), (true, true, "express insured"), (false, false, "standard") })
        {
            var (s, journal, _) = await Run("b-inclusive", new JsonObject { ["express"] = express, ["insured"] = insured });
            var ran = string.Join(" ", new[] { "express", "insured", "standard" }.Where(n => Var(s, n + "-done") == "true"));
            Check(ran == done, $"express={express} insured={insured}: ran {ran}");
            Check(Joins(journal, "or-join") is [var j] && j.Split(' ')[3].Split(',').Length == done.Split(' ').Length, $"the inclusive join fired once: {string.Join(" ", Joins(journal, "or-join"))}");
        }

        Console.WriteLine("an interrupting boundary timer cancels the service task's job");
        {
            gates.Services.Add("slow");
            var (s, journal, id) = await Run("b-boundary");
            Check(Var(s, "escalated") == "true" && Var(s, "slow-done") is null, "the timer fired first: the timeout path ran, the task did not complete");
            Check(journal.Any(e => e == "x h1 slow"), "the job was stopped");
            Check(gates.Cancelled.ContainsKey("slow"), "the job was told to stop");
            var commits = store.Journal.Count;
            gates.Release("slow");      // the job ignores the cancel: it runs and returns its result now
            await owner.PumpAsync(() => gates.Ran.ContainsKey("slow") && store.Messages.Values.All(m => m.Done));
            // The gate knows when the job resumed, not when its result reached the worker: give it time, then run passes again.
            await Task.Delay(200);
            await owner.PumpAsync(() => true);
            Check(owner.State(id.Id)!.Variables.GetValueOrDefault("slow-done") is null && owner.Status(id.Id) == InstanceStatus.Finished && store.Journal.Count == commits,
                "the job's late result changed nothing and made no commit");
            gates.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            (s, journal, _) = await Run("b-boundary");
            // Run waits for every message: a timer that was not recalled would hold it until it fires, at the deadline.
            Check(Var(s, "slow-done") == "true" && Var(s, "escalated") is null && clock.ElapsedMilliseconds < Deadline,
                $"the job finished first: the timer was recalled (done in {clock.ElapsedMilliseconds} ms, the timer is {Deadline} ms)");
            Check(journal.Any(e => e == "x h2 deadline"), "the timer was stopped");
        }

        Console.WriteLine("a message published before its catch subscribes is kept and still counts");
        {
            var order = $"o{++_n}";
            await Events.PublishAsync(store, new EventRow(BpmnModel.MessageKey("paid", order), $"paid-{order}", "{\"paid\":true}", Environment.TickCount64 + 60_000));
            var (s, journal, _) = await Run("b-early", new JsonObject { ["order"] = order });
            Check(Var(s, "paid") == "true" && Var(s, "prepared") == $"\"{order}\"", "published before the instance existed, the catch still got it (a kept event)");
            Check(store.Subscriptions.Count == 0, "no subscription left");
        }

        Console.WriteLine("a call activity is a child start");
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var (s, journal, id) = await Run("b-call", new JsonObject { ["order"] = "c1" });
            Check(journal.Contains("x h2 call-deadline") && clock.ElapsedMilliseconds < Deadline, $"the child finished first: the call's timer was recalled (done in {clock.ElapsedMilliseconds} ms)");
            var child = journal.First(e => e.StartsWith("r ", StringComparison.Ordinal)).Split(' ')[2];
            Check(Var(s, "child-out") == "\"child of c1\"", $"the child's variables came back: {Var(s, "child-out")}");
            Check(store.Instances[child] is { Status: InstanceStatus.Finished, Definition: "b-child" } c && c.Parent == id.Id, $"child {child} finished, its parent is {id.Id}");
        }
        Console.WriteLine("a boundary timer on the call activity terminates the child");
        {
            gates.Services.Add("child");
            var (s, journal, id) = await Run("b-call", new JsonObject { ["order"] = "c2" });
            var child = journal.First(e => e.StartsWith("w ", StringComparison.Ordinal) && e.Contains(" call ", StringComparison.Ordinal)).Split(' ')[4];
            await owner.PumpAsync(() => owner.Status(child) == InstanceStatus.Cancelled);
            Check(Var(s, "child-out") is null && journal.Contains("x h1 call"), "the timer fired first and stopped the call");
            Check(store.Instances[child].Status == InstanceStatus.Cancelled && gates.Cancelled.ContainsKey("child"), $"child {child} was terminated, and its job was told to stop");
            gates.Release("child");     // its work ends; its result is nobody's
            await owner.PumpAsync(() => gates.Ran.ContainsKey("child") && store.Messages.Values.All(m => m.Done));
            gates.Clear();
        }

        Console.WriteLine("a loop whose body forks and joins");
        {
            var (s, journal, _) = await Run("b-loop");
            Check(Var(s, "count") == $"{BpmnCases.Rounds}" && Var(s, "notes") == $"{BpmnCases.Rounds}", $"count {Var(s, "count")}, notes {Var(s, "notes")}");
            var joins = Joins(journal, "join");
            var arrivals = joins.SelectMany(j => j.Split(' ')[3].Split(',')).ToList();
            Check(joins.Count == BpmnCases.Rounds && arrivals.Count == 2 * BpmnCases.Rounds && arrivals.Distinct().Count() == arrivals.Count, $"the join fired once per round, each arrival once: {string.Join(" ", joins)}");
        }

        Console.WriteLine("known: an inclusive join in a loop beside a parallel branch (Bpmn.Semantics 0.5.0, BPMN.md section 7)");
        {
            var id = InstanceRef.In(0, $"b-inclusive-loop-{++_n}");
            owner.Worker.Post(new Message($"m{_n}", id, Kinds.Start, Start: BpmnModel.Spec("b-inclusive-loop")), fresh: true);
            await owner.PumpAsync(() => owner.Status(id.Id) is InstanceStatus.Finished or InstanceStatus.Faulted && store.Messages.Values.All(m => m.Done));
            var journal = store.Journal.Values.Where(j => j.Instance == id.Id).OrderBy(j => j.Position).SelectMany(j => Json.Read<List<string>>(j.Entries)).ToList();
            Check(owner.Status(id.Id) == InstanceStatus.Faulted && journal[^1] == "fault bpmn.join.deadlock",
                $"still faults with bpmn.join.deadlock (an upgrade that fires the join should update BPMN.md): {owner.Status(id.Id)}, {journal[^1]}");
        }

        Check(store.Messages.Values.All(m => m.Done) && store.Subscriptions.Count == 0, "no message and no subscription left");
        Console.WriteLine(_failures == 0 ? "cases: all passed" : $"cases: {_failures} FAILED");
        return _failures;
    }
}
