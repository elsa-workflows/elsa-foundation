using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;

namespace Elsa.Workflows.Design.FastEndpointsCapture;

public sealed record HistoricalOpenApiOperationEvidence
{
    public required EndpointIdentity Endpoint { get; init; }
    public string OperationId { get; init; } = "";
    public string Tags { get; init; } = "[]";
    public string Security { get; init; } = "[]";
    public required string Parameters { get; init; }
    public required string RequestBody { get; init; }
    public required string Responses { get; init; }
    public required string MediaTypes { get; init; }
    public required string Schemas { get; init; }

    public string Canonical
    {
        get
        {
            var projection = new Dictionary<string, object?>
            {
                ["endpoint"] = Endpoint.ToString(),
                ["mediaTypes"] = MediaTypes,
                ["parameters"] = Parameters,
                ["requestBody"] = RequestBody,
                ["responses"] = Responses,
                ["schemas"] = Schemas
            };

            if (!string.IsNullOrEmpty(OperationId) || Tags != "[]" || Security != "[]")
            {
                projection["operationId"] = OperationId;
                projection["tags"] = Tags;
                projection["security"] = Security;
            }

            return CompatibilityJson.Serialize(projection);
        }
    }
}

public sealed record HistoricalOpenApiEvidenceDocument(IReadOnlyList<HistoricalOpenApiOperationEvidence> Operations)
{
    public static HistoricalOpenApiEvidenceDocument Empty { get; } = new([]);
}

/// <summary>
/// Adds the identity fields consumed by the migration comparison to the historical projector.
/// Structural projection remains the exact projector present in the pinned FastEndpoints source.
/// </summary>
public static class HistoricalOpenApiEvidenceCapture
{
    public static HistoricalOpenApiEvidenceDocument Capture(JsonDocument suppliedDocument, bool includeIdentityMetadata = false) =>
        Capture(suppliedDocument.RootElement.GetRawText(), includeIdentityMetadata);

    public static HistoricalOpenApiEvidenceDocument Capture(string suppliedDocument, bool includeIdentityMetadata = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suppliedDocument);
        var root = JsonNode.Parse(suppliedDocument) as JsonObject
            ?? throw new InvalidDataException("The supplied OpenAPI document must be a JSON object.");
        var structural = OpenApiEvidenceCapture.Project(root);
        var operations = structural.Operations.Select(operation =>
        {
            var operationNode = FindOperation(root, operation.Endpoint);
            return new HistoricalOpenApiOperationEvidence
            {
                Endpoint = operation.Endpoint,
                OperationId = includeIdentityMetadata ? operationNode?["operationId"]?.GetValue<string>() ?? "" : "",
                Tags = includeIdentityMetadata ? CompatibilityJson.Canonicalize(operationNode?["tags"] ?? new JsonArray()) : "[]",
                Security = includeIdentityMetadata ? CompatibilityJson.Canonicalize(operationNode?["security"] ?? new JsonArray()) : "[]",
                Parameters = operation.Parameters,
                RequestBody = operation.RequestBody,
                Responses = operation.Responses,
                MediaTypes = operation.MediaTypes,
                Schemas = operation.Schemas
            };
        }).ToArray();
        return new(operations);
    }

    private static JsonObject? FindOperation(JsonObject document, EndpointIdentity endpoint)
    {
        if (document["paths"] is not JsonObject paths)
            return null;
        foreach (var path in paths)
        {
            if (path.Value is not JsonObject pathItem)
                continue;
            foreach (var method in new[] { "get", "put", "post", "delete", "options", "head", "patch", "trace" })
            {
                if (pathItem[method] is JsonObject operation && new EndpointIdentity(path.Key, method).Equals(endpoint))
                    return operation;
            }
        }
        return null;
    }
}
