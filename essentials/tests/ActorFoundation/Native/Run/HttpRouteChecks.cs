using System.Text.Json.Nodes;

namespace Kernel.Native.Run;

/// <summary>Which published workflows a request on a route starts: the start routes of <see cref="HttpRoutes"/>.</summary>
internal static class HttpRouteChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("http routes");
        Table(check);
        Conflicts(check);
        await StartTriggersAsync(check);
        await FlowchartStartAsync(check);
    }

    private static Node NoStart(Node trigger) => Bind(trigger, false);
    private static Node Starts(Node trigger) => Bind(trigger, true);

    private static Node Bind(Node trigger, bool canStart)
    {
        trigger.Inputs[NativeStartTriggers.Input] = canStart;
        return trigger;
    }

    // Owner decision of 25 November: an HttpEndpoint or Event whose CanStartWorkflow is false only waits, also where a run begins.
    private static async Task StartTriggersAsync(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("endpoint-waits", Wf.Sequence("main", NoStart(Wf.HttpEndpoint("in", "waits")), Wf.Respond("out", "ok"))))
            .Add(Wf.Workflow("endpoint-starts", Wf.Sequence("main", Starts(Wf.HttpEndpoint("in", "starts")), Wf.Respond("out", "ok"))))
            .Add(Wf.Workflow("event-waits", Wf.Sequence("main", NoStart(Wf.Event("in", "paid")), Wf.Set("after", "output", "ran"))))
            .Add(Wf.Workflow("event-starts", Wf.Sequence("main", Wf.Event("in", "paid"), Wf.Set("after", "output", "ran"))));
        var routes = new HttpRoutes(registry);
        check(!routes.TryStart("POST", "waits", out _) && routes.Routes.Single(r => r.Workflow == "endpoint-waits") is { Start: false, Node: "in" } &&
              routes.TryStart("POST", "starts", out var starts) && starts == ("endpoint-starts", "in"),
            "a start-position HttpEndpoint with CanStartWorkflow false is a wait route, not a start route; true still starts");
        check(!NativeStartTriggers.Events(registry.Get("event-waits")).Any() &&
              NativeStartTriggers.Events(registry.Get("event-starts")).Select(n => n.Id).SequenceEqual(["in"]),
            "a start-position Event with CanStartWorkflow false is no event start; unbound, it is one");

        var owner = new Owner("starts", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
        await using var worker = owner.Worker;
        await owner.StartAsync();
        foreach (var id in new[] { "event-waits", "event-starts" })
            owner.Worker.Post(new Message($"{id}#start", InstanceRef.In(0, id), Kinds.Start,
                Start: NativeModel.Spec(id, Modes.Balanced, triggerNode: "in", trigger: JsonValue.Create("payload"))), fresh: true);
        await owner.PumpAsync(until: () => owner.Status("0/event-starts") is InstanceStatus.Finished);
        await owner.PumpAsync();
        check(owner.Status("0/event-waits") is InstanceStatus.Running && owner.State("0/event-waits")?.Variables.GetValueOrDefault("output") is null &&
              owner.Status("0/event-starts") is InstanceStatus.Finished && owner.State("0/event-starts")?.Variables["output"]?.GetValue<string>() == "ran",
            "an Event with CanStartWorkflow false takes no start delivered to it and waits for its event; one that may start runs on");
    }

    private static void Table(Action<bool, string> check)
    {
        var routes = new HttpRoutes(new WorkflowRegistry()
            .Add(Wf.Workflow("at-root", Wf.HttpEndpoint("in", "root")))
            .Add(Wf.Workflow("in-sequence", Wf.Sequence("main", Wf.HttpEndpoint("in", "sequence"), Wf.Respond("out", "ok"))))
            .Add(Wf.Workflow("flowchart", Wf.Flowchart("flow",
                [Wf.Respond("out", "ok"), Wf.HttpEndpoint("in", "flowchart", "GET")],
                [new Connection("in", "out")])))
            .Add(Wf.Workflow("flowchart-mid", Wf.Flowchart("flow",
                [Wf.WriteLine("first", "before"), Wf.HttpEndpoint("in", "flowchart-mid"), Wf.Respond("out", "ok")],
                [new Connection("first", "in"), new Connection("in", "out")])))
            .Add(Wf.Workflow("flowchart-loop", Wf.Flowchart("flow",
                [Wf.HttpEndpoint("in", "flowchart-loop"), Wf.Respond("out", "ok")],
                [new Connection("in", "out"), new Connection("out", "in")])))
            .Add(Wf.Workflow("flowchart-entries", Wf.Flowchart("flow",
                [Wf.WriteLine("stray", "left over"), Wf.HttpEndpoint("in-a", "entry-a", "GET"), Wf.HttpEndpoint("in-b", "entry-b"), Wf.Respond("out", "ok")],
                [new Connection("in-a", "out"), new Connection("in-b", "out")])))
            .Add(Wf.Workflow("flowchart-expression", Wf.Flowchart("flow",
                [Wf.HttpEndpoint("in", Binding.Expr("'computed'")), Wf.HttpEndpoint("literal", "expression-sibling"), Wf.Respond("out", "ok")],
                [new Connection("in", "out"), new Connection("literal", "out")])))
            .Add(Wf.Workflow("flowchart-broken", Wf.Flowchart("flow",
                [Wf.HttpEndpoint("in", "flowchart-broken")],
                [new Connection("in", "missing")]))));
        check(routes.TryStart("POST", "root", out var root) && root == ("at-root", "in") &&
              routes.TryStart("POST", "sequence", out var sequence) && sequence == ("in-sequence", "in"),
            "an HttpEndpoint at the root, or first in a root Sequence, still starts its workflow");
        check(routes.TryStart("GET", "/flowchart/", out var flowchart) && flowchart == ("flowchart", "in") && !routes.TryStart("POST", "flowchart", out _),
            "a root Flowchart whose start node is an HttpEndpoint starts on that endpoint's method and path, wherever the node is listed");
        check(!routes.TryStart("POST", "flowchart-mid", out _),
            "an HttpEndpoint behind the Flowchart's start node is a wait in the middle, not a start route");
        check(routes.TryStart("GET", "entry-a", out var a) && a == ("flowchart-entries", "in-a") &&
              routes.TryStart("POST", "entry-b", out var b) && b == ("flowchart-entries", "in-b"),
            "every HttpEndpoint of a root Flowchart that no connection leads to is a start route, also behind a stray node listed first");
        check(!routes.TryStart("POST", "flowchart-loop", out _),
            "an HttpEndpoint a loop leads back to is a wait, not a start route, even when the engine falls back to it as the start node");
        check(routes.TryStart("POST", "expression-sibling", out var sibling) && sibling == ("flowchart-expression", "literal") &&
              !routes.TryStart("POST", "computed", out _) && !routes.TryStart("POST", "'computed'", out _) && !routes.TryStart("POST", "flowchart-broken", out _),
            "a start endpoint needs a literal path, and a Flowchart the graph engine refuses registers no route");
    }

    private static void Conflicts(Action<bool, string> check)
    {
        static Workflow Starts(string id, string node, string path, string method = "POST") =>
            Wf.Workflow(id, Wf.Sequence("main", Wf.HttpEndpoint(node, path, method), Wf.Respond($"{node}-out", id)));

        var twice = Refused(new WorkflowRegistry().Add(Starts("orders", "in", "orders")).Add(Starts("orders-copy", "in", "orders")));
        check(twice is { Method: "POST", Path: "orders", First: ("orders", "in"), Second: ("orders-copy", "in") } &&
              twice.Message.Contains("'orders'", StringComparison.Ordinal) && twice.Message.Contains("'orders-copy'", StringComparison.Ordinal) &&
              twice.Message.Contains("POST /orders", StringComparison.Ordinal),
            $"two definitions that start on one method and path are refused, naming both ({twice?.Message})");

        var spelled = Refused(new WorkflowRegistry().Add(Wf.Workflow("a", Wf.Flowchart("flow",
            [Wf.Respond("out", "a"), Wf.HttpEndpoint("entry", "orders", "POST")], [new Connection("entry", "out")])))
            .Add(Starts("b", "in", " /Orders/ ", "post")));
        check(spelled is { Method: "POST", Path: "orders", First: ("a", "entry"), Second: ("b", "in") },
            "the method's case and the path's case, white space and slashes do not make two routes, and a Flowchart entry claims a route as a Sequence start does");
        var rooted = Refused(new WorkflowRegistry().Add(Wf.Workflow("empty", Wf.HttpEndpoint("in", ""))).Add(Wf.Workflow("slash", Wf.HttpEndpoint("in", "/"))));
        var cased = new HttpRoutes(new WorkflowRegistry().Add(Starts("cased", "in", "Orders/Export")));
        check(rooted is { Path: "", First: ("empty", "in"), Second: ("slash", "in") } && rooted.Message.Contains("POST /;", StringComparison.Ordinal) &&
              cased.TryStart("post", "/orders/export/", out var found) && found == ("cased", "in"),
            "an empty path and '/' are one route at the root, and a request finds a route whatever its case and slashes");

        var republished = new WorkflowRegistry().Add(Starts("orders", "in-v1", "orders")).Add(Starts("orders", "in-v2", "orders"));
        check(Refused(republished) is null && new HttpRoutes(republished).TryStart("POST", "orders", out var latest) && latest == ("orders", "in-v2"),
            "a definition published again replaces its own route");

        var methods = new WorkflowRegistry().Add(Starts("read", "in", "orders", "GET")).Add(Starts("write", "in", "orders", "POST"));
        check(Refused(methods) is null && new HttpRoutes(methods).TryStart("GET", "orders", out var read) && read.Workflow == "read" &&
              new HttpRoutes(methods).TryStart("POST", "orders", out var write) && write.Workflow == "write",
            "one path starts different definitions on different methods");

        var inOne = Refused(new WorkflowRegistry().Add(Wf.Workflow("both", Wf.Flowchart("flow",
            [Wf.HttpEndpoint("first", "same"), Wf.HttpEndpoint("second", "same"), Wf.Respond("out", "x")],
            [new Connection("first", "out"), new Connection("second", "out")]))));
        check(inOne is { First: ("both", "first"), Second: ("both", "second") } && inOne.Message.Contains("two endpoints", StringComparison.Ordinal),
            "one definition with two entries on one method and path is refused, naming both endpoints");

        var waitBeside = new WorkflowRegistry().Add(Starts("starter", "in", "approve"))
            .Add(Wf.Workflow("waiter", Wf.Sequence("main", Wf.HttpEndpoint("in", "begin"), Wf.HttpEndpoint("wait", "approve"))));
        check(Refused(waitBeside) is { Kind: HttpRouteConflict.StartAndWait, First: ("starter", "in"), Second: ("waiter", "wait") },
            "a wait in the middle of another workflow on a start route's path is refused, so that a request reaches one of them");
    }

    private static HttpRouteConflictException? Refused(WorkflowRegistry registry)
    {
        try { _ = new HttpRoutes(registry); return null; }
        catch (HttpRouteConflictException conflict) { return conflict; }
    }

    private static async Task FlowchartStartAsync(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry().Add(Wf.Workflow("designed", Wf.Flowchart("flow",
            [Wf.WriteLine("note", "a stray node listed first"), Wf.HttpEndpoint("in", "designed").Capture("order", "Body"),
                Wf.HttpEndpoint("lookup", "designed/lookup", "GET"), Wf.If("check", Binding.Expr("order.id > 0")),
                Wf.Respond("accepted", Binding.Expr("'accepted ' + order.id"), status: 201), Wf.Respond("refused", "no id", status: 400),
                Wf.Respond("found", Binding.Expr("'found ' + $lookup.Query.id"))],
            [new Connection("in", "check"), new Connection("check", "accepted", "True"), new Connection("check", "refused", "False"),
                new Connection("lookup", "found")]),
            ("order", null)));
        // A loop entered from an endpoint that is not the start node (a stray node is listed first): the engine classifies
        // its loop-closing edge from the entry, as it does when the entry is the start node, so the outcome cannot depend on
        // how the nodes are named. The engine refuses a loop back to a node with another inbound connection as ambiguous,
        // so here the same refusal is the right answer in every variant; misclassified, one naming of it ran.
        const string shape = "plain";
        foreach (var stray in new[] { false, true })
        foreach (var (head, tail) in new[] { ("a-head", "b-tail"), ("b-head", "a-tail") })
        {
            Node[] nodes = [Wf.HttpEndpoint("in", $"loop/{shape}/{stray}/{head}"), Wf.If(head, Binding.Expr("n < 3")),
                Wf.Set(tail, "n", Binding.Expr("n + 1")), Wf.Respond("out", Binding.Expr("'n=' + n"))];
            registry.Add(Wf.Workflow($"loop-{shape}-{stray}-{head}", Wf.Flowchart("flow",
                stray ? [Wf.WriteLine("stray", "listed first"), .. nodes] : nodes,
                [new Connection("in", head), new Connection(head, tail, "True"), new Connection(tail, head), new Connection(head, "out", "False")]),
                ("n", 0)));
        }
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var owner = new Owner("routes", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await using var worker = owner.Worker;
            await owner.StartAsync();
            var accepted = await owner.Http.SendAsync(new HttpRequest("POST", "designed", Wf.Object(("id", 7))), worker, mode, () => owner.PumpAsync());
            var refused = await owner.Http.SendAsync(new HttpRequest("POST", "designed", Wf.Object(("id", 0))), worker, mode, () => owner.PumpAsync());
            var found = await owner.Http.SendAsync(new HttpRequest("GET", "designed/lookup") { Query = new Dictionary<string, string> { ["id"] = "9" } },
                worker, mode, () => owner.PumpAsync());
            check(accepted is { StatusCode: 201 } && accepted.Body?.GetValue<string>() == "accepted 7" && refused is { StatusCode: 400 } &&
                  found is { StatusCode: 200 } && found.Body?.GetValue<string>() == "found 9",
                $"{mode}: each entry HttpEndpoint of a designer-shaped Flowchart starts it there, and the branch it takes answers ({accepted.StatusCode}, {refused.StatusCode}, {found.StatusCode})");
            var loops = new Dictionary<string, string>();
            foreach (var stray in new[] { false, true })
            foreach (var head in new[] { "a-head", "b-head" })
            {
                var answer = await owner.Http.SendAsync(new HttpRequest("POST", $"loop/{shape}/{stray}/{head}"), worker, mode, () => owner.PumpAsync());
                loops[$"{shape}/{stray}/{head}"] = $"{answer.StatusCode} {answer.Body}";
            }
            check(loops["plain/False/a-head"].StartsWith("500", StringComparison.Ordinal) && loops.Values.All(outcome => outcome == loops["plain/False/a-head"]),
                $"{mode}: a loop entered from a second entry is classified as from the start node, whatever its node ids ({string.Join(", ", loops.Select(l => l.Key + ": " + l.Value))})");
        }
    }
}
