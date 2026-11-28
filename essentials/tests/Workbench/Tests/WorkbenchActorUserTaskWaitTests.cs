using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

public sealed class WorkbenchActorUserTaskWaitTests
{
    private const string Qualification = "CShells:Shells:default:Features:WorkflowsRuntimeKernelHosting:QualifyActorUserTaskWaits";

    [Fact]
    public Task Actor_task_resumes_the_exact_form_in_a_reopened_stock_workbench() =>
        QualifyAsync(() => WorkbenchProcess.StartAsync(WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string>(WorkbenchShell.Development.Settings)
            {
                [Qualification] = "true"
            }
        }));

    [SkippableFact]
    public async Task Actor_task_reopens_on_the_postgresql_compose_profile()
    {
        var platform = Environment.GetEnvironmentVariable("ELSA_WORKBENCH_USER_TASKS_PLATFORM_POSTGRES");
        var actor = Environment.GetEnvironmentVariable("ELSA_WORKBENCH_USER_TASKS_ACTOR_POSTGRES");
        Skip.If(string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(actor),
            "Supply two disposable PostgreSQL databases for the platform and Actor stores.");
        Assert.NotEqual(platform, actor);
        var shell = WorkbenchShell.Development with
        {
            Settings = new Dictionary<string, string>(WorkbenchShell.Development.Settings)
            {
                [Qualification] = "true",
                ["ConnectionStrings:Elsa"] = platform!,
                ["ConnectionStrings:ElsaOpenTelemetry"] = platform!,
                ["ConnectionStrings:ElsaElsa3Import"] = platform!,
                ["CShells:Shells:default:Features:WorkflowsRuntimeKernelHosting:PostgresConnectionString"] = actor!,
                ["CShells:Shells:default:Features:FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminUserName"] = "admin",
                ["CShells:Shells:default:Features:FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword"] = "Password123!"
            }
        };
        await QualifyAsync(() => WorkbenchProcess.StartAsync(shell, directory =>
            File.Copy(Path.Combine(WorkbenchBuild.RepositoryRoot, "docker/compose/elsa-workbench.shells.json"),
                Path.Combine(directory, "shells.json"), overwrite: true)));
    }

    internal static async Task QualifyAsync(Func<Task<WorkbenchProcess>> start)
    {
        const string formId = "purchase-approval";
        const string title = "Review the purchase";
        const string note = "Verified against the pinned form";
        await using var host = await start();
        using var client = await SignInAsync(host);
        var identity = await ReadAsync(client, "/runtime/user-task-identity");
        var reviewer = (string)identity["userId"]!;
        var formRoute = $"/runtime/user-task-forms/{formId}";
        await PublishFormAsync(client, formRoute, formId, "note", "Review note");

        var taskVersion = await ActivityVersionAsync(client, "Elsa.Activities.Primitives.Activities.CreateUserTask");
        var flowchartVersion = await ActivityVersionAsync(client, "Elsa.Activities.Flowchart.Activities.Flowchart");
        var definition = await PostAsync(client, "/design/workflows/definitions/submit",
            new { name = "Actor purchase approval", state = PurchaseWorkflow(taskVersion, flowchartVersion, reviewer, title, formId) });
        var versionId = (string)definition["version"]!["id"]!;
        var publication = await PostAsync(client, $"/publishing/workflows/{versionId}/publish", new { });
        var artifactId = (string)publication["artifactId"]!;
        var sourceReferenceId = (string)publication["sourceReferenceId"]!;
        var dispatch = await PostAsync(client, $"/runtime/workflows/executables/{artifactId}/execute", new { sourceReferenceId });
        var runId = (string)dispatch["workflowExecutionId"]!;
        var runPath = $"/runtime/workflows/instances/{runId}";

        var task = await EventuallyAsync(async () =>
        {
            var inbox = await ReadAsync(client, "/runtime/user-tasks?view=mine");
            var matching = inbox["items"]!.AsArray().Where(item => (string?)item?["workflowExecutionId"] == runId).ToArray();
            return matching.Length == 0 ? null : Assert.Single(matching);
        }, $"open task for run {runId}");
        Assert.Equal("Open", (string?)task["status"]);
        Assert.Equal(title, (string?)task["title"]);
        Assert.Equal(reviewer, (string?)task["assigneeId"]);
        Assert.Equal("1", (string?)task["form"]?["version"]);
        var taskId = (string)task["id"]!;
        var taskPath = $"/runtime/user-tasks/{taskId}";

        await PublishFormAsync(client, formRoute, formId, "reference", "Required replacement field");
        Assert.Equal("2", (string?)(await ReadAsync(client, $"{formRoute}/definition"))["latestPublished"]?["form"]?["version"]);

        await using var reopened = await host.CrashAndRestartAsync();
        using var restartedClient = await SignInAsync(reopened);
        var pending = await ReadAsync(restartedClient, taskPath);
        Assert.Equal("Open", (string?)pending["status"]);
        Assert.Equal(runId, (string?)pending["workflowExecutionId"]);
        Assert.Equal("1", (string?)pending["form"]?["version"]);
        Assert.Equal("note", (string?)pending["form"]?["fields"]?[0]?["key"]);
        Assert.Equal("Review note", (string?)pending["form"]?["fields"]?[0]?["label"]);

        var revision = (long)pending["revision"]!;
        using (var invalid = await restartedClient.PostAsJsonAsync($"{taskPath}/complete",
                   new { revision, requestId = "missing-note", action = "Approve", data = new { } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var response = new { revision, requestId = "review-response", action = "Approve", data = new { note } };
        var accepted = await PostAsync(restartedClient, $"{taskPath}/complete", response);
        var replayed = await PostAsync(restartedClient, $"{taskPath}/complete", response);
        var completionId = (string?)accepted["decision"]?["response"]?["completionId"];
        Assert.False(string.IsNullOrWhiteSpace(completionId));
        Assert.Equal(completionId, (string?)replayed["decision"]?["response"]?["completionId"]);

        var completed = await EventuallyAsync(async () =>
        {
            var run = await ReadAsync(restartedClient, runPath);
            var status = (string?)run["instance"]?["status"];
            Assert.DoesNotContain(status, new[] { "Faulted", "Cancelled" });
            return status == "Completed" ? run : null;
        }, $"completed run {runId}");
        Assert.Equal(runId, (string?)completed["instance"]?["workflowExecutionId"]);
        Assert.Equal(artifactId, (string?)completed["instance"]?["artifactId"]);
        Assert.Equal(sourceReferenceId, (string?)completed["instance"]?["sourceReferenceId"]);
        Assert.Equal(versionId, (string?)completed["instance"]?["definitionVersionId"]);
        Assert.Equal(taskId, (string?)ReadOutput(completed, "TaskId"));
        Assert.Equal("Approve", (string?)ReadOutput(completed, "Action"));
        Assert.Equal("Approve", (string?)ReadOutput(completed, "Branch"));
        Assert.Equal(note, (string?)ReadOutput(completed, "Data")?["note"]);

        var history = await ReadAsync(restartedClient, $"{runPath}/history?name=ActivityCompleted&limit=50");
        Assert.Equal("kernel-activity-journal-v1", (string?)history["coverage"]?["policyId"]);
        Assert.True((bool?)history["scopeComplete"]);
        var committed = history["items"]!.AsArray();
        Assert.All(committed, entry =>
        {
            Assert.Equal(runId, (string?)entry?["workflowExecutionId"]);
            Assert.Equal("CommittedTransition", (string?)entry?["authority"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)entry?["physicalCommitId"]));
        });
        Assert.Single(committed, entry => (string?)entry?["executableNodeId"] == "review");
        Assert.Single(committed, entry => (string?)entry?["executableNodeId"] == "branch-approve");
        Assert.DoesNotContain(committed, entry => (string?)entry?["executableNodeId"] == "branch-reject");

        var settled = await EventuallyAsync(async () =>
        {
            var detail = await ReadAsync(restartedClient, taskPath);
            return (string?)detail["status"] == "Completed" ? detail : null;
        }, $"settled task {taskId}");
        Assert.Equal(completionId, (string?)settled["decision"]?["response"]?["completionId"]);
        Assert.Equal(note, (string?)settled["decision"]?["response"]?["data"]?["note"]);
        Assert.Single(settled["history"]!.AsArray(), entry => (string?)entry?["operation"] == "accepted");
        var completion = Assert.Single(settled["history"]!.AsArray(), entry => (string?)entry?["operation"] == "completed");
        Assert.Equal(reviewer, (string?)completion!["actorId"]);
    }

    private static object PurchaseWorkflow(string taskVersion, string flowchartVersion, string reviewer, string title, string formId)
    {
        var activities = new[]
        {
            Node("review", taskVersion,
            [
                Literal("Title", title), Literal("AssigneeId", reviewer),
                Literal("Actions", new[] { "Approve", "Reject" }), Literal("FormId", formId), Literal("FormVersion", "1")
            ]),
            SetOutput("branch-approve", "Branch", "String", Literal("value", "Approve")),
            SetOutput("branch-reject", "Branch", "String", Literal("value", "Reject")),
            SetOutput("task-id", "TaskId", "String", TaskResult("TaskId")),
            SetOutput("action", "Action", "String", TaskResult("Action")),
            SetOutput("data", "Data", "System.Text.Json.JsonElement", TaskResult("Data"))
        };
        var connections = new[]
        {
            Edge("review", "Approve", "branch-approve"), Edge("review", "Reject", "branch-reject"),
            Edge("branch-approve", "Done", "task-id"), Edge("branch-reject", "Done", "task-id"),
            Edge("task-id", "Done", "action"), Edge("action", "Done", "data")
        };
        return new
        {
            variables = Array.Empty<object>(),
            inputs = Array.Empty<object>(),
            outputs = new[]
            {
                Output("Branch", "String"), Output("TaskId", "String"), Output("Action", "String"), Output("Data", "System.Text.Json.JsonElement")
            },
            rootActivity = new
            {
                nodeId = "purchase",
                activityVersionId = flowchartVersion,
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                structure = new
                {
                    kind = "elsa.flowchart.structure",
                    schemaVersion = "1.0.0",
                    payload = new { activities, connections, startNodeId = "review", nodeMetadata = new { }, connectionMetadata = new { } }
                }
            }
        };
    }

    private static object Node(string nodeId, string activityVersionId, object[] inputs) =>
        new { nodeId, activityVersionId, inputs, outputs = Array.Empty<object>() };

    private static object Literal(string referenceKey, object value) =>
        new { referenceKey, value = new { expressionType = "Literal", value } };

    private static object TaskResult(string projectionKey) =>
        new
        {
            referenceKey = "value",
            value = new { expressionType = "ActivityResult", value = new { producerNodeId = "review", projectionKey, isOptional = false } }
        };

    private static object Output(string name, string alias) =>
        new { referenceKey = name, name, type = new { alias, collectionKind = "single" }, isNullable = false };

    private static object SetOutput(string nodeId, string name, string alias, object value) =>
        new
        {
            nodeId,
            activityVersionId = "elsa.intrinsic.set-output@1",
            intrinsic = new { kind = "setOutput", valueType = new { alias, collectionKind = "single" } },
            inputs = new[] { Literal("name", name), value },
            outputs = Array.Empty<object>()
        };

    private static object Edge(string from, string port, string to) =>
        new { id = $"{from}-{port}-{to}", source = new { nodeId = from, port }, target = new { nodeId = to } };

    private static async Task PublishFormAsync(HttpClient client, string route, string id, string field, string label)
    {
        var current = await ReadOptionalAsync(client, $"{route}/definition");
        var revision = (long?)current?["revision"] ?? 0;
        var form = new
        {
            id, version = "$draft", name = "Purchase response", provider = "elsa.basic-forms/1",
            fields = new[] { new { key = field, label, kind = "text", required = true } }
        };
        var draft = await PostAsync(client, $"{route}/draft", new { revision, form });
        await PostAsync(client, $"{route}/publish", new { revision = (long)draft["revision"]!, form });
    }

    private static async Task<string> ActivityVersionAsync(HttpClient client, string key)
    {
        var catalogue = await ReadAsync(client, $"/design/activities/definitions?search={Uri.EscapeDataString(key)}");
        var item = Assert.Single(catalogue["items"]!.AsArray(), entry => (string?)entry?["definition"]?["activityTypeKey"] == key);
        return (string)item!["definition"]!["headVersionId"]!;
    }

    private static JsonNode? ReadOutput(JsonNode run, string name) => run["outputs"]?[name]?["value"];

    private static async Task<HttpClient> SignInAsync(WorkbenchProcess host)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = host.Client.BaseAddress };
        try
        {
            using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
            login.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<JsonNode> EventuallyAsync(Func<Task<JsonNode?>> read, string observation)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            if (await read() is { } found)
                return found;
            await Task.Delay(250);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(30));
        throw new TimeoutException($"Timed out waiting for the {observation}.");
    }

    private static async Task<JsonNode> ReadAsync(HttpClient client, string path) =>
        await ReadOptionalAsync(client, path) ?? throw new InvalidOperationException($"GET {path} returned 404.");

    private static async Task<JsonNode?> ReadOptionalAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ReadResponseAsync(response);
    }

    private static async Task<JsonNode> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ReadResponseAsync(response);
    }

    private static async Task<JsonNode> ReadResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {body}");
        return JsonNode.Parse(body) ?? throw new InvalidOperationException("The Workbench response body was empty.");
    }
}
