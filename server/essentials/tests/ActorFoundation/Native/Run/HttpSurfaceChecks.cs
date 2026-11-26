using System.Text.Json.Nodes;

namespace Kernel.Native.Run;

/// <summary>
/// The request and response data a designed HTTP endpoint needs: the query string and headers on the way in, headers on
/// the way out, under the <see cref="HttpFields"/> rules, through the in-process <see cref="HttpHost"/>.
/// </summary>
internal static class HttpSurfaceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("http surface");
        Fields(check);
        await HostAsync(check);
    }

    private static void Fields(Action<bool, string> check)
    {
        var request = new HttpRequest("GET", "hello")
        {
            Query = HttpFields.Query([new("name", "Ada"), new("name", "Lin"), new("Name", "other"), new("", "nameless")]),
            Headers = HttpFields.RequestHeaders([new("X-Tenant", "acme"), new("x-tenant", "beta"), new("Authorization", "Bearer secret"), new("COOKIE", "s=1"), new("X-Api-Key", "k")]),
        };
        check(request.Query.Count == 2 && request.Query["name"] == "Ada,Lin" && request.Query["Name"] == "other" && !request.Query.ContainsKey("NAME") &&
              Values.Path(Values.From(request), "Query.name")?.GetValue<string>() == "Ada,Lin",
            "query names are kept exactly as sent, so a binding reads Query.name; repeats join with ',' and a nameless pair is left out");
        check(request.Headers.Count == 1 && request.Headers["X-TENANT"] == "acme, beta" && request.Headers.Keys.Single() == "x-tenant",
            "header names are stored in lower case, looked up without case, and repeats join with ', '");
        var json = Json.Write(request);
        var back = Values.To<HttpRequest>(JsonNode.Parse(json))!;
        check(!json.Contains("secret", StringComparison.Ordinal) && !json.Contains("s=1", StringComparison.Ordinal) && back.Headers["X-Tenant"] == "acme, beta" &&
              back.Query["name"] == "Ada,Lin" && !back.Query.ContainsKey("NAME") && Values.Path(Values.From(request), "Headers.x-tenant")?.GetValue<string>() == "acme, beta",
            "credentials are not kept; the fields and their comparers survive a round trip, and a binding path reads Headers.x-tenant");
        var direct = Json.Write(new HttpRequest("POST", "x")
            { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret", ["Cookie"] = "s=secret", ["X-Kept"] = "kept" } });
        var stored = Json.Read<HttpRequest>("""{"Method":"POST","Path":"x","Headers":{"Authorization":"Bearer secret","X-Kept":"kept"}}""")!;
        check(!direct.Contains("secret", StringComparison.Ordinal) && direct.Contains("kept", StringComparison.Ordinal) &&
              stored.Headers.Count == 1 && stored.Headers["x-kept"] == "kept",
            "the request's own Headers drops credentials, whoever builds it and when it is read back");
        check(new HttpRequest("POST", "x").Headers.Count == 0 && Values.To<HttpRequest>(JsonNode.Parse("""{"Method":"POST","Path":"x"}"""))!.Query.Count == 0,
            "a request written before this change reads back with empty fields");

        check(Refused(() => new HttpRequest("GET", "x") { Headers = Fields("h", HttpFields.MaxFields + 1) }) &&
              !Refused(() => new HttpRequest("GET", "x") { Headers = Fields("h", HttpFields.MaxFields) }) &&
              Refused(() => new HttpRequest("GET", "x") { Query = Fields("q", HttpFields.MaxFields + 1) }),
            $"a collection holds at most {HttpFields.MaxFields} fields");
        var fill = HttpFields.MaxBytes - 1;     // the name "a" and its value
        check(!Refused(() => new HttpRequest("GET", "x") { Headers = Map("a", new string('v', fill)) }) &&
              Refused(() => new HttpRequest("GET", "x") { Headers = Map("a", new string('v', fill + 1)) }) &&
              Refused(() => new HttpRequest("GET", "x") { Query = Map("a", new string('é', fill)) }),
            $"a collection takes at most {HttpFields.MaxBytes} UTF-8 bytes");
        check(Refused(() => new HttpRequest("GET", "x") { Headers = Map("bad name", "v") }) &&
              Refused(() => new HttpRequest("GET", "x") { Headers = Map("x-ok", "a\r\nInjected: yes") }) &&
              Refused(() => new HttpRequest("GET", "x") { Headers = Map("x-ok", "café") }) &&
              Refused(() => new HttpRequest("GET", "x") { Headers = Map("x-ok", "a\u0001b") }) &&
              !Refused(() => new HttpRequest("GET", "x") { Headers = Map("x-ok", "a\tb ~") }),
            "a header name must be an HTTP token, and a value is visible ASCII, space or tab");

        var headers = HttpFields.ResponseHeaders(new JsonObject { ["Retry-After"] = 120, ["X-Flag"] = true, ["X-Name"] = "n" });
        var built = HttpFields.ResponseHeaders(new JsonObject { ["X-Id"] = JsonValue.Create(Guid.Empty) });
        check(headers["retry-after"] == "120" && headers["x-flag"] == "true" && headers["X-NAME"] == "n" && built["x-id"] == Guid.Empty.ToString(),
            "response headers take strings, numbers and booleans, also a string value built from a CLR value");
        check(Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["X-List"] = new JsonArray("a") })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["X-Null"] = null })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["content-type"] = "text/html" })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["Content-Length"] = 1 })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["Connection"] = "close" })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["Set-Cookie"] = "a=1" })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["X-Twice"] = "a", ["x-twice"] = "b" })) &&
              Refused(() => HttpFields.ResponseHeaders(new JsonObject { ["X-Name"] = "Zoë" })),
            "a response header is a scalar given once, in visible ASCII, and the edge's own, hop-by-hop and set-cookie headers are refused");
        var response = Values.To<HttpResponse>(Values.From(new HttpResponse(201, "text/plain", "ok") { Headers = headers }))!;
        check(response.Headers["Retry-After"] == "120" && response.Headers.Count == 3, "response headers survive the live channel's round trip");
    }

    private static async Task HostAsync(Action<bool, string> check)
    {
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("surface-start", Wf.Sequence("root",
                Wf.HttpEndpoint("in", "surface", "GET"),
                Wf.Respond("out", Binding.Expr("$in.Query.name + '@' + $in.Headers['x-tenant']"), headers: Binding.Literal(new JsonObject { ["X-Seen"] = "yes" })))))
            .Add(Wf.Workflow("surface-wait", Wf.Sequence("root",
                Wf.HttpEndpoint("in", "surface/begin"),
                Wf.Respond("first", "waiting"),
                Wf.HttpEndpoint("again", "surface/resume"),
                Wf.Respond("second", Binding.Expr("$again.Query.step + '@' + $again.Headers['x-tenant']"), status: 202,
                    headers: Binding.Literal(new JsonObject { ["Location"] = "/surface/done" })))))
            .Add(Wf.Workflow("surface-refused", Wf.Sequence("root",
                Wf.HttpEndpoint("in", "surface/refused"),
                Wf.Respond("out", "never", headers: Binding.Literal(new JsonObject { ["X-List"] = new JsonArray(1, 2) })))));
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            var owner = new Owner("surface", 0, new InMemoryStore(1, () => Environment.TickCount64), registry, new Network());
            await using var worker = owner.Worker;
            await owner.StartAsync();

            var started = await owner.Http.SendAsync(new HttpRequest("GET", "surface")
            {
                Query = Map("name", "Ada"),
                Headers = new Dictionary<string, string> { ["X-Tenant"] = "acme", ["Authorization"] = "Bearer secret" },
            }, worker, mode, () => owner.PumpAsync());
            check(started is { StatusCode: 200 } && started.Body?.GetValue<string>() == "Ada@acme" && started.Headers["x-seen"] == "yes",
                $"{mode}: a start reads its query and headers, and its response carries its headers");

            var first = await owner.Http.SendAsync(new HttpRequest("POST", "surface/begin"), worker, mode, () => owner.PumpAsync());
            var resumed = await owner.Http.SendAsync(new HttpRequest("POST", "surface/resume")
            {
                Query = Map("step", "2"),
                Headers = Map("x-tenant", "beta"),
            }, worker, mode, () => owner.PumpAsync(poll: true));
            check(first.Body?.GetValue<string>() == "waiting" && resumed is { StatusCode: 202 } && resumed.Body?.GetValue<string>() == "2@beta" &&
                  resumed.Headers["location"] == "/surface/done",
                $"{mode}: a resumed wait reads the second request's query and headers, and answers with its own headers");

            var refused = await owner.Http.SendAsync(new HttpRequest("POST", "surface/refused"), worker, mode, () => owner.PumpAsync());
            check(refused.StatusCode == 500, $"{mode}: a response header that is not a scalar faults the workflow and the caller gets 500 ({refused.StatusCode})");
        }
    }

    private static Dictionary<string, string> Fields(string prefix, int count) =>
        Enumerable.Range(0, count).ToDictionary(i => $"{prefix}{i}", _ => "v");

    private static Dictionary<string, string> Map(string name, string value) => new() { [name] = value };

    private static bool Refused(Action action)
    {
        try { action(); return false; }
        catch (HttpFieldsException) { return true; }
    }
}
