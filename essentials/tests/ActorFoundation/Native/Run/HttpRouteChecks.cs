namespace Kernel.Native.Run;

/// <summary>Which published workflows a request on a route starts: the start routes of <see cref="HttpRoutes"/>.</summary>
internal static class HttpRouteChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("http routes");
        Table(check);
        await FlowchartStartAsync(check);
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
        }
    }
}
