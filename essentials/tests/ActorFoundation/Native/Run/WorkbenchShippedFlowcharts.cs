using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task RunShippedFlowchartsAsync(string executable, bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-flowcharts-");
        var sqlite = postgres ? null : Path.Combine(workspace.FullName, "kernel.sqlite");
        var connection = postgres
            ? Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : "";
        try
        {
            using var host = StartHost();
            using var first = await ConnectAsync(host);
            var initial = new FlowchartCorpus(first, token);
            var left = await initial.PublishDecisionAsync("decision-true", true);
            var right = await initial.PublishDecisionAsync("decision-false", false);
            var cancelled = await initial.PublishCancellationAsync();
            await initial.WaitForActivitiesAsync(left, "mid", "finance");
            await initial.WaitForActivitiesAsync(right, "mid", "finance");
            await initial.WaitForActivitiesAsync(cancelled, "loser", "stop");
            await initial.RequireWaitingAsync(left, "join", "end");
            await initial.RequireWaitingAsync(right, "join", "end");
            await initial.RequireWaitingAsync(cancelled, "join", "end");
            await host.StopAsync();

            using var restarted = StartHost();
            using var second = await ConnectAsync(restarted);
            var corpus = new FlowchartCorpus(second, token);
            await corpus.CompleteDecisionAsync("decision-true", left);
            await corpus.CompleteDecisionAsync("decision-false", right);
            await corpus.CompleteCancellationAsync(cancelled);
            await corpus.CheckLoopAsync();
            await restarted.StopAsync();
            Console.WriteLine($"ok shipped mapped Flowchart corpus on {(postgres ? "PostgreSQL" : "SQLite")}, owner processes {host.Id} and {restarted.Id}.");
        }
        finally { workspace.Delete(recursive: true); }

        WorkbenchProcess StartHost() => WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null,
            sqlite, workspace, diagnosticHistoryDisabled: true);

        async Task<HttpClient> ConnectAsync(WorkbenchProcess process)
        {
            await process.WaitReadyAsync(token);
            await process.WaitForKernelServingAsync(token);
            var address = new Uri(await process.ListeningAsync(token));
            Require(address.IsLoopback && address.Port != 5095, "the Workbench uses a dedicated loopback listener");
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
            try
            {
                using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token);
                Require(login.IsSuccessStatusCode, "the administrator signs in through the normal identity route");
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    // Selected shapes from the Flowchart conformance corpus use only authored activities and gateway policies.
    // Standard History observes their committed order and identities with diagnostic history disabled.
    private sealed class FlowchartCorpus(HttpClient client, CancellationToken token)
    {
        private const string Root = "/runtime/workflows/instances";
        private const string Paths = "/workflows/http/core-preview/";
        private const string Flowchart = "Elsa.Activities.Flowchart.Activities.Flowchart";
        private const string HttpEndpoint = "Elsa.Activities.Http.Activities.HttpEndpoint";
        private const string WriteLine = "Elsa.Activities.Primitives.Activities.WriteLine";
        private const string Break = "Elsa.Activities.Primitives.Activities.Break";
        private const string If = "Elsa.Activities.If.Activities.If";
        private static readonly object Integer = new { alias = "Int32", collectionKind = "single" };
        private static readonly object Boolean = new { alias = "Boolean", collectionKind = "single" };
        private readonly Dictionary<string, string> _versions = new(StringComparer.Ordinal);

        internal async Task<string> PublishDecisionAsync(string name, bool condition)
        {
            await PublishAsync(name,
                [await EndpointAsync(name, "start", true), await AnswerAsync("answer", "opened"), await LineAsync("fork"),
                    await DecisionAsync("choose", Literal(condition)), await LineAsync("mid"), await EndpointAsync(name, "finance"),
                    await LineAsync("join"), await AnswerAsync("end", "joined")],
                [Edge("start", "answer"), Edge("answer", "fork"), Edge("fork", "choose"), Edge("fork", "finance"),
                    Edge("choose", "mid", "True"), Edge("choose", "mid", "False"), Edge("mid", "join"),
                    Edge("finance", "join"), Edge("join", "end")],
                Policies(("fork", "parallelFork"), ("choose", "decision"), ("join", "parallelJoin")));
            return await StartAsync(name);
        }

        internal async Task<string> PublishCancellationAsync()
        {
            const string name = "break-parallel";
            await PublishAsync(name,
                [await EndpointAsync(name, "start", true), await AnswerAsync("answer", "opened"), await LineAsync("fork"),
                    await EndpointAsync(name, "loser"), await EndpointAsync(name, "stop"), await NodeAsync("break", Break, []),
                    await LineAsync("join"), await LineAsync("end")],
                [Edge("start", "answer"), Edge("answer", "fork"), Edge("fork", "loser"), Edge("fork", "stop"),
                    Edge("loser", "join"), Edge("stop", "break"), Edge("break", "join", "Break"), Edge("join", "end")],
                Policies(("fork", "parallelFork"), ("join", "parallelJoin")));
            return await StartAsync(name);
        }

        internal async Task CompleteDecisionAsync(string name, string run)
        {
            await RequireWaitingAsync(run, "join", "end");
            using var response = await CallAsync(name + "/finance", HttpStatusCode.OK, "joined");
            var history = await FinishedAsync(run);
            var starts = Nodes(history, "ActivityStarted");
            var completed = Nodes(history, "ActivityCompleted");
            Require(starts.Order(StringComparer.Ordinal).SequenceEqual(
                new[] { "root", "start", "answer", "fork", "choose", "mid", "finance", "join", "end" }.Order(StringComparer.Ordinal)),
                $"{name} starts the shared target, Join and successor exactly once");
            var finance = history.FindIndex(entry => entry is { Kind: "ActivityCompleted", Node: "finance" });
            var mid = history.FindIndex(entry => entry is { Kind: "ActivityCompleted", Node: "mid" });
            Require(completed.Contains("root") &&
                history.FindIndex(entry => entry is { Kind: "ActivityStarted", Node: "join" }) > Math.Max(finance, mid) &&
                finance >= 0 && mid >= 0, $"{name} joins only after both live inbounds complete across the restart");
        }

        internal async Task CompleteCancellationAsync(string run)
        {
            await RequireWaitingAsync(run, "join", "end");
            using var stopped = await CallAsync("break-parallel/stop", HttpStatusCode.NoContent);
            var history = await FinishedAsync(run);
            var losing = history.Single(entry => entry is { Kind: "ActivityStarted", Node: "loser" });
            Require(history.Count(entry => entry is { Kind: "ActivityCancelled", Node: "loser" } && entry.Activity == losing.Activity) == 1 &&
                Nodes(history, "ActivityStarted").Contains("break") &&
                !history.Any(entry => entry.Node is "join" or "end" || entry is { Kind: "ActivityCompleted", Node: "loser" }),
                "Break cancels the exact waiting sibling and finishes without firing the Join or its successor");
            using var late = await CallAsync("break-parallel/loser", HttpStatusCode.NotFound);
        }

        internal async Task CheckLoopAsync()
        {
            const string name = "alternating-loop";
            var increment = new
            {
                nodeId = "increment", activityVersionId = "elsa.intrinsic.set@1", outputs = Array.Empty<object>(),
                intrinsic = new { kind = "set", valueType = Integer, variable = new { referenceKey = "round", declaringScopeId = "workflow" } },
                inputs = Inputs(("value", RoundExpression("args.round + 1", Integer)))
            };
            var graph = await GraphAsync("graph", "head",
                [await LineAsync("head"), await DecisionAsync("pick", RoundExpression("args.round % 2 === 0", Boolean)), await LineAsync("left"),
                    await LineAsync("right"), await LineAsync("join"), increment,
                    await DecisionAsync("gate", RoundExpression("args.round < 3", Boolean)), await LineAsync("end")],
                [Edge("head", "pick"), Edge("pick", "left", "True"), Edge("pick", "right", "False"),
                    Edge("left", "join"), Edge("right", "join"), Edge("join", "increment"),
                    Edge("increment", "gate"), Edge("gate", "head", "True"), Edge("gate", "end", "False")],
                Policies(("pick", "decision"), ("join", "parallelJoin"), ("gate", "decision")));
            // Keep the boot HTTP activity outside the loop's graph, so the back edge is the head's only inbound edge.
            object[] steps = [await EndpointAsync(name, "start", true), await AnswerAsync("answer", "opened"), graph];
            var root = await NodeAsync("root", "Elsa.Activities.Sequence.Activities.Sequence", [],
                new { kind = "elsa.sequence.structure", schemaVersion = "1.0.0", payload = new { activities = steps } });
            await PublishRootAsync(name, root,
                [new { referenceKey = "round", name = "round", type = Integer, @default = Literal(0) }]);
            var run = await StartAsync(name);
            var history = await FinishedAsync(run);
            var starts = history.Where(entry => entry.Kind == "ActivityStarted").ToArray();
            Require(starts.Select(entry => entry.Node).SequenceEqual(["root", "start", "answer", "graph",
                "head", "pick", "left", "join", "increment", "gate",
                "head", "pick", "right", "join", "increment", "gate",
                "head", "pick", "left", "join", "increment", "gate", "end"]) &&
                starts.Where(entry => entry.Node == "join").Select(entry => entry.Activity).Distinct(StringComparer.Ordinal).Count() == 3,
                "each iteration follows its chosen branch and fires one distinct Join before finishing the final round");
        }

        internal async Task WaitForActivitiesAsync(string run, params string[] nodes)
        {
            while (!nodes.All(Nodes(await HistoryAsync(run), "ActivityStarted").Contains))
            {
                Require(await StatusAsync(run) == "Running", "the graph stays active until the required waits are committed");
                await Task.Delay(50, token);
            }
        }

        internal async Task RequireWaitingAsync(string run, params string[] absent)
        {
            Require(await StatusAsync(run) == "Running" && (await HistoryAsync(run)).All(entry => !absent.Contains(entry.Node)),
                "the published graph remains running and has not fired its Join or successor");
        }

        private async Task<List<Observation>> FinishedAsync(string run)
        {
            while (true)
            {
                var status = await StatusAsync(run);
                if (status == "Completed") return await HistoryAsync(run);
                Require(status == "Running", $"the published Flowchart remains running or completes ({status})");
                await Task.Delay(50, token);
            }
        }

        private async Task<string> StatusAsync(string run) =>
            (await ReadAsync(RunPath(run))).GetProperty("instance").GetProperty("status").GetString()!;

        private async Task<List<Observation>> HistoryAsync(string run)
        {
            var observations = new List<Observation>();
            var route = RunPath(run) + "/history?limit=100";
            string? cursor = null;
            do
            {
                var page = await ReadAsync(cursor is null ? route : route + "&cursor=" + Uri.EscapeDataString(cursor));
                Require(page.GetProperty("coverage").GetProperty("policyId").GetString() == "kernel-activity-journal-v1",
                    "the public route reports committed kernel observations");
                observations.AddRange(page.GetProperty("items").EnumerateArray().Select(row => new Observation(
                    row.GetProperty("name").GetString()!, row.GetProperty("executableNodeId").GetString()!,
                    row.GetProperty("activityExecutionId").GetString()!)));
                cursor = page.GetProperty("nextContinuationToken").GetString();
            } while (cursor is not null);
            return observations;
        }

        private async Task PublishAsync(string name, object[] activities, object[] connections, object nodeMetadata) =>
            await PublishRootAsync(name, await GraphAsync("root", "start", activities, connections, nodeMetadata), []);

        private Task<object> GraphAsync(string id, string start, object[] activities, object[] connections, object nodeMetadata) =>
            NodeAsync(id, Flowchart, [], new
            {
                kind = "elsa.flowchart.structure", schemaVersion = "1.0.0",
                payload = new { activities, connections, nodeMetadata, startNodeId = start }
            });

        private async Task PublishRootAsync(string name, object root, object[] variables)
        {
            var submitted = await ReadAsync("/design/workflows/definitions/submit", new
            {
                name, state = new { rootActivity = root, variables, inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
            });
            var version = submitted.GetProperty("version").GetProperty("id").GetString()!;
            await ReadAsync($"/publishing/workflows/{Uri.EscapeDataString(version)}/publish", new { });
        }

        private Task<object> EndpointAsync(string name, string id, bool start = false) => NodeAsync(id, HttpEndpoint,
            [("Path", Literal($"core-preview/{name}{(start ? "" : "/" + id)}")), ("CanStartWorkflow", Literal(start)), ("SupportedMethods", Literal(new[] { "POST" }))]);

        private Task<object> AnswerAsync(string id, string body) => NodeAsync(id, "Elsa.Activities.Http.Activities.WriteHttpResponse",
            [("StatusCode", Literal(200)), ("Body", Literal(body))]);
        private Task<object> LineAsync(string id) => NodeAsync(id, WriteLine, [("text", Literal(id))]);
        private Task<object> DecisionAsync(string id, object condition) => NodeAsync(id, If,
            [("Condition", condition)], new { kind = "elsa.if.structure", schemaVersion = "1.0.0", payload = new { } });

        private async Task<object> NodeAsync(string id, string kind, (string Key, object Value)[] inputs, object? structure = null)
        {
            if (!_versions.TryGetValue(kind, out var version))
            {
                var catalog = await ReadAsync($"/design/activities/definitions?search={Uri.EscapeDataString(kind)}");
                version = catalog.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("definition"))
                    .Single(row => row.GetProperty("activityTypeKey").GetString() == kind).GetProperty("headVersionId").GetString()!;
                _versions.Add(kind, version);
            }
            return new { nodeId = id, activityVersionId = version, structure, outputs = Array.Empty<object>(), inputs = Inputs(inputs) };
        }

        private async Task<string> StartAsync(string name)
        {
            using var response = await CallAsync(name, HttpStatusCode.OK, "opened");
            return response.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single();
        }

        private async Task<HttpResponseMessage> CallAsync(string route, HttpStatusCode expected, string? content = null)
        {
            var response = await client.PostAsJsonAsync(Paths + route, new { }, token);
            try
            {
                var body = await response.Content.ReadAsStringAsync(token);
                Require(response.StatusCode == expected && (content is null || body == content),
                    $"the published route {route} returns the expected result ({(int)response.StatusCode}: {body})");
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private async Task<JsonElement> ReadAsync(string path, object? data = null)
        {
            using var response = data is null ? await client.GetAsync(path, token) : await client.PostAsJsonAsync(path, data, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }

        private static object Edge(string from, string to, string port = "Done") =>
            new { id = $"{from}-{port}-{to}", source = new { nodeId = from, port }, target = new { nodeId = to } };
        private static object Policies(params (string Node, string Kind)[] policies) =>
            policies.ToDictionary(policy => policy.Node, policy => new { policyKind = policy.Kind }, StringComparer.Ordinal);
        private static object[] Inputs(params (string Key, object Value)[] values) =>
            values.Select(pair => new { referenceKey = pair.Key, value = pair.Value }).ToArray();
        private static object Literal(object value) => new { expressionType = "Literal", value };
        private static object RoundExpression(string source, object type) => new
        {
            expressionType = "JavaScript", value = new
            {
                language = "JavaScript", source, resultType = type, options = new { }, capabilityProfile = "binding-pure-v1",
                parameters = new { round = new { kind = "Variable", declaringScopeNodeId = "workflow", variableKey = "round" } }
            }
        };
        private static string RunPath(string run) => Root + "/" + Uri.EscapeDataString(run);
        private static string[] Nodes(IEnumerable<Observation> history, string kind) =>
            history.Where(entry => entry.Kind == kind).Select(entry => entry.Node).ToArray();
        private sealed record Observation(string Kind, string Node, string Activity);
    }
}
