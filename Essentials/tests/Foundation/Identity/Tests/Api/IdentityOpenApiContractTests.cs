using Elsa.Api.Compatibility.Testing.OpenApi;
using System.Text.Json.Nodes;

namespace Elsa.Foundation.Identity.Tests.Api;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class IdentityOpenApiContractTests
{
    [Fact]
    public async Task Minimal_api_publishes_the_nine_identity_operations_with_stable_ids_and_tags()
    {
        await using var fixture = await TokenEndpointFixture.StartAsync();
        using var client = fixture.Client;
        var document = await client.GetStringAsync("/openapi/v1.json");
        var identityOperations = OpenApiEvidenceCapture.Capture(document).Operations
            .Where(operation => operation.Endpoint.Route.Value.Contains("/_elsa/identity/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(9, identityOperations.Length);
        AssertIdentityOpenApiMetadata(document);
    }

    private static void AssertIdentityOpenApiMetadata(string document)
    {
        var root = JsonNode.Parse(document)?.AsObject()
            ?? throw new InvalidDataException("The current OpenAPI document is not a JSON object.");
        var expected = new Dictionary<(string Path, string Method), string>
        {
            [("/_elsa/identity/bootstrap", "get")] = "FoundationIdentityBootstrap",
            [("/_elsa/identity/capabilities", "get")] = "FoundationIdentityCapabilities",
            [("/_elsa/identity/session", "get")] = "FoundationIdentitySession",
            [("/_elsa/identity/token", "get")] = "FoundationIdentityToken",
            [("/_elsa/identity/challenge/{provider}", "get")] = "FoundationIdentityChallenge",
            [("/_elsa/identity/logout/{provider}", "post")] = "FoundationIdentityLogout",
            [("/_elsa/identity/refresh", "post")] = "FoundationIdentityRefresh",
            [("/_elsa/identity/login", "get")] = "AspNetCoreIdentityLoginPage",
            [("/_elsa/identity/login", "post")] = "AspNetCoreIdentityLogin"
        };

        foreach (var ((path, method), operationId) in expected)
        {
            var operation = root["paths"]?[path]?[method]?.AsObject()
                ?? throw new InvalidDataException($"OpenAPI operation is missing: {method.ToUpperInvariant()} {path}");
            Assert.Equal(operationId, operation["operationId"]?.GetValue<string>());
            var tags = operation["tags"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];
            Assert.Equal(["Identity"], tags);
        }
    }
}
