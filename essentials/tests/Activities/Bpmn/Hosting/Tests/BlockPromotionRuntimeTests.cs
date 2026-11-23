using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Runs the production planner's documents through ordinary reusable publication and the native host.</summary>
public sealed class BlockPromotionRuntimeTests
{
    [Fact]
    public async Task Reviewed_promotion_documents_preserve_native_locals_and_isolate_two_exact_version_calls()
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "native-blocks", "block-promotions.json")))!;
        Assert.Equal("pure-planner-golden-documents-not-runtime-evidence", fixture["kind"]!.GetValue<string>());
        Assert.Equal(6, fixture["cases"]!.AsArray().Count);
        await using var host = await OwnedWorkbench.StartAsync();
        await host.LoginAsync();
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (placeholder, value) in fixture["versions"]!.AsObject())
        {
            var key = value!.GetValue<string>();
            var page = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(key));
            var row = Assert.Single(page["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == key)!;
            versions.Add(placeholder, row["definition"]!["headVersionId"]!.GetValue<string>());
        }
        var catalog = await host.GetAsync("/design/activities/catalog");
        foreach (var (placeholder, kind) in new[] { ("set", "Set"), ("set-output", "SetOutput") })
            versions[placeholder] = Assert.Single(catalog["activities"]!.AsArray(), item => item?["intrinsic"]?["kind"]?.GetValue<string>() == kind)!["activityVersionId"]!.GetValue<string>();

        foreach (var value in fixture["cases"]!.AsArray())
        {
            var entry = value!;
            // An explicit observer is added only to the comparison baseline. It reads the private local
            // while its original native frame is alive; it is not part of the production promotion input.
            foreach (var expected in new[] { "one", "two" })
            {
                var baseline = entry["original"]!["rootActivity"]!.DeepClone();
                var body = Find(baseline, "block")!;
                var observer = Output("observe-local", "answer", new JsonObject { ["expressionType"] = "Variable", ["value"] = new JsonObject { ["referenceKey"] = "local", ["declaringScopeId"] = "block" } });
                Append(body, observer, entry["kind"]!.GetValue<string>());
                Remap(baseline);
                var view = await Run(baseline, entry["inputs"]!.DeepClone(), new Dictionary<string, string> { ["Greeting"] = expected });
                Assert.Equal(expected, Read(view, "answer"));
            }

            var candidate = entry["candidate"]!.DeepClone();
            Remap(candidate);
            var created = await host.PostAsync("/design/activities/definitions", new
            {
                category = "Named blocks qualification", displayName = "Promotion " + entry["name"]!.GetValue<string>() + " " + Guid.NewGuid().ToString("N"),
                description = "Owned production-planner qualification", contract = candidate["contract"], provider = candidate["provider"],
                layout = candidate["layout"], activityPresentation = candidate["activityPresentation"]
            });
            var draftId = created["draft"]!["draftId"]!.GetValue<string>();
            var draft = await host.GetAsync("/design/activities/drafts/" + draftId);
            var revision = draft["revision"]!.GetValue<long>();
            var preflight = await host.PostAsync($"/design/activities/drafts/{draftId}/publication-preflight", new
            { expectedDraftRevision = revision, expectedDefinitionHeadVersionId = (string?)null });
            Assert.True(preflight["isPublishable"]!.GetValue<bool>(), preflight.ToJsonString());
            var publication = await host.PostAsync($"/design/activities/drafts/{draftId}/publish", new
            {
                expectedDraftRevision = revision, expectedDefinitionHeadVersionId = preflight["definitionHeadVersionId"]?.GetValue<string>(),
                version = preflight["minimumVersion"]!.GetValue<string>(), reviewToken = preflight["reviewToken"]!.GetValue<string>(), idempotencyKey = Guid.NewGuid().ToString("N")
            });
            var exactVersion = publication["outcome"]!["definitionVersionId"]!.GetValue<string>();
            var calls = new JsonArray();
            foreach (var (id, input) in new[] { ("call-one", "one"), ("call-two", "two") })
            {
                var call = entry["callSite"]!.DeepClone();
                call["nodeId"] = id; call["activityVersionId"] = exactVersion;
                foreach (var binding in call["inputs"]!.AsArray()) binding!["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = input };
                calls.Add(call);
            }
            var outputKey = entry["outputReferenceKey"]!.GetValue<string>();
            foreach (var (name, producer) in new[] { ("one", "call-one"), ("two", "call-two"), ("one-again", "call-one") })
                calls.Add(Output("expose-" + name, name, new JsonObject { ["expressionType"] = "ActivityResult", ["value"] = new JsonObject { ["producerNodeId"] = producer, ["projectionKey"] = outputKey } }));
            var root = new JsonObject { ["nodeId"] = "caller", ["activityVersionId"] = versions["sequence"], ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["structure"] = new JsonObject { ["kind"] = "elsa.sequence.structure", ["schemaVersion"] = "1.0.0", ["payload"] = new JsonObject { ["activities"] = calls } } };
            Remap(root);
            var actual = await Run(root, new JsonArray(), null);
            Assert.Equal("one", Read(actual, "one")); Assert.Equal("two", Read(actual, "two")); Assert.Equal("one", Read(actual, "one-again"));
        }

        async Task<JsonNode> Run(JsonNode root, JsonNode inputs, Dictionary<string, string>? values)
        {
            var source = await host.PostAsync("/design/workflows/definitions/submit", new { name = "Promotion native proof " + Guid.NewGuid().ToString("N"),
                state = new { rootActivity = root, inputs, outputs = Array.Empty<object>(), variables = Array.Empty<object>() } });
            var published = await host.PostAsync($"/publishing/workflows/{source["version"]!["id"]!.GetValue<string>()}/publish", new { });
            var started = await host.PostAsync($"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute", new
            { inputs = values, sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
            var id = started["workflowExecutionId"]!.GetValue<string>(); var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                var view = await host.GetAsync("/runtime/workflows/instances/" + id);
                Assert.Empty(view["incidents"]!.AsArray());
                if (view["instance"]!["status"]!.GetValue<string>() == "Completed") return view;
                Assert.True(DateTimeOffset.UtcNow < deadline, view.ToJsonString()); await Task.Delay(100);
            }
        }
        void Remap(JsonNode? value)
        {
            if (value is JsonArray array) { foreach (var item in array) Remap(item); return; }
            if (value is not JsonObject record) return;
            if (record["activityVersionId"] is JsonValue id && id.TryGetValue<string>(out var placeholder) && versions.TryGetValue(placeholder, out var actual)) record["activityVersionId"] = actual;
            foreach (var item in record.ToArray()) Remap(item.Value);
        }
    }

    private static JsonObject Output(string id, string name, JsonNode value) => new()
    {
        ["nodeId"] = id, ["activityVersionId"] = "set-output", ["inputs"] = new JsonArray(
            new JsonObject { ["referenceKey"] = "name", ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = name } },
            new JsonObject { ["referenceKey"] = "value", ["value"] = value }), ["outputs"] = new JsonArray(),
        ["intrinsic"] = new JsonObject { ["kind"] = "setOutput", ["valueType"] = new JsonObject { ["alias"] = "String", ["collectionKind"] = "Single" } }
    };

    private static JsonNode? Find(JsonNode? node, string id)
    {
        if (node is JsonObject record)
        {
            if (record["nodeId"]?.GetValue<string>() == id) return record;
            foreach (var pair in record) if (Find(pair.Value, id) is { } found) return found;
        }
        if (node is JsonArray array) foreach (var child in array) if (Find(child, id) is { } found) return found;
        return null;
    }

    private static void Append(JsonNode owner, JsonNode step, string kind)
    {
        var payload = owner["structure"]!["payload"]!;
        var previous = payload["activities"]!.AsArray().Last()!["nodeId"]!.GetValue<string>();
        payload["activities"]!.AsArray().Add(step);
        if (kind == "flowchart") payload["connections"]!.AsArray().Add(new JsonObject { ["id"] = "observer-link", ["source"] = new JsonObject { ["nodeId"] = previous, ["port"] = "Done" }, ["target"] = new JsonObject { ["nodeId"] = "observe-local" } });
        if (kind == "bpmn")
        {
            var flow = Assert.Single(payload["sequenceFlows"]!.AsArray(), item => item!["targetRef"]!.GetValue<string>() == "end")!;
            var source = flow["sourceRef"]!.GetValue<string>(); flow["sourceRef"] = "observer-element";
            payload["elements"]!.AsArray().Add(new JsonObject { ["elementId"] = "observer-element", ["elementType"] = "task", ["childNodeId"] = "observe-local" });
            payload["sequenceFlows"]!.AsArray().Add(new JsonObject { ["flowId"] = "observer-link", ["sourceRef"] = source, ["targetRef"] = "observer-element" });
        }
    }

    private static string Read(JsonNode view, string key)
    {
        var output = Assert.IsType<JsonObject>(view["outputs"]![key]); Assert.False(output["isRedacted"]!.GetValue<bool>());
        Assert.Equal("DiagnosticSnapshot", output["captureMode"]!.GetValue<string>());
        Assert.Equal("string", output["value"]!["kind"]!.GetValue<string>()); Assert.False(output["value"]!["truncated"]!.GetValue<bool>());
        return output["value"]!["preview"]!.GetValue<string>();
    }
}
