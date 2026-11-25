using System.Net;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #5790: a script bound to an Elsa 3 <c>HttpHeaders</c> or <c>ObjectDictionary</c> input declares the Elsa 4
/// dictionary member's type, so the imported workflow publishes and Elsa 4 converts the script's value at run time.
/// </summary>
public sealed class DictionaryInputExpressionTests
{
    [Theory]
    [InlineData("return { 'X-Demo': ['one', 'two'] };", new[] { "one", "two" })]
    public async Task A_script_on_Elsa3_response_headers_publishes_and_sets_the_headers(string script, string[] expected)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("http-response-content-type"))!.AsArray();
        collection[0]!["root"]!["activities"]![1]!["responseHeaders"] = new JsonObject
        {
            ["typeName"] = "HttpHeaders",
            ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = script }
        };
        var import = await ImportAsync(host, collection, "http-response-content-type-v1");
        await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import, "workflowVersionId"));

        using var response = await host.Client.PostAsync("workflows/http/elsa3/hello", new StringContent(""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Equal(expected, response.Headers.GetValues("X-Demo"));
    }

    [Theory]
    [InlineData("ObjectDictionary")]
    [InlineData("Object")]
    public async Task A_script_on_Elsa3_dispatch_input_publishes_and_runs(string typeName)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("dispatch-target", "dispatch-caller"))!.AsArray();
        // Elsa 4 dispatches only inputs the target declares.
        var target = collection.Single(source => (string?)source!["id"] == "dispatch-target-v1")!;
        target["inputs"] = new JsonArray(new JsonObject { ["name"] = "Name", ["type"] = "String" });
        var caller = collection.Single(source => (string?)source!["id"] == "dispatch-caller-v1")!;
        caller["root"]!["activities"]![0]!["input"] = new JsonObject
        {
            ["typeName"] = typeName,
            ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = "return { Name: 'x' };" }
        };
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        await host.SelectAsync(handle, plan, "dispatch-target-v1", "dispatch-caller-v1");
        var applied = await host.ApplyAsync(handle, plan, "import-dispatch-" + typeName, "dispatch-target-v1", "dispatch-caller-v1");
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());

        await host.PublishWorkflowAsync(VersionId(items, "dispatch-target-v1"));
        var published = await host.PublishWorkflowAsync(VersionId(items, "dispatch-caller-v1"));
        var run = await host.RunAsync(published);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
    }

    private static async Task<JsonNode> ImportAsync(Elsa3ImportHost host, JsonArray collection, string sourceVersionId)
    {
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        await host.SelectAsync(handle, plan, sourceVersionId);
        var applied = await host.ApplyAsync(handle, plan, "import-" + sourceVersionId, sourceVersionId);
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        return Assert.Single(items);
    }

    private static string VersionId(IReadOnlyList<JsonNode> items, string sourceVersionId) =>
        Elsa3ImportHost.Required(
            items.Single(item => (string?)item["sourceVersionId"] == sourceVersionId),
            "workflowVersionId");
}
