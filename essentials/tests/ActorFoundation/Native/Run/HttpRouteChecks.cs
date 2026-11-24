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
            .Add(Wf.Workflow("flowchart-expression", Wf.Flowchart("flow",
                [Wf.HttpEndpoint("in", Binding.Expr("'computed'")), Wf.Respond("out", "ok")],
                [new Connection("in", "out")])))
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
        check(routes.TryStart("POST", "flowchart-loop", out var loop) && loop == ("flowchart-loop", "in"),
            "when every node has an inbound connection, the Flowchart's first node is its start node, as the graph engine picks it");
        check(!routes.TryStart("POST", "computed", out _) && !routes.TryStart("POST", "flowchart-broken", out _),
            "a start endpoint needs a literal path, and a Flowchart the graph engine refuses registers no route");
    }

    private static async Task FlowchartStartAsync(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry().Add(Wf.Workflow("designed", Wf.Flowchart("flow",
            [Wf.HttpEndpoint("in", "designed").Capture("order", "Body"), Wf.If("check", Binding.Expr("order.id > 0")),
                Wf.Respond("accepted", Binding.Expr("'accepted ' + order.id"), status: 201), Wf.Respond("refused", "no id", status: 400)],
            [new Connection("in", "check"), new Connection("check", "accepted", "True"), new Connection("check", "refused", "False")]),
            ("order", null)));
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var owner = new Owner("routes", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await using var worker = owner.Worker;
            await owner.StartAsync();
            var accepted = await owner.Http.SendAsync(new HttpRequest("POST", "designed", Wf.Object(("id", 7))), worker, mode, () => owner.PumpAsync());
            var refused = await owner.Http.SendAsync(new HttpRequest("POST", "designed", Wf.Object(("id", 0))), worker, mode, () => owner.PumpAsync());
            check(accepted is { StatusCode: 201 } && accepted.Body?.GetValue<string>() == "accepted 7" && refused is { StatusCode: 400 },
                $"{mode}: a request starts a designer-shaped Flowchart at its HttpEndpoint and the branch it takes answers ({accepted.StatusCode}, {refused.StatusCode})");
        }
    }
}
