using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static class ShortWaitIntegration
{
    private sealed class Transport(InMemoryStore store) : IReplyTransport
    {
        public readonly List<(Reply Reply, bool Stored)> Seen = [];
        public void Send(string node, Reply reply) => Seen.Add((reply, store.Instances.ContainsKey("0/reply")));
    }
    private sealed class Probe : Kernel.Sim.Model<List<string>>
    {
        public override string Key => "short-wait-integration";
        public readonly JsonObject Entry = new() { ["value"] = "before" };
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start)
                {
                    state.Add("first"); turn.Record(Entry);
                    if (turn.Instance.Definition == "reply-start")
                    { turn.Reply(message, Request.Response, "ok"); turn.SetStatus(InstanceStatus.Finished); }
                    else turn.Send(turn.Instance.Ref, "later", options: new SendOptions(Delay: TimeSpan.FromMilliseconds(50)));
                }
                else if (message.Kind == "later") { state.Add("later"); turn.SetStatus(InstanceStatus.Finished); }
                else if (message.Kind == "reply") { state.Add("reply"); turn.Reply(message, Request.Response, "ok"); }
            }
        }
    }
    public static async Task<int> Run()
    {
        var failures = 0;
        async Task Check(string name, Func<Task> test)
        { try { await test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        static Message Start(string id, History history = History.Full, string definition = "wait", string? reply = null, TimeSpan? shortWait = null) =>
            new("start-" + id, InstanceRef.In(0, id), Kinds.Start, Start: new("short-wait-integration", definition, Ephemeral: true, History: history, ShortWait: shortWait), ReplyTo: reply);
        await Check("release drains every bounded batch", async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var model = new Probe();
            var worker = new Worker("short-release", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => now, MaxBatch = 1, ShortWait = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            worker.Post(Start("a"), fresh: true); worker.Post(Start("b"), fresh: true); await worker.RunAsync();
            Require(worker.Unwritten.Count() == 2, "two unwritten runs not prepared");
            await worker.DisposeAsync();
            Require(store.Instances.ContainsKey("0/a") && store.Instances.ContainsKey("0/b"), "stand-down lost an unwritten run");
            Require(store.Messages.Values.Count(m => m.Message.Kind == "later") == 2, "promoted timers not retained");
        });
        await Check("deadline writes before due local turn", async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var model = new Probe();
            await using var worker = new Worker("short-due", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => now, ShortWait = TimeSpan.FromMilliseconds(50) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); worker.Post(Start("due"), fresh: true); await worker.RunAsync();
            now = 150; await worker.RunAsync();
            Require(store.Instances.ContainsKey("0/due"), "due turn completed without the required checkpoint");
        });
        foreach (var terminal in new[] { false, true }) await Check("addressed reply commits first " + (terminal ? "terminal" : "running"), async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var transport = new Transport(store); var model = new Probe();
            await using var worker = new Worker("short-reply", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => now, Transport = transport, ShortWait = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            var target = InstanceRef.In(0, "reply");
            if (terminal) worker.Post(Start("reply", definition: "reply-start", reply: ReplyTo.Of("caller", "req", 1000)), fresh: true);
            else { worker.Post(Start("reply"), fresh: true); await worker.RunAsync(); worker.Post(new Message("reply-command", target, "reply", ReplyTo: ReplyTo.Of("caller", "req", 1000))); }
            worker.RunTurn(target.Id);
            Require(transport.Seen.Count == 0, "reply escaped before fenced flush"); await worker.FlushAsync();
            Require(transport.Seen.Count == 1 && transport.Seen[0].Stored && store.Instances.ContainsKey(target.Id), "reply did not follow its checkpoint");
        });
        await Check("queued start honors absolute caller deadline", async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var model = new Probe();
            await using var worker = new Worker("short-cap", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => now, ShortWait = TimeSpan.FromMilliseconds(200) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            worker.Post(Start("cap", reply: ReplyTo.Of("caller", "req", 200), shortWait: TimeSpan.FromMilliseconds(100)), fresh: true);
            now = 175; await worker.RunAsync(); now = 200; await worker.FlushAsync();
            Require(store.Instances.ContainsKey("0/cap"), "unwritten run survived the caller deadline");
        });
        await Check("Full history snapshots mutable entry", async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var model = new Probe();
            var worker = new Worker("short-history", 0, store, new WorkflowInstance([model]), new WorkerOptions { Monotonic = () => now, ShortWait = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync(); worker.Post(Start("history"), fresh: true); await worker.RunAsync();
            model.Entry["value"] = "after"; await worker.DisposeAsync();
            Require(store.Journal.Values.Single(j => j.Instance == "0/history").Entries.Contains("before"), "pending history retained a mutable reference");
        });
        foreach (var history in new[] { History.State, History.Minimal }) await Check(history + " retains no pending entries", async () =>
        {
            long now = 100; var store = new InMemoryStore(1, () => now); var model = new Probe();
            await using var worker = new Worker("short-history-" + history, 0, store, new WorkflowInstance([model]),
                new WorkerOptions { Monotonic = () => now, ShortWait = TimeSpan.FromMilliseconds(100) });
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            worker.Post(Start("no-history", history), fresh: true); await worker.RunAsync();
            var slots = (System.Collections.IDictionary)typeof(Worker).GetField("_slots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(worker)!;
            var slot = slots["0/no-history"]!;
            var pending = (System.Collections.ICollection)slot.GetType().GetField("Pending")!.GetValue(slot)!;
            Require(pending.Count == 0, "unwritten run retained entries outside Full history");
        });
        Console.WriteLine($"short-wait integration: {failures} failures"); return failures == 0 ? 0 : 1;
    }
}
