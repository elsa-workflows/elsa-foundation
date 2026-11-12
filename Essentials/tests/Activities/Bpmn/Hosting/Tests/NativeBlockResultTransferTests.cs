using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Exercises production-planner documents through the normal current publisher and owned Workbench.</summary>
public sealed class NativeBlockResultTransferTests
{
    [Fact]
    public async Task Explicit_native_result_transfers_publish_run_and_survive_restart_without_weakening_scope_admission()
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "native-blocks", "result-transfers.json")))!;
        Assert.Equal("pure-planner-golden-documents-not-runtime-evidence", fixture["kind"]!.GetValue<string>());
        await using var host = await OwnedWorkbench.StartAsync();
        await host.LoginAsync();
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (placeholder, type) in fixture["versions"]!.AsObject())
        {
            var activityType = type!.GetValue<string>();
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(activityType));
            var row = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == activityType);
            versions.Add(placeholder, row!["definition"]!["headVersionId"]!.GetValue<string>());
        }
        Assert.Equal(6, fixture["cases"]!.AsArray().Count);
        foreach (var entry in fixture["cases"]!.AsArray())
        {
            var test = entry!;
            var baseline = test["context"]!["document"]!["rootActivity"]!.DeepClone();
            var grouped = test["grouped"]!["rootActivity"]!.DeepClone();
            var ungrouped = test["ungrouped"]!["rootActivity"]!.DeepClone();
            foreach (var root in new[] { baseline, grouped, ungrouped }) Remap(root);

            // Without the explicit transfer, the exact ordinary outside read must still be refused.
            var naive = grouped.DeepClone();
            var ownerId = test["ownerId"]!.GetValue<string>();
            var outer = FindNode(naive, ownerId)!["structure"]!["payload"]!;
            var original = FindNode(baseline, ownerId)!["structure"]!["payload"]!;
            var selected = test["request"]!["selectedNodeIds"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            outer.AsObject().Remove("variables");
            var block = outer["activities"]![0]!["structure"]!["payload"]!;
            block["activities"] = new JsonArray(original["activities"]!.AsArray().Where(node => selected.Contains(node!["nodeId"]!.GetValue<string>())).Select(node => node!.DeepClone()).ToArray());
            if (block["connections"] is not null) block["connections"] = new JsonArray(original["connections"]!.AsArray()
                .Where(edge => selected.Contains(edge!["source"]!["nodeId"]!.GetValue<string>()) && selected.Contains(edge!["target"]!["nodeId"]!.GetValue<string>()))
                .Select(edge => edge!.DeepClone()).ToArray());
            outer["activities"]![1] = original["activities"]!.AsArray().Single(node => node!["nodeId"]!.GetValue<string>() == "consumer")!.DeepClone();
            var refusedVersion = await Submit(naive, test["variables"]);
            var problem = await host.PostAsync($"/publishing/workflows/{refusedVersion}/publish", new { }, HttpStatusCode.BadRequest);
            Assert.Contains(problem["diagnostics"]!.AsArray(), item => item!["code"]!.GetValue<string>() == "ValueFlow/ScopeBoundary"
                && item!["authoredPath"]!.GetValue<string>() == "consumer/inputs/Expression");

            foreach (var root in new[] { baseline, grouped, ungrouped })
            {
                var version = await Submit(root, test["variables"]);
                var publication = await host.PostAsync($"/publishing/workflows/{version}/publish", new { });
                var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
                    new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
                var execution = started["workflowExecutionId"]!.GetValue<string>();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                JsonNode view;
                while (true)
                {
                    view = await host.GetAsync("/runtime/workflows/instances/" + execution);
                    Assert.Empty(view["incidents"]!.AsArray());
                    if (view["instance"]!["status"]!.GetValue<string>() == "Completed") break;
                    Assert.True(DateTimeOffset.UtcNow < deadline, "The explicit native result transfer did not complete.");
                    await Task.Delay(100);
                }
                var value = view["outputs"]!["answer"]!["value"]!["value"]!;
                Assert.Equal(test["expected"]!.GetValue<string>(), value.ToJsonString());
                var looping = test["loop"]!.GetValue<bool>();
                foreach (var id in new[] { "producer", "consumer", "answer" })
                {
                    var occurrences = view["activities"]!.AsArray().Where(item => item!["authoredActivityId"]!.GetValue<string>() == id).ToArray();
                    Assert.Equal(looping && id != "answer" ? 3 : 1, occurrences.Length);
                    Assert.All(occurrences, item => Assert.Equal("Completed", item!["status"]!.GetValue<string>()));
                }
                if (looping)
                {
                    var details = new List<JsonNode>();
                    foreach (var activity in view["activities"]!.AsArray().Where(item => item!["authoredActivityId"]!.GetValue<string>() == "consumer"))
                        details.Add(await host.GetAsync($"/runtime/workflows/instances/{execution}/activity-executions/{activity!["activityExecutionId"]!.GetValue<string>()}"));
                    var values = new List<int>();
                    foreach (var detail in details.OrderBy(item => item["scheduledAt"]!.GetValue<string>(), StringComparer.Ordinal))
                    {
                        var evidence = detail["valueSnapshots"]!.AsArray()[0]!["evidenceId"]!.GetValue<string>();
                        var payload = await host.GetAsync($"/runtime/workflows/instances/{execution}/activity-executions/{detail["activityExecutionId"]!.GetValue<string>()}/value-evidence/{evidence}/payload");
                        values.Add(payload["payload"]!["value"]!.GetValue<int>());
                    }
                    Assert.Equal(new[] { 1, 2, 3 }, values);
                }
                if (ReferenceEquals(root, grouped))
                {
                    await host.StopAsync();
                    await host.RestartAsync();
                    await host.LoginAsync();
                    var retained = await host.GetAsync("/runtime/workflows/instances/" + execution);
                    Assert.Equal("Completed", retained["instance"]!["status"]!.GetValue<string>());
                    Assert.True(JsonNode.DeepEquals(value, retained["outputs"]!["answer"]!["value"]!["value"]));
                    Assert.Empty(retained["incidents"]!.AsArray());
                }
            }
        }

        async Task<string> Submit(JsonNode root, JsonNode? variables)
        {
            var submitted = await host.PostAsync("/design/workflows/definitions/submit", new {
                name = "Native block result transfer " + Guid.NewGuid().ToString("N"),
                state = new { variables = variables?.DeepClone() ?? new JsonArray(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
            });
            return submitted["version"]!["id"]!.GetValue<string>();
        }

        void Remap(JsonNode? node)
        {
            if (node is JsonArray array) { foreach (var child in array) Remap(child); return; }
            if (node is not JsonObject value) return;
            if (value["activityVersionId"] is JsonValue id && id.TryGetValue<string>(out var placeholder) && versions.TryGetValue(placeholder, out var actual))
                value["activityVersionId"] = actual;
            foreach (var child in value.ToArray()) Remap(child.Value);
        }

        static JsonNode? FindNode(JsonNode? node, string id)
        {
            if (node is JsonObject value)
            {
                if (value["activityVersionId"] is not null && value["nodeId"]?.GetValue<string>() == id) return value;
                foreach (var child in value) if (FindNode(child.Value, id) is { } found) return found;
            }
            if (node is JsonArray array) foreach (var child in array) if (FindNode(child, id) is { } found) return found;
            return null;
        }
    }
}
