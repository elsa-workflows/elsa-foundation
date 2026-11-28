using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task RunShippedChildrenAsync(string executable, bool postgres, bool stock)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-children-");
        var sqlite = postgres ? null : Path.Combine(workspace.FullName, "kernel.sqlite");
        var connectionKey = stock ? "KERNEL_WORKBENCH_ACTOR_POSTGRES" : "KERNEL_PREVIEW_POSTGRES";
        var connection = postgres
            ? Environment.GetEnvironmentVariable(connectionKey) ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : "";
        try
        {
            using var host = stock
                ? WorkbenchProcess.StartStock(executable, workspace, sqlite)
                : WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null, sqlite, workspace);
            await host.WaitReadyAsync(token);
            if (!stock)
                await host.WaitForKernelServingAsync(token);
            var address = new Uri(await host.ListeningAsync(token));
            Require(address.IsLoopback && address.Port != 5095, "the Workbench uses a dedicated loopback listener");
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
            using (var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token))
                Require(login.IsSuccessStatusCode, "the administrator signs in through the normal identity route");

            var attached = await PublishChildAsync("awaited-child", "awaited output", "00:00:00.250");
            var detached = await PublishChildAsync("detached-child", "detached output", "00:00:08");
            await PublishParentAsync("awaited-parent", attached, wait: true, flowchart: false);
            await PublishParentAsync("detached-parent", detached, wait: false, flowchart: true);

            var parent = await RunParentAsync("awaited-parent");
            var child = await FindChildAsync(attached);
            Require(child.Status == "Completed", "the Native parent replies after the attached child finishes");
            await RequireChildOutputAsync(child.Ref, "awaited output");

            var detachedParent = await RunParentAsync("detached-parent");
            var detachedChild = await FindChildAsync(detached);
            await WaitForFinishedAsync(detachedParent);
            Require(detachedChild.Status == "Running", "the Flowchart parent finishes while the detached child is still waiting");
            await WaitForFinishedAsync(detachedChild.Ref);
            await RequireChildOutputAsync(detachedChild.Ref, "detached output");
            await host.StopAsync();

            // The provider inspection runs after Workbench releases its database file.
            var options = postgres ? new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options : null;
            var reads = postgres
                ? await new EfStore(() => PostgresPreviewHosting.CreateShippedContext(options!), postgres: true)
                    .OpenPreviewRootReadsAsync(cancellationToken: token)
                : SqliteEmbeddedHosting.CreateFoundationReads(sqlite!);
            var tenant = EmbeddedTenant.Bind("default");
            var parentView = await reads.GetAsync(tenant, parent, token);
            Require(await reads.GetAsync(tenant, child.Ref, token) is
                { Header: { Parent: { } recordedParent, EndedAt: { } childEnded } } &&
                recordedParent == parent && childEnded <= parentView?.Header.EndedAt &&
                await reads.GetAsync(tenant, detachedChild.Ref, token) is { Header.Parent: null },
                "the actor read model retains the exact attached parent and keeps the detached child independent");

            async Task<(ActorRef Ref, string? Status)> FindChildAsync(string definition)
            {
                while (true)
                {
                    var page = await ReadAsync($"/runtime/workflows/instances/page?definitionId={Uri.EscapeDataString(definition)}&take=10");
                    var items = page.GetProperty("items");
                    if (items.GetArrayLength() > 0)
                    {
                        var item = items.EnumerateArray().Single();
                        return (new(item.GetProperty("workflowExecutionId").GetString()!), item.GetProperty("status").GetString());
                    }
                    await Task.Delay(50, token);
                }
            }

            async Task WaitForFinishedAsync(ActorRef run)
            {
                while (true)
                {
                    var detail = await ReadAsync(RunPath(run));
                    var status = detail.GetProperty("instance").GetProperty("status").GetString();
                    if (status == "Completed") return;
                    if (status is "Cancelled" or "Faulted")
                        throw new InvalidOperationException($"Run {run.Id} ended as {status}.");
                    await Task.Delay(50, token);
                }
            }

            async Task RequireChildOutputAsync(ActorRef child, string expected)
            {
                var detail = await ReadAsync(RunPath(child));
                Require(detail.GetProperty("instance").GetProperty("status").GetString() == "Completed" &&
                    detail.GetProperty("outputs").GetProperty("Result").GetProperty("value").GetString() == expected,
                    "the standard run details read gives the published child's committed output");
            }

            async Task<ActorRef> RunParentAsync(string name)
            {
                while (true)
                {
                    using var response = await client.PostAsJsonAsync($"/workflows/http/core-preview/{name}", new { }, token);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }
                    var body = await response.Content.ReadAsStringAsync(token);
                    Require(response.StatusCode == HttpStatusCode.OK && body == name, $"the published {name} answers through the normal HTTP route ({(int)response.StatusCode}: {body})");
                    return new(response.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single());
                }
            }

            async Task<string> PublishChildAsync(string name, string output, string delay)
            {
                var wait = await NodeAsync("delay", "Elsa.Activities.Scheduling.Activities.Delay", new() { ["Duration"] = delay });
                var set = new
                {
                    nodeId = "result", activityVersionId = "elsa.intrinsic.set-output@1", outputs = Array.Empty<object>(),
                    intrinsic = new { kind = "setOutput", valueType = new { alias = "String", collectionKind = "single" } },
                    inputs = new[] { new { referenceKey = "name", value = Literal("Result") }, new { referenceKey = "value", value = Literal(output) } }
                };
                return await PublishAsync(name, [wait, set], flowchart: false);
            }

            async Task PublishParentAsync(string name, string child, bool wait, bool flowchart)
            {
                var start = await NodeAsync("start", "Elsa.Activities.Http.Activities.HttpEndpoint", new()
                    { ["Path"] = $"core-preview/{name}", ["CanStartWorkflow"] = true, ["SupportedMethods"] = new[] { "POST" } });
                var dispatch = await NodeAsync("dispatch", "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow", new()
                    { ["WorkflowDefinitionId"] = child, ["WaitForCompletion"] = wait });
                var answer = await NodeAsync("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse", new() { ["StatusCode"] = 200, ["Body"] = name });
                await PublishAsync(name, [start, dispatch, answer], flowchart);
            }

            async Task<string> PublishAsync(string name, object[] activities, bool flowchart)
            {
                var type = flowchart ? "Elsa.Activities.Flowchart.Activities.Flowchart" : "Elsa.Activities.Sequence.Activities.Sequence";
                var kind = flowchart ? "elsa.flowchart.structure" : "elsa.sequence.structure";
                object payload = flowchart
                    ? new { activities, startNodeId = "start", connections = new[] { Edge("start", "dispatch", "Done"), Edge("dispatch", "answer", "Dispatched") } }
                    : new { activities };
                var root = await NodeAsync("root", type, [], new { kind, schemaVersion = "1.0.0", payload });
                var submitted = await ReadAsync("/design/workflows/definitions/submit", new
                {
                    name, state = new { rootActivity = root, variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
                });
                var version = submitted.GetProperty("version").GetProperty("id").GetString()!;
                await ReadAsync($"/publishing/workflows/{Uri.EscapeDataString(version)}/publish", new { });
                return submitted.GetProperty("definition").GetProperty("id").GetString()!;
            }

            async Task<object> NodeAsync(string id, string kind, Dictionary<string, object> values, object? structure = null)
            {
                var catalog = await ReadAsync($"/design/activities/definitions?search={Uri.EscapeDataString(kind)}");
                var version = catalog.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("definition"))
                    .Single(row => row.GetProperty("activityTypeKey").GetString() == kind).GetProperty("headVersionId").GetString();
                return new
                {
                    nodeId = id, activityVersionId = version, structure, outputs = Array.Empty<object>(),
                    inputs = values.Select(pair => new { referenceKey = pair.Key, value = Literal(pair.Value) })
                };
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
        static string RunPath(ActorRef run) => $"/runtime/workflows/instances/{Uri.EscapeDataString(run.Id)}";
        static object Edge(string from, string to, string port) =>
            new { id = $"{from}-{to}", source = new { nodeId = from, port }, target = new { nodeId = to } };
    }
}
