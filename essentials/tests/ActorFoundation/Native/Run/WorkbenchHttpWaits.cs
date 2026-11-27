using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Workflows.Runtime.KernelHost;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task RunHttpWaitsAsync(string executable, bool postgres, bool gated)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-http-waits-");
        var sqlite = postgres ? null : Path.Combine(workspace.FullName, "kernel.sqlite");
        var connection = postgres
            ? Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : "";
        try
        {
            using var host = WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null, sqlite, workspace);
            await host.WaitReadyAsync(token);
            await host.WaitForKernelServingAsync(token);
            var address = new Uri(await host.ListeningAsync(token));
            Require(address.IsLoopback && address.Port != 5095, "the Workbench uses a dedicated loopback listener");
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
            using (var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token))
                Require(login.IsSuccessStatusCode, "the administrator signs in through the normal identity route");

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

            async Task<(bool Ok, string Body)> PublishAsync(string name, object[] activities, bool flowchart)
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
                return (reply.IsSuccessStatusCode, await reply.Content.ReadAsStringAsync(token));
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

            async Task<JsonElement> ReadAsync(string path, object? data = null)
            {
                using var response = data is null ? await client.GetAsync(path, token) : await client.PostAsJsonAsync(path, data, token);
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
}
