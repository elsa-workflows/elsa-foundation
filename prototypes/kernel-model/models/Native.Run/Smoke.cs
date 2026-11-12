using System.Text.Json.Nodes;
using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>Every ported activity once, in every mode, and the five HTTP cases, on the in-memory store.</summary>
public static class Smoke
{
    public const string Expected = "one 6 3 1 2 2 child done answer of quotes";

    public static IEnumerable<Workflow> Workflows()
    {
        yield return Workflow("smoke-all", Sequence("root",
                Set("s1", "x", 1),
                WriteLine("w1", Expr("'x is ' + x")),
                If("if1", Expr("x == 1"), Set("then", "y", "one"), Set("else", "y", "other")),
                ForEach("each", Expr("[1, 2, 3]"), Set("add", "sum", Expr("sum + CurrentItem"))),
                While("loop", Expr("i < 3"), Set("inc", "i", Expr("i + 1"))),
                Parallel("par", null,
                    Sequence("b1", Delay("d1", 100), Set("a", "a", 1)),
                    Sequence("b2", Event("e1", Expr("'go-' + input.key")), Set("b", "b", 2))),
                Flowchart("flow",
                    [WriteLine("f1", "start"), Set("f2", "c", 1), Set("f3", "d", 1), Set("f4", "e", Expr("c + d"))],
                    [new("f1", "f2"), new("f1", "f3"), new("f2", "f4"), new("f3", "f4")], ("f4", JoinMode.All)),
                Call("svc", "quotes").Capture("quote"),
                Notify("notify", "hello"),
                Dispatch("child", "smoke-child", wait: true).Capture("childOut", "Output"),
                Dispatch("fire", "smoke-child", wait: false),
                Publish("ping", Expr("'ping-' + input.key"), "hi"),
                Set("out", "output", Expr("y + ' ' + sum + ' ' + i + ' ' + a + ' ' + b + ' ' + e + ' ' + childOut + ' ' + quote"))),
            ("x", null), ("y", null), ("sum", 0), ("i", 0), ("a", null), ("b", null), ("c", null), ("d", null), ("e", null), ("quote", null), ("childOut", null), ("output", null));
        yield return Workflow("smoke-child", Sequence("root", Delay("d", 50), Set("o", "output", "child done")));
        yield return Workflow("smoke-listener", Sequence("root", Event("hear", Expr("'ping-' + input.key")), Set("o", "output", "heard")));
        yield return Workflow("smoke-wait", Sequence("root", Event("wait", "never"), Set("o", "output", "late")));
        yield return Workflow("http-early", Sequence("root", HttpEndpoint("http-in", "early", respondBeforeCommit: true), Respond("answer", "early"), Event("wait", "never")));
    }

    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failures++;
    }

    public static async Task<int> RunAsync()
    {
        var registry = Reference.Registry();
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            Console.WriteLine($"every activity, {mode}");
            var store = new InMemoryStore(1, () => Environment.TickCount64);
            var owner = new Owner("n1", 0, store, registry, new Network());
            await owner.StartAsync();
            var key = new JsonObject { ["key"] = "k-" + mode };
            owner.Worker.Post(new Message("m1", InstanceRef.In(0, "listener"), Kinds.Start, Start: NativeModel.Spec("smoke-listener", mode, key)), fresh: true);
            owner.Worker.Post(new Message("m2", InstanceRef.In(0, "all"), Kinds.Start, Start: NativeModel.Spec("smoke-all", mode, key.DeepClone().AsObject())), fresh: true);
            await owner.PumpAsync();
            await Events.PublishAsync(store, new EventRow("event:go-k-" + mode, "e-go", "null", Environment.TickCount64 + 60_000));
            await owner.PumpAsync(poll: true, until: () => owner.Status("0/all") == InstanceStatus.Finished && owner.Status("0/listener") == InstanceStatus.Finished && store.Instances.Values.Count(i => i.Definition == "smoke-child" && i.Status == InstanceStatus.Finished) == 2 && store.Messages.Values.All(m => m.Done), keepPolling: true);
            var state = owner.State("0/all")!;
            Check(state.Variables["output"]?.GetValue<string>() == Expected, $"output is '{state.Variables["output"]}'");
            Check(owner.State("0/listener")!.Variables["output"]?.GetValue<string>() == "heard", "the published event resumed the listener");
            Check(store.Instances.Values.Count(i => i.Definition == "smoke-child" && i.Status == InstanceStatus.Finished) == 2, "two children finished, one waited for, one detached");
            Check(store.Subscriptions.Count == 0 && store.Messages.Values.All(m => m.Done), "no subscription and no message left");
            Check(state.Frames.Count == 0 && state.Agenda.Count == 0 && state.Waits.Count == 0, "no frame, work or wait left in the state");
        }

        Console.WriteLine("cancel");
        {
            var store = new InMemoryStore(1, () => Environment.TickCount64);
            var owner = new Owner("n1", 0, store, registry, new Network());
            await owner.StartAsync();
            owner.Worker.Post(new Message("m1", InstanceRef.In(0, "w"), Kinds.Start, Start: NativeModel.Spec("smoke-wait")), fresh: true);
            await owner.PumpAsync();
            Check(store.Subscriptions.Count == 1, "the wait is a subscription");
            await store.AddAsync(new Message("c1", InstanceRef.In(0, "w"), NativeKinds.Cancel, KeepUntil: Environment.TickCount64 + 60_000));
            await owner.PumpAsync(poll: true, until: () => owner.Status("0/w") == InstanceStatus.Cancelled);
            Check(store.Subscriptions.Count == 0, "cancelled: the subscription is gone");
        }

        await HttpAsync(registry);
        Console.WriteLine(_failures == 0 ? "smoke: all passed" : $"smoke: {_failures} failed");
        return _failures;
    }

    private static HttpRequest Post(string path, object body) => new("POST", path, Values.From(body));

    private static async Task HttpAsync(WorkflowRegistry registry)
    {
        long Now() => Environment.TickCount64;

        Console.WriteLine("HTTP 1 and 5: a request starts the workflow; the response is released after the commit of its step");
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced })
        {
            var store = new InMemoryStore(1, Now);
            var network = new Network();
            var owner = new Owner("n1", 0, store, registry, network);
            await owner.StartAsync();
            var rowsAtRelease = -1;
            network.OnAnswer = _ => rowsAtRelease = store.Instances.Count;
            var response = await owner.Http.SendAsync(Post(Reference.HttpPath, new { firstName = "Ada", lastName = "Lovelace" }), owner.Worker, mode, () => owner.PumpAsync());
            Check(response is { StatusCode: 200 } && response.Body?.GetValue<string>() == "Ada Lovelace", $"{mode}: 200 '{response.Body}'");
            Check(mode == Modes.Ephemeral ? store.Instances.Count == 0 && rowsAtRelease == 0 : rowsAtRelease == 1,
                mode == Modes.Ephemeral ? "Ephemeral: nothing written; released at once" : "Balanced: released after the row was committed");
        }
        {
            var store = new InMemoryStore(1, Now);
            var network = new Network();
            var owner = new Owner("n1", 0, store, registry, network);
            await owner.StartAsync();
            var rowsAtRelease = -1;
            network.OnAnswer = _ => rowsAtRelease = store.Instances.Count;
            var response = await owner.Http.SendAsync(new HttpRequest("POST", "early"), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            Check(response.StatusCode == 200 && rowsAtRelease == 0, "opt-out: released before the commit of a step that commits");
        }

        Console.WriteLine("HTTP 2: respond, then continue");
        {
            var store = new InMemoryStore(1, Now);
            var network = new Network();
            var owner = new Owner("n1", 0, store, registry, network);
            await owner.StartAsync();
            string? id = null;
            network.OnAnswer = reply => id = reply.Key[..reply.Key.IndexOf('#')];
            var response = await owner.Http.SendAsync(Post("orders/respond-continue", new { id = 7 }), owner.Worker, Modes.Balanced, () => owner.PumpAsync(until: () => id is not null));
            Check(response.Body?.GetValue<string>() == "accepted 7", $"answered '{response.Body}'");
            await owner.PumpAsync(until: () => owner.Status(id!) == InstanceStatus.Finished);
            Check(owner.State(id!)!.Variables["output"]?.GetValue<string>() == "shipped 7", "then it ran on and finished");
        }

        Console.WriteLine("HTTP 3: a wait before the response");
        foreach (var path in new[] { "quotes", "slow" })
        {
            var store = new InMemoryStore(1, Now);
            var network = new Network();
            var owner = new Owner("n1", 0, store, registry, network);
            await owner.StartAsync();
            var released = false;
            network.OnAnswer = _ => released = true;
            var response = await owner.Http.SendAsync(new HttpRequest("POST", path), owner.Worker, Modes.Balanced, () => owner.PumpAsync(until: () => released));
            Check(response.StatusCode == 200, $"{(path == "quotes" ? "off-step work" : "a 20 ms Delay")} first; the request waited: '{response.Body}'");
        }

        Console.WriteLine("HTTP 4: a second request resumes the workflow waiting at an HttpEndpoint");
        {
            var store = new InMemoryStore(1, Now);
            var owner = new Owner("n1", 0, store, registry, new Network());
            await owner.StartAsync();
            var first = await owner.Http.SendAsync(Post("orders", new { id = 42 }), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            Check(first.Body?.GetValue<string>() == "waiting for approval of 42", $"one node, first: '{first.Body}'");
            var second = await owner.Http.SendAsync(Post("orders/42/approve", new { by = "sam" }), owner.Worker, Modes.Balanced, () => owner.PumpAsync(poll: true));
            Check(second.Body?.GetValue<string>() == "approved 42 by sam", $"one node, second: '{second.Body}'");
            var none = await owner.Http.SendAsync(Post("orders/42/approve", new { by = "sam" }), owner.Worker, Modes.Balanced, () => owner.PumpAsync(poll: true));
            Check(none.StatusCode == 404, "a third request finds nobody waiting: 404");
        }
        {
            var network = new Network();
            var store = new InMemoryStore(2, Now);
            var (a, b) = (new Owner("A", 0, store, registry, network), new Owner("B", 1, store, registry, network));
            await a.StartAsync();
            await b.StartAsync();
            var first = await a.Http.SendAsync(Post("orders", new { id = 43 }), a.Worker, Modes.Balanced, () => a.PumpAsync());
            var second = await b.Http.SendAsync(Post("orders/43/approve", new { by = "kim" }), b.Worker, Modes.Balanced, async () =>
            {
                await a.PumpAsync(poll: true);      // the owner, on node A, runs the step and sends the response to node B's address
                await b.PumpAsync();
            });
            Check(first.StatusCode == 200 && second.Body?.GetValue<string>() == "approved 43 by kim" && network.Hops == 1,
                $"two nodes: answered on B by the owner on A, {network.Hops} hop: '{second.Body}'");
        }
        {
            var store = new InMemoryStore(1, Now);
            var network = new Network();
            var owner = new Owner("n1", 0, store, registry, network);
            await owner.StartAsync();
            await owner.Http.SendAsync(Post("orders", new { id = 44 }), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            var lost = await owner.Http.SendAsync(Post("orders/44/approve", new { by = "lee" }), owner.Worker, Modes.Balanced, async () =>
            {
                await owner.Worker.PollAsync();
                await owner.Worker.TakeAsync();     // the owner resets before the step runs: what it held is lost
                await owner.PumpAsync(poll: true);
            });
            var finished = store.Instances.Values.Single().Status;
            Check(lost.StatusCode == 503 && finished == InstanceStatus.Finished && network.Sent == 2 && network.Answers == 1,
                $"owner lost while the request waits: {lost.StatusCode}; the workflow still finished; its late answer went nowhere");
        }
    }
}
