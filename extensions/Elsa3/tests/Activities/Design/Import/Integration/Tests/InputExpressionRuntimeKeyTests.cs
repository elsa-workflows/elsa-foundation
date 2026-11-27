using System.Net;
using System.Text.Json.Nodes;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #5686: an Elsa 3 input script that indexes an object or array with a runtime key (<c>obj[key]</c>) publishes.
/// Input expressions evaluate without the workflow-state capability, so a runtime key cannot reach it; publication
/// refuses only a script that names <c>workflowState</c>.
/// </summary>
public sealed class InputExpressionRuntimeKeyTests
{
    [Fact]
    public async Task A_response_body_script_with_runtime_keys_publishes_and_responds()
    {
        const string script = """
            const copy = { keep: 1, drop: 2 };
            const name = 'dr' + 'op';
            delete copy[name];
            const list = [10, 20];
            const index = list.length - 1;
            list[index] = 30;
            return { copy: copy, list: list };
            """;
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("http-response-content-type"))!.AsArray();
        var respond = collection[0]!["root"]!["activities"]![1]!;
        respond["content"] = Script("Object", script);
        respond["contentType"] = new JsonObject
        {
            ["typeName"] = "String",
            ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = "application/json" }
        };

        using var response = await PublishAndCallAsync(host, collection);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"copy":{"keep":1},"list":[10,30]}"""), JsonNode.Parse(body)), body);
    }

    [Fact]
    public async Task A_response_headers_script_with_a_runtime_key_publishes_and_sets_the_header()
    {
        const string script = """
            const name = 'X-' + 'Demo';
            const result = {};
            result[name] = 'one';
            return result;
            """;
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("http-response-content-type"))!.AsArray();
        collection[0]!["root"]!["activities"]![1]!["responseHeaders"] = Script("HttpHeaders", script);

        using var response = await PublishAndCallAsync(host, collection);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Equal(["one"], response.Headers.GetValues("X-Demo"));
    }

    private static JsonObject Script(string typeName, string script) => new()
    {
        ["typeName"] = typeName,
        ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = script }
    };

    private static async Task<HttpResponseMessage> PublishAndCallAsync(Elsa3ImportHost host, JsonArray collection)
    {
        const string sourceVersionId = "http-response-content-type-v1";
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        await host.SelectAsync(handle, plan, sourceVersionId);
        var applied = await host.ApplyAsync(handle, plan, "import-" + sourceVersionId, sourceVersionId);
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));
        return await host.Client.PostAsync("workflows/http/elsa3/hello", new StringContent(""));
    }
}
