using System.Net;
using System.Text.Json.Nodes;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class HttpResponseContentTypeTests
{
    [Theory]
    [InlineData(false, null, "text/plain", "Hello from Elsa 3")]
    [InlineData(true, null, "application/json", "{\"id\":42}")]
    [InlineData(false, "", "text/plain", "Hello from Elsa 3")]
    [InlineData(false, "text/html", "text/html", "Hello from Elsa 3")]
    public async Task An_imported_HTTP_response_infers_an_unset_content_type_and_preserves_an_explicit_one(
        bool structuredBody, string? contentType, string expectedContentType, string expectedBody)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("http-response-content-type"))!.AsArray();
        var respond = collection[0]!["root"]!["activities"]![1]!;
        if (structuredBody)
            respond["content"] = JsonNode.Parse("""
                { "typeName": "Object", "expression": { "type": "JavaScript", "value": "return { id: 42 };" } }
                """);
        if (contentType is not null)
            respond["contentType"] = new JsonObject
            {
                ["typeName"] = "String",
                ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = contentType }
            };

        using var uploaded = await host.Send(HttpMethod.Post, "migration/elsa3/reusable-activities/collections", collection);
        uploaded.EnsureSuccessStatusCode();
        var handle = Elsa3ImportHost.Required(JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!, "collectionHandle");
        var (planId, items) = await host.AnalyzeAsync(handle);
        const string sourceVersionId = "http-response-content-type-v1";
        await host.SelectAsync(handle, planId, sourceVersionId);
        var applied = await host.ApplyAsync(handle, planId, "import-http-response", sourceVersionId);
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var item = Assert.Single(items);
        await host.PublishWorkflowAsync(Elsa3ImportHost.Required(item, "workflowVersionId"));

        using var response = await host.Client.PostAsync("workflows/http/elsa3/hello", new StringContent(""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Equal(expectedContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedBody, body);
    }
}
