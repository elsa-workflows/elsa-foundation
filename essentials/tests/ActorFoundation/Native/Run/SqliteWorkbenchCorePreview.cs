using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.Hosting;
using Kernel.Foundation.Sqlite;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task<int> RunSqliteAsync(string executable)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-core-preview-sqlite-");
        try
        {
            var path = Path.Combine(workspace.FullName, "kernel.sqlite");
            string flowchart;
            string first;
            using (var host = WorkbenchProcess.Start(executable, "", enabled: true, "publications", null, path, workspace))
            {
                await host.WaitReadyAsync(token);
                Require((await host.WaitForKernelServingAsync(token)).Contains("shell default on the kernel", StringComparison.Ordinal),
                    "the SQLite Workbench serves its default shell's publications on the kernel");
                using var client = await AuthenticateAsync(host);
                using var native = await client.PostAsJsonAsync("/workflows/http/core-preview/probe", new { name = "Ada" }, token);
                Require(native.StatusCode == HttpStatusCode.OK && await native.Content.ReadAsStringAsync(token) == "Ada",
                    "the normal HTTP edge executes a Native sequence on SQLite");
                flowchart = await PublishFlowchartAsync(client);
                await RunFlowchartAsync(client);
                first = (await FindRunsAsync(client, flowchart, 1)).Single();
                using var protectedRead = await client.GetAsync(PreviewRunsPath(flowchart), token);
                Require(protectedRead.StatusCode == HttpStatusCode.Forbidden,
                    "the SQLite preview preserves the stock shell's resource authorization guard");
                await host.StopAsync();
            }

            var reads = new SqliteEmbeddedFoundation(path, "sqlite-workbench-observer", WorkflowActorType.ActorType).CreateReads();
            var tenant = EmbeddedTenant.Bind("default");
            Require(await reads.GetAsync(tenant, new(first), token) is { Header.Status: Contract.ActorStatus.Finished } &&
                (await reads.TailAsync(tenant, new(first), cancellationToken: token)).Items.Count > 0,
                "the stopped SQLite file contains the published flowchart checkpoint and journal");

            var shellPath = Path.Combine(workspace.FullName, "shells.json");
            var shell = JsonNode.Parse(await File.ReadAllTextAsync(shellPath, token))!;
            shell["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject().Remove("DecisionsWorkflowsApi");
            await File.WriteAllTextAsync(shellPath, shell.ToJsonString(), token);
            using (var host = WorkbenchProcess.Start(executable, "", enabled: true, "publications", null, path, workspace))
            {
                await host.WaitReadyAsync(token);
                await host.WaitForKernelServingAsync(token);
                using var client = await AuthenticateAsync(host);
                Require((await FindRunsAsync(client, flowchart, 1)).Single() == first,
                    "the restarted SQLite Workbench returns the prior run through its runtime API");
                var observations = await ReadAsync(client, PreviewRunsPath(flowchart));
                Require(observations.GetProperty("asOf").GetInt64() > 0 && observations.GetProperty("items").EnumerateArray().Single()
                    .GetProperty("ref").GetString() == first, "the opt-in reads observe the persisted run in a shell with ordinary resource policy");
                await RunFlowchartAsync(client);
                var runs = await FindRunsAsync(client, flowchart, 2);
                Require(runs.Contains(first, StringComparer.Ordinal) && runs.Distinct(StringComparer.Ordinal).Count() == 2,
                    "the restarted Workbench serves the persisted Flowchart publication and commits a second run");
                await host.StopAsync();
            }
            return 0;

            static string PreviewRunsPath(string definition) =>
                $"/runtime/core-preview/workflows/instances?definition={Uri.EscapeDataString(definition)}&status=Finished";

            async Task<HttpClient> AuthenticateAsync(WorkbenchProcess host)
            {
                var address = new Uri(await host.ListeningAsync(token));
                Require(address.IsLoopback && address.Port != 5095, "the check uses its own loopback address");
                var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
                try
                {
                    using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token);
                    Require(login.IsSuccessStatusCode, "the disposable Workbench's seeded administrator signs in");
                    return client;
                }
                catch { client.Dispose(); throw; }
            }

            async Task<string> PublishFlowchartAsync(HttpClient client)
            {
                var start = await NodeAsync("start", "Elsa.Activities.Http.Activities.HttpEndpoint",
                    new() { ["Path"] = "core-preview/sqlite-flowchart", ["CanStartWorkflow"] = true, ["SupportedMethods"] = new[] { "POST" } });
                var answer = await NodeAsync("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse",
                    new() { ["StatusCode"] = 201, ["Body"] = "published SQLite flowchart" });
                var payload = new
                {
                    activities = new[] { start, answer }, startNodeId = "start",
                    connections = new[] { new { id = "start-answer", source = new { nodeId = "start", port = "Done" }, target = new { nodeId = "answer" } } }
                };
                var flow = await NodeAsync("flow", "Elsa.Activities.Flowchart.Activities.Flowchart", [],
                    new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0", payload });
                var submitted = await ReadAsync(client, "/design/workflows/definitions/submit", new
                {
                    name = "SQLite Core Preview flowchart",
                    state = new { rootActivity = flow, variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
                });
                var version = submitted.GetProperty("version").GetProperty("id").GetString();
                await ReadAsync(client, $"/publishing/workflows/{version}/publish", new { });
                return submitted.GetProperty("definition").GetProperty("id").GetString()!;

                async Task<object> NodeAsync(string nodeId, string type, Dictionary<string, object> bindings, object? structure = null)
                {
                    var catalog = await ReadAsync(client, $"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
                    var version = catalog.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("definition"))
                        .Single(row => row.GetProperty("activityTypeKey").GetString() == type).GetProperty("headVersionId").GetString();
                    return new
                    {
                        nodeId, activityVersionId = version, structure, outputs = Array.Empty<object>(),
                        inputs = bindings.Select(binding => new { referenceKey = binding.Key, value = new { expressionType = "Literal", value = binding.Value } })
                    };
                }
            }

            async Task RunFlowchartAsync(HttpClient client)
            {
                while (true)
                {
                    using var start = await client.PostAsJsonAsync("/workflows/http/core-preview/sqlite-flowchart", new { }, token);
                    if (start.StatusCode == HttpStatusCode.NotFound) { await Task.Delay(100, token); continue; }
                    Require(start.StatusCode == HttpStatusCode.Created && await start.Content.ReadAsStringAsync(token) == "published SQLite flowchart",
                        "a normally authored and published Flowchart answers from the SQLite kernel");
                    return;
                }
            }

            async Task<string[]> FindRunsAsync(HttpClient client, string definition, int expected)
            {
                while (true)
                {
                    var page = await ReadAsync(client, $"/runtime/workflows/instances/page?definitionId={Uri.EscapeDataString(definition)}&status=Completed&take=10");
                    var runs = page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("workflowExecutionId").GetString()!).ToArray();
                    if (runs.Length == expected) return runs;
                    Require(runs.Length < expected, "the runtime endpoint returned at most the expected number of runs");
                    await Task.Delay(100, token);
                }
            }

            async Task<JsonElement> ReadAsync(HttpClient client, string route, object? payload = null)
            {
                using var response = payload is null ? await client.GetAsync(route, token) : await client.PostAsJsonAsync(route, payload, token);
                var result = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{route} returned {(int)response.StatusCode}: {result}");
                using var json = JsonDocument.Parse(result);
                return json.RootElement.Clone();
            }
        }
        finally { workspace.Delete(recursive: true); }
    }
}
