using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Elsa.Workbench.Qualification;

internal static partial class WorkbenchPostgresPreview
{
    internal static async Task RunShippedHistoryAsync(string executable, bool postgres, bool playback = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var workspace = Directory.CreateTempSubdirectory("elsa-workbench-history-");
        var sqlite = postgres ? null : Path.Combine(workspace.FullName, "kernel.sqlite");
        var connection = postgres
            ? Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.")
            : "";
        try
        {
            using var host = WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null, sqlite, workspace,
                diagnosticHistoryDisabled: true, playbackQualification: playback);
            await host.WaitReadyAsync(token);
            await host.WaitForKernelServingAsync(token);
            var address = new Uri(await host.ListeningAsync(token));
            Require(address.IsLoopback && address.Port != 5095, "the Workbench uses a dedicated loopback listener");
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = address };
            using (var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }, token))
                Require(login.IsSuccessStatusCode, "the administrator signs in through the normal identity route");
            var capabilities = await ReadAsync("/capabilities");
            var occurrencesAdvertised = capabilities.GetProperty("capabilities").EnumerateArray()
                .SelectMany(capability => capability.GetProperty("links").EnumerateArray())
                .Any(link => link.GetProperty("rel").GetString() == "workflow-history-occurrences");
            Require(occurrencesAdvertised == playback, "only the selected playback store advertises the occurrence relation");
            using (var wait = await client.PostAsync("/workflows/http/core-preview/wait", null, token))
                Require(wait.StatusCode == (playback ? HttpStatusCode.NotFound : HttpStatusCode.Accepted),
                    playback ? "private Playback retains the protocol-31 HTTP capability set" :
                        "the shipped profile opens the live HTTP wait");
            if (!playback)
            {
                using var resumed = await client.PostAsync("/workflows/http/core-preview/decision", null, token);
                Require(resumed.StatusCode == HttpStatusCode.OK && await resumed.Content.ReadAsStringAsync(token) == "decided",
                    "the shipped host delivers the HTTP event and returns the workflow response");
            }

            await PublishAsync("native-history", flowchart: false);
            await PublishAsync("flowchart-history", flowchart: true);
            var runs = await Task.WhenAll(CheckHistoryAsync("native-history"), CheckHistoryAsync("flowchart-history"));
            var scoped = await ReadAsync(HistoryPath(runs[0].Run) + "&bookmarkId=unavailable");
            Require(!scoped.GetProperty("scopeComplete").GetBoolean() && scoped.GetProperty("items").GetArrayLength() == 0 &&
                scoped.GetProperty("scopeLimitation").GetString() == "Kernel bookmark history is unavailable.",
                "the standard route discloses unsupported bookmark scope without exposing activity entries");
            using (var wrongRun = await client.GetAsync(HistoryPath(runs[1].Run, runs[0].CatchUp), token))
                Require(wrongRun.StatusCode == HttpStatusCode.BadRequest, "the standard route refuses a cursor from another run");
            using (var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address })
            {
                using var denied = await anonymous.GetAsync(HistoryPath(runs[0].Run), token);
                Require(denied.StatusCode == HttpStatusCode.Unauthorized, "the standard History route requires read authorization");
                using var deniedPlayback = await anonymous.GetAsync(OccurrencesPath(runs[0].Run), token);
                Require(deniedPlayback.StatusCode == HttpStatusCode.Unauthorized, "the standard Playback route requires read authorization");
            }
            await host.StopAsync();
            if (playback)
                await VerifyPlaybackProviderAsync(connection, sqlite, runs[0].Run, token);

            async Task<(string Run, string CatchUp)> CheckHistoryAsync(string name)
            {
                var run = await StartAsync(name);
                var first = await ReadAsync(HistoryPath(run));
                var upper = first.GetProperty("upperSequence").GetInt64();
                var catchUp = first.GetProperty("catchUpToken").GetString()!;
                Require(upper > 3 && first.GetProperty("nextContinuationToken").ValueKind == JsonValueKind.String &&
                    first.GetProperty("coverage").GetProperty("policyId").GetString() == "kernel-activity-journal-v1",
                    $"the published {name} opens a bounded window in the standard History route");
                var admitted = await ReadAsync(OccurrencesPath(run));
                if (!playback)
                    Require(Text(admitted, "state") == "unavailable" && Text(admitted, "reason") == "occurrence-facet-unsupported",
                        $"the shipped {name} reports unavailable Playback on a history-only provider");

                while (true)
                {
                    var detail = await ReadAsync(RunPath(run));
                    var status = detail.GetProperty("instance").GetProperty("status").GetString();
                    if (status == "Completed") break;
                    if (status is "Cancelled" or "Faulted")
                        throw new InvalidOperationException($"The published {name} ended as {status}.");
                    await Task.Delay(50, token);
                }

                var prefix = await CollectAsync(run, first);
                Require(prefix.Any(item => Activity(item, "ActivityStarted", "delay")) &&
                    prefix.All(item => !Activity(item, "ActivityCompleted", "delay") && !Activity(item, "ActivityStarted", "result")),
                    $"the published {name} keeps its original window pinned after the Delay turn commits");

                var live = await ReadAsync(HistoryPath(run, catchUp));
                var later = await CollectAsync(run, live);
                Require(live.GetProperty("upperSequence").GetInt64() > upper && later.All(item => item.GetProperty("sequence").GetInt64() > upper) &&
                    later.Any(item => Activity(item, "ActivityCompleted", "delay")) && later.Any(item => Activity(item, "ActivityCompleted", "result")),
                    $"the published {name} catches up to the committed Delay and Set Output outcomes");
                Require(prefix.Concat(later).All(item => item.GetProperty("authority").GetString() == "CommittedTransition" &&
                    item.GetProperty("occurredAtKnown").GetBoolean() && item.GetProperty("activityAncestryKnown").GetBoolean() &&
                    item.GetProperty("occurredAt").ValueKind == JsonValueKind.String && item.GetProperty("recordedAt").ValueKind == JsonValueKind.String &&
                    !item.GetRawText().Contains("private-history-fixture-value", StringComparison.Ordinal)),
                    $"the published {name} exposes committed structural facts with timestamps and ancestry");
                if (playback) await CheckPlaybackAsync(name, run, admitted, prefix.Concat(later));
                return (run, catchUp);
            }

            async Task CheckPlaybackAsync(string name, string run, JsonElement admitted, IEnumerable<JsonElement> events)
            {
                Require(Text(admitted, "state") == "partial" && Text(admitted, "selectedPosition") == "0" &&
                    admitted.GetProperty("items").GetArrayLength() == 2,
                    $"the published {name} admits a bounded Playback window");
                var originalTotal = Text(admitted, "totalCount");
                var sequence = Text(admitted, "sequenceToken");
                var last = (long.Parse(originalTotal, System.Globalization.CultureInfo.InvariantCulture) - 1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                var frozenPath = OccurrencesPath(run) + "&sequenceToken=" + Uri.EscapeDataString(sequence);
                var ranked = await ReadAsync(frozenPath + "&position=" + last);
                var row = ranked.GetProperty("items").EnumerateArray().Last();
                var activity = Text(row, "activityExecutionId");
                Require(Text(ranked, "selectedPosition") == last && Text(row, "position") == last &&
                    Text(ranked, "upperSequence") == Text(admitted, "upperSequence") && Text(ranked, "totalCount") == originalTotal &&
                    ranked.GetProperty("hasNewerHistory").GetBoolean() && Text(row, "observationKind") == "ActivityStarted" &&
                    Text(row, "executableNodeId") == "delay" && row.GetProperty("nextActivityExecutionId").ValueKind == JsonValueKind.Null,
                    $"the published {name} seeks the final frozen rank after later activity commits");
                Require(events.Any(item => Text(item, "historyEventId") == Text(row, "historyEventId")),
                    $"the published {name} Playback anchor retains the History observation identity");

                var encoded = Uri.EscapeDataString(activity);
                var anchored = await ReadAsync(frozenPath + "&anchorActivityExecutionId=" + encoded);
                Require(Text(anchored, "selectedPosition") == last && Text(anchored, "sequenceToken") == sequence,
                    $"the published {name} resolves its exact anchor inside the same frozen window");
                var laterActivity = events.First(item => Activity(item, "ActivityStarted", "result"));
                var tooNew = await ReadAsync(frozenPath + "&anchorActivityExecutionId=" + Uri.EscapeDataString(Text(laterActivity, "activityExecutionId")));
                Require(Text(tooNew, "state") == "target-unavailable" && Text(tooNew, "reason") == "anchor-outside-range",
                    $"the published {name} refuses an exact anchor committed after the frozen window");
                var refreshed = await ReadAsync(OccurrencesPath(run) + "&anchorActivityExecutionId=" + encoded);
                Require(Text(refreshed, "totalCount") != originalTotal && Text(refreshed, "upperSequence") != Text(admitted, "upperSequence") &&
                    Text(refreshed, "selectedPosition") == last,
                    $"the published {name} admits newer membership only on an explicit anchor refresh");
                var exact = OccurrencesPath(run) + "&activityExecutionId=" + encoded;
                var scoped = await ReadAsync(exact + "&anchorActivityExecutionId=" + encoded);
                Require(Text(scoped, "totalCount") == "1" && Text(scoped, "selectedPosition") == "0" &&
                    Text(scoped.GetProperty("items").EnumerateArray().Single(), "activityExecutionId") == activity,
                    $"the published {name} limits Playback to the exact activity scope");
                var descendants = await ReadAsync(exact + "&includeDescendants=true");
                Require(Text(descendants, "state") == "unavailable" && Text(descendants, "reason") == "scope-descendants-unsupported",
                    $"the published {name} reports unqualified descendant membership as unavailable");
                using var mismatched = await client.GetAsync(frozenPath + "&activityExecutionId=" + encoded, token);
                Require(mismatched.StatusCode == HttpStatusCode.BadRequest, "a Playback sequence token cannot change its scope");
            }

            async Task<IReadOnlyList<JsonElement>> CollectAsync(string run, JsonElement page)
            {
                var upper = page.GetProperty("upperSequence").GetInt64();
                var items = new List<JsonElement>();
                while (true)
                {
                    items.AddRange(page.GetProperty("items").EnumerateArray());
                    if (!page.TryGetProperty("nextContinuationToken", out var next) || next.ValueKind == JsonValueKind.Null)
                        return items;
                    var through = page.GetProperty("scannedThroughSequence").GetInt64();
                    page = await ReadAsync(HistoryPath(run, next.GetString()));
                    if (page.GetProperty("upperSequence").GetInt64() != upper || page.GetProperty("scannedThroughSequence").GetInt64() <= through)
                        throw new InvalidOperationException("The History continuation moved its horizon or failed to advance.");
                }
            }

            async Task<string> StartAsync(string name)
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
                    Require(response.StatusCode == HttpStatusCode.OK && body == name,
                        $"the ordinary published {name} starts through HTTP ({(int)response.StatusCode}: {body})");
                    return response.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single();
                }
            }

            async Task PublishAsync(string name, bool flowchart)
            {
                var start = await NodeAsync("start", "Elsa.Activities.Http.Activities.HttpEndpoint", new()
                    { ["Path"] = $"core-preview/{name}", ["CanStartWorkflow"] = true, ["SupportedMethods"] = new[] { "POST" } });
                var answer = await NodeAsync("answer", "Elsa.Activities.Http.Activities.WriteHttpResponse", new() { ["StatusCode"] = 200, ["Body"] = name });
                var delay = await NodeAsync("delay", "Elsa.Activities.Scheduling.Activities.Delay", new() { ["Duration"] = "00:00:08" });
                var result = new
                {
                    nodeId = "result", activityVersionId = "elsa.intrinsic.set-output@1", outputs = Array.Empty<object>(),
                    intrinsic = new { kind = "setOutput", valueType = new { alias = "String", collectionKind = "single" } },
                    inputs = new[] { new { referenceKey = "name", value = Literal("Result") }, new { referenceKey = "value", value = Literal("private-history-fixture-value") } }
                };
                object[] activities = [start, answer, delay, result];
                object payload = flowchart
                    ? new { activities, startNodeId = "start", connections = new[] { Edge("start", "answer"), Edge("answer", "delay"), Edge("delay", "result") } }
                    : new { activities };
                var root = await NodeAsync("root", flowchart ? "Elsa.Activities.Flowchart.Activities.Flowchart" : "Elsa.Activities.Sequence.Activities.Sequence",
                    [], new { kind = flowchart ? "elsa.flowchart.structure" : "elsa.sequence.structure", schemaVersion = "1.0.0", payload });
                var submitted = await ReadAsync("/design/workflows/definitions/submit", new
                {
                    name, state = new { rootActivity = root, variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>() }
                });
                var version = submitted.GetProperty("version").GetProperty("id").GetString()!;
                await ReadAsync($"/publishing/workflows/{Uri.EscapeDataString(version)}/publish", new { });
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

        static string RunPath(string run) => $"/runtime/workflows/instances/{Uri.EscapeDataString(run)}";
        static string HistoryPath(string run, string? cursor = null) =>
            RunPath(run) + "/history?limit=3" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        static string OccurrencesPath(string run) => RunPath(run) + "/history/occurrences?limit=2";
        static string Text(JsonElement value, string field) => value.GetProperty(field).GetString()!;
        static bool Activity(JsonElement item, string eventName, string node) =>
            item.GetProperty("name").GetString() == eventName && item.GetProperty("executableNodeId").GetString() == node;
        static object Literal(object value) => new { expressionType = "Literal", value };
        static object Edge(string from, string to) =>
            new { id = $"{from}-{to}", source = new { nodeId = from, port = "Done" }, target = new { nodeId = to } };
    }
}
