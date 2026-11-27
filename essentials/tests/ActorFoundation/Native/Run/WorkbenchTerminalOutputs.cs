using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ContractKinds = Kernel.Foundation.Contracts.Kinds;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    private static void CheckEncodingBudget()
    {
        const int limit = 64 * 1024;
        var split = new string('a', 4095) + "💠" + "\n";
        var within = new JsonObject
        {
            ["Result"] = new JsonArray(JsonValue.Create(split), JsonValue.Create(10), (JsonNode?)null)
        };
        var complete = TerminalOutputEncoding.Capture(within, limit);
        using var restored = JsonDocument.Parse(complete.Value!);
        Require(complete.Names == """["Result"]""" && restored.RootElement.GetProperty("Result")[0].GetString() == split &&
            restored.RootElement.GetProperty("Result")[1].GetInt32() == 10 &&
            restored.RootElement.GetProperty("Result")[2].ValueKind == JsonValueKind.Null,
            "bounded encoding preserves a Unicode scalar across chunks and exact JSON types");

        var escapedProperty = new JsonObject { ["\n💠"] = 7 };
        var encodedProperty = TerminalOutputEncoding.Capture(escapedProperty, limit).Value!;
        Require(TerminalOutputEncoding.Capture(escapedProperty, encodedProperty.Length).Value is { } exact &&
            exact.SequenceEqual(encodedProperty) && TerminalOutputEncoding.Capture(escapedProperty, encodedProperty.Length - 1).Value is null,
            "the output byte cap accepts its exact boundary and refuses one byte less");

        var large = new string('x', 2 * 1024 * 1024);
        using var parsed = JsonDocument.Parse($$"""{"large":"{{large}}"}""");
        var strings = new JsonObject { ["Result"] = large };
        var raw = new JsonObject { ["Result"] = JsonValue.Create(parsed.RootElement.GetProperty("large")) };
        var names = new JsonObject { [large] = "sample" };
        var nestedName = new JsonObject { ["Result"] = new JsonObject { [new string('Ω', limit / 2)] = 5 } };
        var start = GC.GetAllocatedBytesForCurrentThread();
        var streamed = TerminalOutputEncoding.Capture(strings, limit);
        var quoted = TerminalOutputEncoding.Capture(raw, limit);
        var named = TerminalOutputEncoding.Capture(names, limit);
        var nested = TerminalOutputEncoding.Capture(nestedName, limit);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Require(streamed is { Names: """["Result"]""", Value: null } && quoted is { Names: """["Result"]""", Value: null } &&
            named is { Names: "[]", Value: null } && nested is { Names: """["Result"]""", Value: null } &&
            allocated < 2 * 1024 * 1024,
            $"oversized managed strings, parsed tokens and output names are omitted before allocating a full body ({allocated} allocation bytes)");
    }

    internal static async Task RunTerminalOutputAsync(string executable, bool postgres, bool withTimers)
    {
        CheckEncodingBudget();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("elsa-workbench-terminal-output-");
        var sqlite = postgres ? null : Path.Combine(directory.FullName, "kernel.sqlite");
        var format = withTimers ? StoreFormat.RootS6TerminalOutputQualification : StoreFormat.RootTerminalOutputQualification;
        var connection = postgres
            ? Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : new SqliteConnectionStringBuilder { DataSource = sqlite, ForeignKeys = true, Pooling = false }.ToString();
        var options = postgres ? new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options
            : new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection).Options;
        var definitions = new SqliteAdmissionDefinitions();
        var scope = EmbeddedTenant.Bind("default");
        const string nativePart = "terminal-native-private-value";
        const string flowPart = "terminal-flowchart-private-value";
        var large = new string('x', 6 * 1024 * 1024) + "Ω";
        var nativeValue = new { message = nativePart, optional = (string?)null, nested = new[] { 1, 2 }, content = large };
        var flowValue = new { message = flowPart, optional = (string?)null, nested = new[] { 3, 4 } };
        try
        {
            if (sqlite is not null && !withTimers &&
                Environment.GetEnvironmentVariable("KERNEL_PREVIEW_TERMINAL_OUTPUT_SQLITE_PREINSTALLED") is { Length: > 0 } priorFile)
                File.Copy(priorFile, sqlite);

            string nativeRun;
            string flowRun;
            using (var host = Start())
            {
                await host.WaitReadyAsync(token);
                await host.WaitForKernelServingAsync(token);
                using var client = await AuthorizeAsync(host);
                await PublishAsync(client, "terminal-output-native", nativeValue, flowchart: false);
                await PublishAsync(client, "terminal-output-flowchart", flowValue, flowchart: true);
                nativeRun = await InvokeAsync(client, "/workflows/http/core-preview/terminal-output-native", new { }, HttpStatusCode.Created, "result ready");
                if (withTimers)
                {
                    var waiting = await ReadJsonAsync(client, RunPath(nativeRun));
                    Require(waiting.GetProperty("instance").GetProperty("status").GetString() == "Running" &&
                        !waiting.GetProperty("outputs").EnumerateObject().Any(),
                        "the published Native run has no retained result while its committed Delay is waiting");
                }
                flowRun = await InvokeAsync(client, "/workflows/http/core-preview/terminal-output-flowchart", new { }, HttpStatusCode.Created, "result ready");
                await RequireResultAsync(client, nativeRun, nativePart, [1, 2], large);
                await RequireResultAsync(client, flowRun, flowPart, [3, 4]);
                const string hostProbeValue = "host-probe-fails-closed";
                var hostProbe = await InvokeAsync(client, "/workflows/http/core-preview/probe", new { name = "Ada", message = hostProbeValue },
                    HttpStatusCode.OK, "Ada");
                var redacted = await ReadJsonAsync(client, RunPath(hostProbe));
                var redactedResult = redacted.GetProperty("outputs").GetProperty("Result");
                Require(redactedResult.GetProperty("isRedacted").GetBoolean() &&
                    redactedResult.GetProperty("value").ValueKind == JsonValueKind.Null &&
                    !redacted.GetRawText().Contains(hostProbeValue, StringComparison.Ordinal),
                    "the stock decision policy fails closed for a run with no pinned artifact, retaining the output name only");
                using var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = client.BaseAddress };
                using var denied = await anonymous.GetAsync(RunPath(flowRun), token);
                Require(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden &&
                    !(await denied.Content.ReadAsStringAsync(token)).Contains(flowPart, StringComparison.Ordinal),
                    "an anonymous request cannot retrieve the exact run's terminal value");
                await host.StopAsync();
            }

            var reader = postgres
                ? await new EfStore(Open, postgres: true).OpenPreviewRootReadsAsync(cancellationToken: token)
                : SqliteEmbeddedHosting.CreateFoundationReads(sqlite!, terminalOutputFormat: format);
            var output = (IActorTerminalOutputReads)reader;
            var point = await reader.GetAsync(scope, new(flowRun), token);
            var endedAt = point!.Header.EndedAt!.Value;
            Require(await output.ReadTerminalOutputAsync(scope, new(flowRun), endedAt, false, token)
                    is { Names: """["Result"]""", Omitted: false, Values: null } &&
                await output.ReadTerminalOutputAsync(ContractScopes.Bind("other"), new(flowRun), endedAt, true, token) is null &&
                await output.ReadTerminalOutputAsync(scope, new(flowRun), endedAt - 1, true, token) is null,
                "metadata-only reads return the authored name without value bytes, and another scope or end is a miss");
            var tail = await reader.TailAsync(scope, new(flowRun), cancellationToken: token);
            Require(tail.Items.Count > 0 && tail.Items.All(item => !item.Entry.Contains(flowPart, StringComparison.Ordinal)) &&
                    !point.Header.TypeData.Contains(flowPart, StringComparison.Ordinal),
                "the retained output is absent from the ordinary activity journal and actor header");
            await using (var db = Open())
            {
                Require(await db.Database.SqlQuery<bool>($"""
                    SELECT count(*) = 3 AS "Value"
                    FROM "FoundationWorkflowTerminalOutputs" o JOIN "FoundationWorkflowHistory" h USING ("Tenant", "Reference")
                    WHERE o."Revision" = h."Revision" AND o."CapturedAt" = h."UpdatedAt" AND o."Tenant" = {scope.Tenant}
                    """).SingleAsync(token),
                    "Native and Flowchart output rows identify the exact committed workflow history checkpoint");
                if (withTimers)
                    Require(await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "FoundationWorkflowTerminalOutputs"
                            WHERE "Tenant" = {scope.Tenant} AND "Reference" = {nativeRun} AND "Revision" > 1) AS "Value"
                        """).SingleAsync(token),
                        "the Native output belongs to a later committed checkpoint after the authored timer resumes");
            }

            await RefusesFormatAsync(
                () => postgres ? PostgresPreviewHosting.InstallShippedAsync(options, token) : SqliteEmbeddedHosting.InstallShippedAsync(sqlite!, token),
                "the ordinary shipped installer refuses the dedicated terminal-output format");
            await RefusesFormatAsync(
                () => postgres ? PostgresPreviewHosting.InstallTerminalOutputQualificationAsync(options, !withTimers, token)
                    : SqliteEmbeddedFoundation.ForTerminalOutputQualification(sqlite!, "other-format", "workflow", !withTimers).InstallShippedAsync(token),
                "protocols 112 and 113 refuse each other's installed format");

            // Keep the shell's publishing and database state, including its completed retained runs.
            using (var host = Start())
            {
                await host.WaitReadyAsync(token);
                await host.WaitForKernelServingAsync(token);
                using var client = await AuthorizeAsync(host);
                await RequireResultAsync(client, flowRun, flowPart, [3, 4]);

                var selected = point.Selection ?? throw new InvalidOperationException("The completed run has no retirement selection.");
                if (postgres)
                {
                    var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
                    Require(await admission.AdmitRootOperatorAsync(scope.Tenant, flowRun, "terminal-output-delete",
                        ContractKinds.Delete, null, null, token, selected) is not null, "the selected PostgreSQL Delete is admitted");
                }
                else
                {
                    var admission = new SqliteRootAdmission(Open, TimeSpan.FromHours(1), definitions);
                    Require((await admission.OperateAsync(scope.Tenant, new(flowRun), "terminal-output-delete",
                        ContractKinds.Delete, selected, null, null, token)).Outcome == AdmissionOutcome.Accepted, "the selected SQLite Delete is admitted");
                }
                while (true)
                {
                    using var retired = await client.GetAsync(RunPath(flowRun), token);
                    if (retired.StatusCode == HttpStatusCode.NotFound) break;
                    retired.EnsureSuccessStatusCode();
                    await Task.Delay(50, token);
                }
                await RequireResultAsync(client, nativeRun, nativePart, [1, 2], large);
                await host.StopAsync();
            }
            await using (var db = Open())
                Require(await db.Database.SqlQuery<bool>($"""
                    SELECT NOT EXISTS (SELECT 1 FROM "FoundationWorkflowTerminalOutputs"
                        WHERE "Tenant" = {scope.Tenant} AND "Reference" = {flowRun}) AS "Value"
                    """).SingleAsync(token) && await output.ReadTerminalOutputAsync(scope, new(flowRun), endedAt, true, token) is null,
                    "fenced retirement physically removes the selected run's output from retained history and the details API");
        }
        finally { directory.Delete(recursive: true); }

        KernelDb Open() => KernelDb.ForRootTerminalOutputQualification(options, sqliteShippedInstall: !postgres,
            definitions: postgres ? null : definitions, format: format);

        WorkbenchProcess Start() => WorkbenchProcess.Start(executable, connection, enabled: true, "publications",
            releaseFile: null, sqlite, directory, terminalOutputQualification: true, terminalOutputsWithTimers: withTimers);

        async Task RefusesFormatAsync(Func<Task> install, string description)
        {
            try { await install(); }
            catch (Exception error) when ((error is StoreFormatException || error.InnerException is StoreFormatException) &&
                error.Message.Contains($"message protocol={format.MessageProtocol}", StringComparison.Ordinal))
            {
                Require(true, description);
                return;
            }
            throw new InvalidOperationException(description);
        }

        async Task<HttpClient> AuthorizeAsync(WorkbenchProcess host)
        {
            var address = new Uri(await host.ListeningAsync(token));
            Require(address.IsLoopback && address.Port != 5095, "the Workbench fixture uses a dedicated loopback listener");
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
            using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token);
            Require(login.IsSuccessStatusCode, "the seeded administrator signs in");
            return client;
        }

        async Task PublishAsync(HttpClient client, string workflow, object outputValue, bool flowchart)
        {
            const string endpoint = "Elsa.Activities.Http.Activities.HttpEndpoint";
            const string respond = "Elsa.Activities.Http.Activities.WriteHttpResponse";
            var rootType = flowchart ? "Elsa.Activities.Flowchart.Activities.Flowchart" : "Elsa.Activities.Sequence.Activities.Sequence";
            var start = await NodeAsync("start", endpoint, new()
                { ["Path"] = $"core-preview/{workflow}", ["CanStartWorkflow"] = true, ["SupportedMethods"] = new[] { "POST" } });
            var answer = await NodeAsync("answer", respond, new() { ["StatusCode"] = 201, ["Body"] = "result ready" });
            var type = new { alias = "JsonElement", collectionKind = "single" };
            var set = new
            {
                nodeId = "set", activityVersionId = "elsa.intrinsic.set-output@1", outputs = Array.Empty<object>(),
                intrinsic = new { kind = "setOutput", valueType = type },
                inputs = new[] { new { referenceKey = "name", value = Literal("Result") }, new { referenceKey = "value", value = Literal(outputValue) } }
            };
            var activities = withTimers && !flowchart
                ? new[] { start, answer, await NodeAsync("delay", "Elsa.Activities.Scheduling.Activities.Delay",
                    new() { ["Duration"] = "00:00:08" }), set }
                : new[] { start, set, answer };
            object payload = flowchart
                ? new { activities, startNodeId = "start", connections = new[] { Edge("start", "set"), Edge("set", "answer") } }
                : new { activities };
            var root = await NodeAsync("root", rootType, [], new
            {
                kind = flowchart ? "elsa.flowchart.structure" : "elsa.sequence.structure", schemaVersion = "1.0.0", payload
            });
            var submitted = await ReadJsonAsync(client, "/design/workflows/definitions/submit", new
            {
                name = workflow,
                state = new { rootActivity = root, variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
            });
            var version = submitted.GetProperty("version").GetProperty("id").GetString()!;
            await ReadJsonAsync(client, $"/publishing/workflows/{Uri.EscapeDataString(version)}/publish", new { });
            return;

            async Task<object> NodeAsync(string id, string key, Dictionary<string, object> values, object? structure = null)
            {
                var catalog = await ReadJsonAsync(client, $"/design/activities/definitions?search={Uri.EscapeDataString(key)}");
                var versionId = catalog.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("definition"))
                    .Single(row => row.GetProperty("activityTypeKey").GetString() == key).GetProperty("headVersionId").GetString();
                return new { nodeId = id, activityVersionId = versionId, structure, outputs = Array.Empty<object>(),
                    inputs = values.Select(pair => new { referenceKey = pair.Key, value = Literal(pair.Value) }) };
            }

            static object Literal(object value) => new { expressionType = "Literal", value };
            static object Edge(string from, string to) =>
                new { id = $"{from}-{to}", source = new { nodeId = from, port = "Done" }, target = new { nodeId = to } };
        }

        async Task<string> InvokeAsync(HttpClient client, string path, object data, HttpStatusCode expectedStatus, string expectedBody)
        {
            while (true)
            {
                using var reply = await client.PostAsJsonAsync(path, data, token);
                if (reply.StatusCode == HttpStatusCode.NotFound)
                {
                    await Task.Delay(100, token);
                    continue;
                }
                var body = await reply.Content.ReadAsStringAsync(token);
                Require(reply.StatusCode == expectedStatus && body == expectedBody,
                    $"the normal HTTP edge runs {path} with its authored response ({(int)reply.StatusCode}: {body})");
                return reply.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single();
            }
        }

        async Task RequireResultAsync(HttpClient client, string id, string message, int[] nested, string? content = null)
        {
            while (true)
            {
                var detail = await ReadJsonAsync(client, RunPath(id));
                var instance = detail.GetProperty("instance");
                if (instance.GetProperty("status").GetString() != "Completed")
                {
                    await Task.Delay(50, token);
                    continue;
                }
                var result = detail.GetProperty("outputs").GetProperty("Result");
                var value = result.GetProperty("value");
                Require(instance.GetProperty("workflowExecutionId").GetString() == id &&
                    !result.GetProperty("isRedacted").GetBoolean() &&
                    result.GetProperty("capturedAt").GetDateTimeOffset() == instance.GetProperty("completedAt").GetDateTimeOffset() &&
                    value.GetProperty("message").GetString() == message && value.GetProperty("optional").ValueKind == JsonValueKind.Null &&
                    value.GetProperty("nested").EnumerateArray().Select(item => item.GetInt32()).SequenceEqual(nested) &&
                    (content is null || value.GetProperty("content").GetString() == content),
                    $"the authorized standard run details returns the complete original Result JSON ({message}, content chars: {content?.Length ?? 0}) at the exact end with diagnostic capture disabled");
                return;
            }
        }

        async Task<JsonElement> ReadJsonAsync(HttpClient client, string path, object? data = null)
        {
            using var response = data is null ? await client.GetAsync(path, token) : await client.PostAsJsonAsync(path, data, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }

        static string RunPath(string id) => $"/runtime/workflows/instances/{Uri.EscapeDataString(id)}";
    }
}
