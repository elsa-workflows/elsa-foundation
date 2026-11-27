using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.Hosting;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
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
            var alias = Path.Combine(workspace.FullName, "configured-kernel.sqlite");
            File.CreateSymbolicLink(alias, path);
            string flowchart;
            string first;
            using (var host = WorkbenchProcess.Start(executable, "", enabled: true, "publications", null, alias, workspace))
            {
                await host.WaitReadyAsync(token);
                Require((await host.WaitForKernelServingAsync(token)).Contains("shell default on the kernel", StringComparison.Ordinal),
                    "the SQLite Workbench serves its default shell's publications on the kernel");
                using var client = await AuthenticateAsync(host);
                using var native = await client.PostAsJsonAsync("/workflows/http/core-preview/probe", new { name = "Ada" }, token);
                Require(native.StatusCode == HttpStatusCode.OK && await native.Content.ReadAsStringAsync(token) == "Ada",
                    "the normal HTTP edge executes a Native sequence on SQLite");
                const string nativeDefinition = "core-preview-http-probe";
                await CheckRunDetailsAsync(client, nativeDefinition, (await FindRunsAsync(client, nativeDefinition, 1)).Single());
                var published = await PublishFlowchartAsync(client);
                flowchart = published.Definition;
                await CheckAuthoringTestRunAsync(client, published.Version);
                await RunFlowchartAsync(client);
                first = (await FindRunsAsync(client, flowchart, 1)).Single();
                await CheckRunDetailsAsync(client, flowchart, first);
                using var protectedRead = await client.GetAsync(PreviewRunsPath(flowchart), token);
                Require(protectedRead.StatusCode == HttpStatusCode.Forbidden,
                    "the SQLite preview preserves the stock shell's resource authorization guard");
                await host.StopAsync();
            }

            var observer = new SqliteEmbeddedFoundation(path, "sqlite-workbench-observer", WorkflowActorType.ActorType);
            await observer.InstallShippedAsync(token);
            var reads = observer.CreateReads();
            var tenant = EmbeddedTenant.Bind("default");
            Require(await reads.GetAsync(tenant, new(first), token) is { Header.Status: Contract.ActorStatus.Finished } &&
                (await reads.TailAsync(tenant, new(first), cancellationToken: token)).Items.Count > 0,
                "the stopped SQLite file contains the published flowchart checkpoint and journal");

            var empty = Path.Combine(workspace.FullName, "empty.sqlite");
            await SqliteEmbeddedHosting.InstallShippedAsync(empty, token);
            await using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = empty }.ConnectionString))
            {
                await db.OpenAsync(token);
                await using var mode = db.CreateCommand();
                mode.CommandText = "PRAGMA journal_mode";
                Require(await mode.ExecuteScalarAsync(token) is "wal", "the shipped installer enables WAL before any owner or reader starts");
            }

            var shellPath = Path.Combine(workspace.FullName, "shells.json");
            var shell = JsonNode.Parse(await File.ReadAllTextAsync(shellPath, token))!;
            shell["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject().Remove("DecisionsWorkflowsApi");
            await File.WriteAllTextAsync(shellPath, shell.ToJsonString(), token);
            using (var host = WorkbenchProcess.Start(executable, "", enabled: true, "publications", null, alias, workspace))
            {
                await host.WaitReadyAsync(token);
                await host.WaitForKernelServingAsync(token);
                using var client = await AuthenticateAsync(host);
                Require((await FindRunsAsync(client, flowchart, 1)).Single() == first,
                    "the restarted SQLite Workbench returns the prior run through its runtime API");
                await CheckRunDetailsAsync(client, flowchart, first);
                File.Delete(alias);
                File.CreateSymbolicLink(alias, empty);
                var observations = await ReadAsync(client, PreviewRunsPath(flowchart));
                Require(observations.GetProperty("asOf").GetInt64() > 0 && observations.GetProperty("items").EnumerateArray().Single()
                    .GetProperty("ref").GetString() == first, "an opt-in reader opened after symlink retargeting still reads the installed file");
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

            async Task<(string Definition, string Version)> PublishFlowchartAsync(HttpClient client)
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
                return (submitted.GetProperty("definition").GetProperty("id").GetString()!, version!);

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

            async Task CheckAuthoringTestRunAsync(HttpClient client, string version)
            {
                var started = await ReadAsync(client, $"/publishing/workflows/{Uri.EscapeDataString(version)}/test-runs",
                    new { acknowledgeUnavailableExpressionValidation = true });
                var execution = started.GetProperty("workflowExecutionId").GetString()!;
                while (true)
                {
                    using var response = await client.GetAsync($"/runtime/workflows/instances/{Uri.EscapeDataString(execution)}", token);
                    if (response.StatusCode == HttpStatusCode.NotFound) { await Task.Delay(100, token); continue; }
                    var body = await response.Content.ReadAsStringAsync(token);
                    Require(response.IsSuccessStatusCode, $"the author's test run read answered {(int)response.StatusCode}: {body}");
                    using var details = JsonDocument.Parse(body);
                    var instance = details.RootElement.GetProperty("instance");
                    Require(instance.GetProperty("workflowExecutionId").GetString() == execution &&
                            instance.GetProperty("runKind").GetString() == "TestRun",
                        "the standard run details endpoint preserves the author's TestRun on a kernel shell");
                    return;
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

            async Task CheckRunDetailsAsync(HttpClient client, string definition, string run)
            {
                var route = $"/runtime/workflows/instances/{Uri.EscapeDataString(run)}";
                var details = await ReadAsync(client, route);
                var instance = details.GetProperty("instance");
                Require(instance.GetProperty("workflowExecutionId").GetString() == run &&
                        instance.GetProperty("definitionId").GetString() == definition &&
                        instance.GetProperty("status").GetString() == "Completed" &&
                        instance.GetProperty("startedAt").ValueKind == JsonValueKind.String &&
                        instance.GetProperty("completedAt").ValueKind == JsonValueKind.String,
                    $"the standard run details endpoint shows the exact completed SQLite run of '{definition}' and its committed times");
                await RequireActivityTimelineAsync(client, route, run, token);
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
