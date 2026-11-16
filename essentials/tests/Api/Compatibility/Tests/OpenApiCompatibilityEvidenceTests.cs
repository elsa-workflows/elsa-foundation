using Elsa.Api.Compatibility.Testing.OpenApi;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class OpenApiCompatibilityEvidenceTests
{
    [Fact]
    public void Projects_consumed_parameters_bodies_responses_media_types_and_schemas()
    {
        const string document = """
        {"openapi":"3.0.0","paths":{"/orders/{id}":{"parameters":[{"name":"id","in":"path","required":true,"schema":{"type":"integer"}}],"get":{"parameters":[{"name":"filter","in":"query","required":false,"schema":{"type":"string"}}],"responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Order"}}}}}}}},"components":{"schemas":{"Order":{"type":"object","properties":{"id":{"type":"integer"},"customer":{"$ref":"#/components/schemas/Customer"}}},"Customer":{"type":"object","properties":{"name":{"type":"string"}}}}}}
        """;

        var evidence = OpenApiEvidenceCapture.Capture(document);
        var operation = Assert.Single(evidence.Operations);

        Assert.Equal("GET /orders/{param}", operation.Endpoint.ToString());
        Assert.Contains("filter", operation.Parameters, StringComparison.Ordinal);
        Assert.Contains("application/json", operation.MediaTypes, StringComparison.Ordinal);
        Assert.Contains("Order", operation.Schemas, StringComparison.Ordinal);
        Assert.Contains("Customer", operation.Schemas, StringComparison.Ordinal);
        Assert.Contains("200", operation.Responses, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_documentation_only_noise_and_orders_operations_deterministically()
    {
        const string firstDocument = """
            {"paths":{"/z":{"get":{"description":"one","responses":{"204":{}}}},"/a":{"post":{"summary":"two","responses":{"201":{}}}}}}
            """;
        const string secondDocument = """
            {"paths":{"/a":{"post":{"responses":{"201":{}}}},"/z":{"get":{"responses":{"204":{}}}}}}
            """;
        var first = OpenApiEvidenceCapture.Capture(firstDocument);
        var second = OpenApiEvidenceCapture.Capture(secondDocument);

        Assert.Equal(first.Operations.Select(x => x.Canonical), second.Operations.Select(x => x.Canonical));
    }

    [Fact]
    public void Operation_parameters_override_matching_path_item_parameters()
    {
        const string document = """
            {"paths":{"/orders":{"parameters":[{"name":"filter","in":"query","required":false,"schema":{"$ref":"#/components/schemas/LegacyFilter"}}],"get":{"parameters":[{"name":"filter","in":"query","required":true,"schema":{"$ref":"#/components/schemas/CurrentFilter"}}],"responses":{"200":{}}}}},"components":{"schemas":{"LegacyFilter":{"type":"integer"},"CurrentFilter":{"type":"string"}}}}
            """;

        var operation = Assert.Single(OpenApiEvidenceCapture.Capture(document).Operations);
        var parameter = Assert.IsType<JsonObject>(Assert.Single(Assert.IsType<JsonArray>(JsonNode.Parse(operation.Parameters))));
        var schemas = Assert.IsType<JsonObject>(JsonNode.Parse(operation.Schemas));

        Assert.True(parameter["required"]!.GetValue<bool>());
        Assert.Equal("#/components/schemas/CurrentFilter", parameter["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("string", schemas["CurrentFilter"]!["type"]!.GetValue<string>());
        Assert.False(schemas.ContainsKey("LegacyFilter"));
    }
}
