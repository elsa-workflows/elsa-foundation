using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Workflows.Runtime.KernelHost;
using Npgsql;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task RunHttpWaitsAsync(string executable, bool postgres, bool gated, bool stock)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-http-waits-");
        var sqlite = postgres ? null : Path.Combine(workspace.FullName, "kernel.sqlite");
        var connection = postgres && !stock
            ? Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : "";
        try
        {
            using var host = stock ? WorkbenchProcess.StartStock(executable, workspace, sqlite)
                : WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null, sqlite, workspace);
            using var client = await OpenClientAsync(host);
            var stockExecution = stock ? await CheckStockExecutionAsync() : null;

            foreach (var (kind, flowchart) in new[] { ("native", false), ("chart", true) })
            {
                var path = $"{kind}/orders/{{Id}}";
                var start = await EndpointAsync("start", path + "/ship", starts: true);
                var accepted = await NodeAsync("accepted", "Elsa.Activities.Http.Activities.WriteHttpResponse",
                    ("StatusCode", Literal(202)), ("Body", BodyFrom("start")));
                var wait = await EndpointAsync("wait", path + "/cancel", starts: false, RouteData("start"));
                var cancelled = await NodeAsync("cancelled", "Elsa.Activities.Http.Activities.WriteHttpResponse",
                    ("Body", BodyFrom("wait")));
                var publication = await PublishAsync($"HTTP wait {kind}", [start, accepted, wait, cancelled], flowchart);
                if (gated)
                {
                    Require(!publication.Ok && publication.Body.Contains(KernelHostCapabilities.HttpWaitUnavailable, StringComparison.Ordinal),
                        $"{kind}: the authored RouteValues binding reaches kernel publication and the unavailable provider gate refuses it ({publication.Body}).");
                    var control = await EndpointAsync("start", $"{kind}/control/{{Id}}", starts: true);
                    var startOnly = await PublishAsync($"HTTP start {kind}", [control, accepted], flowchart);
                    Require(startOnly.Ok, $"{kind}: the provider still accepts the authored start-only workflow ({startOnly.Body}).");
                    using var reply = await client.PostAsync($"/workflows/http/{kind}/control/A%2Fb%2541", null, token);
                    Require(reply.StatusCode == HttpStatusCode.Accepted && await reply.Content.ReadAsStringAsync(token) == "A/b%41",
                        $"{kind}: the existing start route returns 202 with the exact decoded route value.");
                    continue;
                }
                Require(publication.Ok, $"{kind}: the normal publication route accepts the authored HTTP wait ({publication.Body}).");

                const string a = "A/b%41";
                const string b = "a/b%41";
                using (var missing = await PostAsync(kind, a, "cancel"))
                    Require(missing.StatusCode == HttpStatusCode.NotFound, $"{kind}: the published cancel route returns 404 before a run waits.");
                var first = await StartAsync(kind, a);
                var second = await StartAsync(kind, b);
                await ExpectStateAsync(first, "Running");
                await ExpectStateAsync(second, "Running");
                using (var wrong = await PostAsync(kind, "A/B%41", "cancel"))
                    Require(wrong.StatusCode == HttpStatusCode.NotFound, $"{kind}: a case-mismatched correlation value reaches no run.");
                await ResumeAsync(kind, b);
                await ExpectStateAsync(first, "Running");
                await ExpectStateAsync(second, "Completed");
                await ResumeAsync(kind, a);
                await ExpectStateAsync(first, "Completed");
            }

            if (!gated)
            {
                var clashes = await EndpointAsync("start", "chart/orders/{Other}/cancel", starts: true);
                var conflict = await PublishAsync("Start conflicting with a published wait", [clashes], flowchart: false);
                Require(!conflict.Ok && conflict.Body.Contains("are a start and a wait", StringComparison.Ordinal),
                    "publication refuses a start claiming the HTTP method and template shape of a served wait");
            }

            await host.StopAsync();
            if (stockExecution is not null)
            {
                if (sqlite is null) await CheckStockPostgresAsync(stockExecution);
                else await CheckStockSqliteRestartAsync(stockExecution, sqlite);
            }

            async Task<HttpClient> OpenClientAsync(WorkbenchProcess process)
            {
                await process.WaitReadyAsync(token);
                if (!stock) await process.WaitForKernelServingAsync(token);
                var address = new Uri(await process.ListeningAsync(token));
                Require(address.IsLoopback && address.Port != 5095, "the Workbench uses a dedicated loopback listener");
                var session = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
                try
                {
                    if (stock) await WaitForStockShellAsync(session);
                    using var login = await session.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token);
                    Require(login.IsSuccessStatusCode, "the administrator signs in through the normal identity route");
                    return session;
                }
                catch { session.Dispose(); throw; }
            }

            async Task WaitForStockShellAsync(HttpClient session)
            {
                while (true)
                {
                    using var response = await session.GetAsync("/health/ready", token);
                    if (response.IsSuccessStatusCode) return;
                    var body = await response.Content.ReadAsStringAsync(token);
                    Require(response.StatusCode == HttpStatusCode.ServiceUnavailable && !body.Contains("shell_activation_failed", StringComparison.Ordinal),
                        $"the stock Workbench shell becomes ready ({(int)response.StatusCode}: {body}).");
                    await Task.Delay(100, token);
                }
            }

            async Task<StockWorkflowExecution> CheckStockExecutionAsync()
            {
                _ = await ReadAsync("/runtime/workflows/retention");
                Console.WriteLine("ok the configured Actor owner answers the standard retention endpoint");
                using var oldReads = await client.GetAsync("/runtime/core-preview/workflows/instances", token);
                Require(oldReads.StatusCode == HttpStatusCode.NotFound, "the stock Workbench does not map the preview read edge.");

                var body = await NodeAsync("write", "Elsa.Activities.Primitives.Activities.WriteLine", ("text", Literal("stock-workbench-execution")));
                var published = await PublishAsync("Stock Workbench execution", [body], flowchart: false);
                if (!published.Ok)
                    throw new InvalidOperationException($"The stock Workbench refused the authored Sequence: {published.Body}");
                using var receipt = JsonDocument.Parse(published.Body);
                var artifact = receipt.RootElement.GetProperty("artifactId").GetString()!;
                var sourceReferenceId = receipt.RootElement.GetProperty("sourceReferenceId").GetString()!;
                var execution = await ExecuteAsync(client, artifact, sourceReferenceId);
                var testRun = await ReadAsync($"/publishing/workflows/{published.Version}/test-runs",
                    new { acknowledgeUnavailableExpressionValidation = true });
                var testActor = testRun.GetProperty("workflowExecutionId").GetString()!;
                Require(execution != testActor, "published Execute and saved TestRun admit separate runs");
                await ExpectCompletedRunAsync(client, execution, "PublishedRun");
                await ExpectCompletedRunAsync(client, testActor, "TestRun");
                return new(execution, testActor, artifact, sourceReferenceId);
            }

            async Task CheckStockSqliteRestartAsync(StockWorkflowExecution execution, string path)
            {
                Require(File.Exists(path), "the stock SQLite owner persisted execution in its configured file.");
                using var restarted = WorkbenchProcess.StartStock(executable, workspace, path);
                using var session = await OpenClientAsync(restarted);
                var route = $"/runtime/workflows/instances/{Uri.EscapeDataString(execution.PublishedActor)}";
                var old = await ReadAsync(route, session: session);
                var instance = old.GetProperty("instance");
                Require(instance.GetProperty("workflowExecutionId").GetString() == execution.PublishedActor &&
                    instance.GetProperty("status").GetString() == "Completed" &&
                    instance.GetProperty("artifactId").GetString() == execution.ArtifactId &&
                    instance.GetProperty("sourceReferenceId").GetString() == execution.SourceReferenceId,
                    "stock SQLite restarts on the same file and reads the completed Actor with its exact publication.");
                await RequireActivityTimelineAsync(session, route, execution.PublishedActor, token);
                var rerun = await ExecuteAsync(session, execution.ArtifactId, execution.SourceReferenceId);
                Require(rerun != execution.PublishedActor, "stock SQLite serves the restored publication after restart.");
                await ExpectCompletedRunAsync(session, rerun, "PublishedRun");
                await restarted.StopAsync();
            }

            async Task CheckStockPostgresAsync(StockWorkflowExecution execution)
            {
                await using var actor = new NpgsqlConnection(WorkbenchProcess.StockPostgresDatabase("KERNEL_WORKBENCH_ACTOR_POSTGRES").ConnectionString);
                await actor.OpenAsync(token);
                await using var roots = new NpgsqlCommand("""SELECT count(*) FROM "Instances" WHERE "Id" = ANY (@actors)""", actor);
                roots.Parameters.AddWithValue("actors", new[] { execution.PublishedActor, execution.TestActor });
                Require(await roots.ExecuteScalarAsync(token) is 2L,
                    "the configured PostgreSQL Actor database contains both exact hosted workflow executions.");

                await using var runtime = new NpgsqlConnection(WorkbenchProcess.StockPostgresDatabase("KERNEL_WORKBENCH_RUNTIME_POSTGRES").ConnectionString);
                await runtime.OpenAsync(token);
                await using var migrations = new NpgsqlCommand("""SELECT EXISTS (SELECT 1 FROM "__EFMigrationsHistory_ElsaRuntime")""", runtime);
                Require(await migrations.ExecuteScalarAsync(token) is true,
                    "the independently configured PostgreSQL Runtime database has the applied Runtime EF migrations.");
            }

            async Task<string> ExecuteAsync(HttpClient session, string artifact, string sourceReferenceId)
            {
                var receipt = await ReadAsync($"/runtime/workflows/executables/{Uri.EscapeDataString(artifact)}/execute", new { sourceReferenceId }, session);
                return receipt.GetProperty("workflowExecutionId").GetString()!;
            }

            async Task ExpectCompletedRunAsync(HttpClient session, string actor, string kind)
            {
                while (true)
                {
                    using var response = await session.GetAsync($"/runtime/workflows/instances/{Uri.EscapeDataString(actor)}", token);
                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        var body = await response.Content.ReadAsStringAsync(token);
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException($"The stock {kind} details answered {(int)response.StatusCode}: {body}");
                        using var detail = JsonDocument.Parse(body);
                        var instance = detail.RootElement.GetProperty("instance");
                        var status = instance.GetProperty("status").GetString();
                        Require(instance.GetProperty("workflowExecutionId").GetString() == actor && instance.GetProperty("runKind").GetString() == kind,
                            $"the standard details show the exact admitted stock {kind}.");
                        if (status == "Completed") return;
                        Require(status == "Running", $"the stock {kind} completes (current status {status}).");
                    }
                    await Task.Delay(100, token);
                }
            }

            Task<HttpResponseMessage> PostAsync(string kind, string order, string action) =>
                client.PostAsync($"/workflows/http/{kind}/orders/{Uri.EscapeDataString(order)}/{action}", null, token);

            async Task<string> StartAsync(string kind, string order)
            {
                using var reply = await PostAsync(kind, order, "ship");
                Require(reply.StatusCode == HttpStatusCode.Accepted && await reply.Content.ReadAsStringAsync(token) == order,
                    $"{kind}: the authored start returns 202 and sees the exact decoded route value.");
                return reply.Headers.GetValues(KernelHttpEdge.WorkflowInstanceIdHeader).Single();
            }

            async Task ResumeAsync(string kind, string order)
            {
                using var reply = await PostAsync(kind, order, "cancel");
                Require(reply.StatusCode == HttpStatusCode.OK && await reply.Content.ReadAsStringAsync(token) == order,
                    $"{kind}: the held resume sees the exact decoded value '{order}'.");
            }

            async Task ExpectStateAsync(string run, string expected)
            {
                var details = await ReadAsync($"/runtime/workflows/instances/{Uri.EscapeDataString(run)}");
                Require(details.GetProperty("instance").GetProperty("status").GetString() == expected,
                    $"the exact admitted run {run} is {expected} in standard instance details");
            }

            Task<object> EndpointAsync(string id, string path, bool starts, object? values = null)
            {
                List<(string, object)> inputs = [("Path", Literal(path)), ("CanStartWorkflow", Literal(starts)),
                    ("SupportedMethods", Literal(new[] { "POST" }))];
                if (values is not null) inputs.Add(("RouteValues", values));
                return NodeAsync(id, "Elsa.Activities.Http.Activities.HttpEndpoint", inputs.ToArray());
            }

            async Task<(bool Ok, string Body, string Version)> PublishAsync(string name, object[] activities, bool flowchart)
            {
                string type = flowchart ? "Elsa.Activities.Flowchart.Activities.Flowchart" : "Elsa.Activities.Sequence.Activities.Sequence";
                string kind = flowchart ? "elsa.flowchart.structure" : "elsa.sequence.structure";
                object payload = flowchart
                    ? new { activities, startNodeId = "start", connections = activities.Length == 2
                        ? new[] { Edge("start", "accepted") }
                        : new[] { Edge("start", "accepted"), Edge("accepted", "wait"), Edge("wait", "cancelled") } }
                    : new { activities };
                var root = new { nodeId = "root", activityVersionId = await CatalogueVersionAsync(type), outputs = Array.Empty<object>(),
                    inputs = Array.Empty<object>(), structure = new { kind, schemaVersion = "1.0.0", payload } };
                var submitted = await ReadAsync("/design/workflows/definitions/submit", new
                {
                    name, state = new { rootActivity = root, variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
                });
                var version = submitted.GetProperty("version").GetProperty("id").GetString()!;
                using var reply = await client.PostAsJsonAsync($"/publishing/workflows/{Uri.EscapeDataString(version)}/publish", new { }, token);
                return (reply.IsSuccessStatusCode, await reply.Content.ReadAsStringAsync(token), version);
            }

            async Task<object> NodeAsync(string id, string type, params (string Key, object Value)[] bindings) => new
            {
                nodeId = id, activityVersionId = await CatalogueVersionAsync(type), outputs = Array.Empty<object>(),
                inputs = bindings.Select(binding => new { referenceKey = binding.Key, value = binding.Value })
            };

            async Task<string> CatalogueVersionAsync(string type)
            {
                var catalogue = await ReadAsync($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
                return catalogue.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("definition"))
                    .Single(row => row.GetProperty("activityTypeKey").GetString() == type).GetProperty("headVersionId").GetString()!;
            }

            async Task<JsonElement> ReadAsync(string path, object? data = null, HttpClient? session = null)
            {
                session ??= client;
                using var response = data is null ? await session.GetAsync(path, token) : await session.PostAsJsonAsync(path, data, token);
                var body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {body}");
                using var json = JsonDocument.Parse(body);
                return json.RootElement.Clone();
            }
        }
        finally { workspace.Delete(recursive: true); }

        static object Literal(object value) => new { expressionType = "Literal", value };
        static object RouteData(string producerNodeId) => new
        {
            expressionType = "ActivityResult", value = new { producerNodeId, projectionKey = "RouteData", isOptional = false }
        };
        static object BodyFrom(string producerNodeId) => new
        {
            expressionType = "JavaScript", value = new
            {
                language = "JavaScript", source = "args.route.id", resultType = new { alias = "System.Text.Json.JsonElement?", collectionKind = "single" },
                options = new { }, capabilityProfile = "binding-pure-v1",
                parameters = new { route = new { kind = "ActivityResult", producerNodeId, projectionKey = "RouteData", isOptional = false } }
            }
        };
        static object Edge(string from, string to) => new { id = $"{from}-{to}", source = new { nodeId = from, port = "Done" }, target = new { nodeId = to } };
    }

    private sealed record StockWorkflowExecution(string PublishedActor, string TestActor, string ArtifactId, string SourceReferenceId);
}
