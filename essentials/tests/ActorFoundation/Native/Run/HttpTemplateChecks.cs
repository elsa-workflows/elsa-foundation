using System.Text.Json.Nodes;

namespace Kernel.Native.Run;

/// <summary>Route templates (design card 1, option A): starts on the template, waits on the template resolved with their values.</summary>
internal static class HttpTemplateChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("http templates");
        Templates(check);
        Table(check);
        await HostAsync(check);
    }

    private static void Templates(Action<bool, string> check)
    {
        var parsed = HttpRouteTemplate.Parse(" /Orders/{ID}/Cancel/ ");
        check(parsed.Text == "orders/{id}/cancel" && parsed.Shape == "orders/{}/cancel" && parsed.FullyBound &&
              HttpRouteTemplate.Parse("files/{*Path}").Text == "files/{*path}" && HttpRouteTemplate.Parse("reports/{year?}").Shape == "reports/{?}" &&
              HttpRouteTemplate.Parse("/").Segments.Count == 0,
            "a template keeps literals and names in lower case, and its shape drops the names");
        check(new[] { "orders/{id:int}", "orders/{id=1}", "orders/x{id}", "{id?}/x", "{*rest}/x", "a//b", "{id}/{ID}", "orders/{}", "orders/{a-b}" }
                .All(template => Refused(() => HttpRouteTemplate.Parse(template))),
            "constraints, defaults, partial segments, optional or catch-all before the end, empty segments and repeated names are refused");
        var cancel = HttpRouteTemplate.Parse("orders/{id}/cancel");
        check(cancel.Resolve(new Dictionary<string, string> { ["ID"] = "A/42" }) == "orders/A%2F42/cancel" &&
              Refused(() => cancel.Resolve(null)) && Refused(() => cancel.Resolve(new Dictionary<string, string> { ["id"] = "" })) &&
              Refused(() => HttpRouteTemplate.Parse("reports/{year?}").Resolve(new Dictionary<string, string> { ["year"] = "2026" })),
            "a wait's template resolves with each value escaped as one segment, and refuses a missing value or an unbound parameter");
        HttpRouteTemplate T(string text) => HttpRouteTemplate.Parse(text);
        check(HttpRouteTemplate.Compare(T("orders/list"), T("orders/{id}")) < 0 && HttpRouteTemplate.Compare(T("orders/{id}"), T("orders/{id?}")) < 0 &&
              HttpRouteTemplate.Compare(T("orders/{id?}"), T("orders/{*rest}")) < 0 && HttpRouteTemplate.Compare(T("orders"), T("orders/{id?}")) < 0,
            "a literal is more specific than a parameter, a parameter than an optional one, an optional one than a catch-all");
        var longPath = new string('x', 600);
        var hashed = HttpRoutes.Key("post", longPath);
        check(HttpRoutes.Key("post", "Orders/42") == "http:POST:orders/42" && hashed.StartsWith("http:POST:h:", StringComparison.Ordinal) &&
              hashed.Length == "http:POST:h:".Length + 64 && hashed == HttpRoutes.Key("POST", "/" + longPath.ToUpperInvariant() + "/"),
            $"a key past {HttpRoutes.MaxReadableKeyBytes} bytes is the SHA-256 of its normalized path, the same on both sides");
        var request = new HttpRequest("POST", "x") { RouteValues = new Dictionary<string, string> { ["ID"] = "Ab" } };
        var back = Values.To<HttpRequest>(Values.From(request))!;
        check(back.RouteValues["id"] == "Ab" && back.RouteValues.Keys.Single() == "id" && Values.Path(Values.From(request), "RouteValues.id")?.GetValue<string>() == "Ab",
            "a request's route values keep lower-case names and their values' case, and survive a round trip");
    }

    private static void Table(Action<bool, string> check)
    {
        static Workflow Starts(string id, string path, string method = "POST") =>
            Wf.Workflow(id, Wf.Sequence("main", Wf.HttpEndpoint("in", path, method), Wf.Respond("out", id)));
        static Workflow Waits(string id, string path, string start = "begin") =>
            Wf.Workflow(id, Wf.Sequence("main", Wf.HttpEndpoint("in", start + "/" + id), Wf.HttpEndpoint("wait", path, routeValues: Binding.Result("in", "RouteValues"))));

        var shape = Conflict(new WorkflowRegistry().Add(Starts("by-id", "orders/{id}")).Add(Starts("by-key", "Orders/{Key}")));
        check(shape is { StartAndWait: false, Path: "orders/{key}", First: ("by-id", "in"), Second: ("by-key", "in") },
            "two definitions that start on one template shape are refused");
        var mixed = Conflict(new WorkflowRegistry().Add(Waits("waiter", "approve/{id}")).Add(Starts("starter", "approve/{code}")));
        var reversed = Conflict(new WorkflowRegistry().Add(Starts("starter", "approve")).Add(Waits("waiter", "approve")));
        check(mixed is { StartAndWait: true, First: ("waiter", "wait"), Second: ("starter", "in") } && reversed is { StartAndWait: true },
            "a shape that is a start in one definition and a wait in another is refused, whichever comes first");
        var waits = new HttpRoutes(new WorkflowRegistry().Add(Waits("w1", "orders/{id}/cancel")).Add(Waits("w2", "orders/{order}/cancel")));
        check(waits.Routes.Count(route => !route.Start) == 2, "two waits on one shape are fine: each resolves to its own key");
        check(TemplateRefused(new WorkflowRegistry().Add(Waits("optional", "orders/{id?}"))) is { } unbound && unbound.Message.Contains("'optional'", StringComparison.Ordinal) &&
              TemplateRefused(new WorkflowRegistry().Add(Starts("constrained", "orders/{id:int}"))) is { } constrained && constrained.Message.Contains("'constrained'", StringComparison.Ordinal),
            "a wait with an optional parameter, or a constrained template, is refused when the table is built, naming the workflow");
        var ordered = new HttpRoutes(new WorkflowRegistry().Add(Starts("all", "orders/{*rest}")).Add(Starts("one", "orders/{id}")).Add(Starts("list", "orders/list")));
        check(ordered.Routes.Select(route => route.Workflow).SequenceEqual(["list", "one", "all"]), "the routes come most specific first");
    }

    private static async Task HostAsync(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("enrich", Wf.Sequence("main", Wf.HttpEndpoint("in", "orders/{id}/enrich"),
                Wf.Respond("out", Binding.Expr("'enrich ' + $in.RouteValues.id")))))
            .Add(Wf.Workflow("list", Wf.Sequence("main", Wf.HttpEndpoint("in", "items/list", "GET"), Wf.Respond("out", "the list"))))
            .Add(Wf.Workflow("item", Wf.Sequence("main", Wf.HttpEndpoint("in", "items/{id}", "GET"), Wf.Respond("out", Binding.Expr("'item ' + $in.RouteValues.id")))))
            .Add(Wf.Workflow("files", Wf.Sequence("main", Wf.HttpEndpoint("in", "files/{*path}", "GET"), Wf.Respond("out", Binding.Expr("'file ' + $in.RouteValues.path")))))
            .Add(Wf.Workflow("reports", Wf.Sequence("main", Wf.HttpEndpoint("in", "reports/{year?}", "GET"),
                Wf.Respond("out", Binding.Expr("'report ' + $in.RouteValues.year")))))
            .Add(Wf.Workflow("ship", Wf.Sequence("main", Wf.HttpEndpoint("in", "orders/{id}/ship"), Wf.Respond("accepted", Binding.Expr("'shipping ' + $in.RouteValues.id"), 202),
                Wf.HttpEndpoint("cancel", "orders/{id}/cancel", routeValues: Binding.Result("in", "RouteValues")),
                Wf.Respond("cancelled", Binding.Expr("'cancelled ' + $cancel.RouteValues.id")))))
            .Add(Wf.Workflow("unbound", Wf.Sequence("main", Wf.HttpEndpoint("in", "unbound/{id}"),
                Wf.HttpEndpoint("wait", "unbound/{id}/later"), Wf.Respond("out", "never"))));
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var owner = new Owner("templates", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await using var worker = owner.Worker;
            await owner.StartAsync();
            Task<HttpResponse> Send(string method, string path, bool poll = false) =>
                owner.Http.SendAsync(new HttpRequest(method, path), worker, mode, () => owner.PumpAsync(poll: poll));

            var enrich = await Send("POST", "/Orders/42/Enrich");
            check(enrich.Body?.GetValue<string>() == "enrich 42", $"{mode}: a start on a template gets its route value ({enrich.StatusCode} {enrich.Body})");
            var list = await Send("GET", "items/list");
            var item = await Send("GET", "items/7");
            var file = await Send("GET", "files/a/b%20c/d");
            var all = await Send("GET", "reports/2026");
            var none = await Send("GET", "reports");
            check(list.Body?.GetValue<string>() == "the list" && item.Body?.GetValue<string>() == "item 7" && file.Body?.GetValue<string>() == "file a/b c/d" &&
                  all.Body?.GetValue<string>() == "report 2026" && none is { StatusCode: 200 },
                $"{mode}: the most specific template wins, and optional and catch-all parameters take what is there ({list.Body}, {item.Body}, {file.Body}, {all.Body})");

            var first = await Send("POST", "orders/41/ship");
            var second = await Send("POST", "orders/42/ship");
            var shipping = owner.Worker.Instances.Where(i => i.Row?.Definition == "ship").Select(i => i.Id).ToArray();
            var cancelled = await Send("POST", "orders/42/cancel", poll: true);
            var states = shipping.Select(id => owner.Status(id)).ToArray();
            check(first is { StatusCode: 202 } && second is { StatusCode: 202 } && cancelled.Body?.GetValue<string>() == "cancelled 42" &&
                  states.Count(status => status == InstanceStatus.Finished) == 1 && states.Count(status => status == InstanceStatus.Running) == 1,
                $"{mode}: a wait on orders/{{id}}/cancel resolves with its order's id, so cancelling order 42 resumes only that order ({cancelled.Body}; {string.Join(", ", states)})");
            var other = await Send("POST", "orders/41/cancel", poll: true);
            check(other.Body?.GetValue<string>() == "cancelled 41" && shipping.All(id => owner.Status(id) == InstanceStatus.Finished),
                $"{mode}: the other order's wait is still there for its own request ({other.Body})");

            var unbound = await Send("POST", "unbound/1");
            check(unbound.StatusCode == 500, $"{mode}: a wait whose route has no value for a parameter faults its workflow, and the caller gets 500 ({unbound.StatusCode})");
        }
    }

    private static HttpRouteConflictException? Conflict(WorkflowRegistry registry)
    {
        try { _ = new HttpRoutes(registry); return null; }
        catch (HttpRouteConflictException conflict) { return conflict; }
    }

    private static HttpRouteTemplateException? TemplateRefused(WorkflowRegistry registry)
    {
        try { _ = new HttpRoutes(registry); return null; }
        catch (HttpRouteTemplateException refused) { return refused; }
    }

    private static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (HttpRouteTemplateException) { return true; }
    }
}
