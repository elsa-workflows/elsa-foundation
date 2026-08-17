using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Serialization;
using System.Text.Json.Nodes;

namespace Elsa.Workflows.Runtime.FastEndpointsCapture;

internal sealed record HistoricalOpenApiOperationEvidence
{
    public required EndpointIdentity Endpoint { get; init; }
    public required string Parameters { get; init; }
    public required string RequestBody { get; init; }
    public required string Responses { get; init; }
    public required string MediaTypes { get; init; }
    public required string Schemas { get; init; }

    public string Canonical => CompatibilityJson.Serialize(new Dictionary<string, object?>
    {
        ["endpoint"] = Endpoint.ToString(),
        ["parameters"] = Parameters,
        ["requestBody"] = RequestBody,
        ["responses"] = Responses,
        ["mediaTypes"] = MediaTypes,
        ["schemas"] = Schemas
    });
}

internal sealed record HistoricalOpenApiEvidenceDocument(IReadOnlyList<HistoricalOpenApiOperationEvidence> Operations);

/// <summary>
/// The owner-local OpenAPI projection used by the historical Runtime capture.
/// Keep this projector independent from the shared comparison projector so later
/// schema/identity changes cannot rewrite the immutable before oracle.
/// </summary>
internal static class HistoricalOpenApiEvidenceCapture
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static HistoricalOpenApiEvidenceDocument Capture(string suppliedDocument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suppliedDocument);
        var root = JsonNode.Parse(suppliedDocument) as JsonObject
            ?? throw new InvalidDataException("The supplied OpenAPI document must be a JSON object.");
        return Project(root);
    }

    private static HistoricalOpenApiEvidenceDocument Project(JsonObject document)
    {
        var operations = new List<HistoricalOpenApiOperationEvidence>();
        if (document["paths"] is not JsonObject paths)
            return new(operations);

        foreach (var path in paths.OrderBy(x => x.Key, StringComparer.Ordinal)
                     .Where(x => x.Value is JsonObject)
                     .Select(x => (x.Key, Item: (JsonObject)x.Value!)))
        {
            foreach (var method in Methods.Where(method => path.Item[method] is JsonObject))
            {
                var operation = (JsonObject)path.Item[method]!;
                var endpoint = new EndpointIdentity(path.Key, method);
                var parameters = MergeParameters(path.Item["parameters"], operation["parameters"]);
                var requestBody = ProjectRequestBody(operation["requestBody"]);
                var responses = ProjectResponses(operation["responses"]);
                operations.Add(new HistoricalOpenApiOperationEvidence
                {
                    Endpoint = endpoint,
                    Parameters = CompatibilityJson.Canonicalize(parameters),
                    RequestBody = CompatibilityJson.Canonicalize(requestBody),
                    Responses = CompatibilityJson.Canonicalize(responses),
                    MediaTypes = CompatibilityJson.Canonicalize(ExtractMediaTypes(parameters, requestBody, responses)),
                    Schemas = CompatibilityJson.Canonicalize(ExtractSchemas(document, parameters, requestBody, responses))
                });
            }
        }

        return new(operations);
    }

    private static JsonArray MergeParameters(JsonNode? pathParameters, JsonNode? operationParameters)
    {
        var all = new List<JsonNode>();
        Add(operationParameters);
        Add(pathParameters);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new JsonArray(all.Where(parameter => parameter is JsonObject && seen.Add($"{parameter!["in"]}:{parameter["name"]}"))
            .OrderBy(parameter => parameter!["in"]?.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(parameter => parameter!["name"]?.GetValue<string>(), StringComparer.Ordinal)
            .Select(parameter => ProjectParameter(parameter!)).ToArray());

        void Add(JsonNode? node)
        {
            if (node is JsonArray array)
                all.AddRange(array.Where(item => item is not null).Select(item => item!));
        }
    }

    private static JsonObject ProjectParameter(JsonNode parameter)
    {
        if (parameter is not JsonObject obj)
            return new();
        var result = new JsonObject { ["name"] = obj["name"]?.GetValue<string>(), ["in"] = obj["in"]?.GetValue<string>() };
        if (obj["required"] is not null)
            result["required"] = obj["required"]!.DeepClone();
        if (obj["schema"] is not null)
            result["schema"] = ProjectSchema(obj["schema"]!);
        if (obj["content"] is not null)
            result["content"] = ProjectContent(obj["content"]!);
        return result;
    }

    private static JsonNode ProjectRequestBody(JsonNode? value) => value is not JsonObject obj ? new JsonObject() : new JsonObject
    {
        ["required"] = obj["required"]?.DeepClone() ?? false,
        ["content"] = ProjectContent(obj["content"])
    };

    private static JsonNode ProjectResponses(JsonNode? value)
    {
        if (value is not JsonObject responses)
            return new JsonObject();
        var result = new JsonObject();
        foreach (var response in responses.OrderBy(x => x.Key, StringComparer.Ordinal).Where(x => x.Value is JsonObject))
        {
            var item = (JsonObject)response.Value!;
            result[response.Key] = new JsonObject { ["content"] = ProjectContent(item["content"]) };
        }
        return result;
    }

    private static JsonNode ProjectContent(JsonNode? value)
    {
        if (value is not JsonObject content)
            return new JsonObject();
        var result = new JsonObject();
        foreach (var media in content.OrderBy(x => x.Key, StringComparer.Ordinal).Where(x => x.Value is JsonObject))
        {
            var item = (JsonObject)media.Value!;
            result[media.Key.ToLowerInvariant()] = new JsonObject
            {
                ["schema"] = item["schema"] is null ? null : ProjectSchema(item["schema"]!)
            };
        }
        return result;
    }

    private static JsonArray ExtractMediaTypes(JsonNode parameters, JsonNode requestBody, JsonNode responses)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        AddContent(requestBody["content"]);
        if (parameters is JsonArray parametersArray)
            foreach (var parameter in parametersArray)
                AddContent(parameter?["content"]);
        if (responses is JsonObject responseObject)
            foreach (var response in responseObject)
                AddContent(response.Value?["content"]);
        return new JsonArray(values.Order(StringComparer.Ordinal).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

        void AddContent(JsonNode? content)
        {
            if (content is JsonObject obj)
                foreach (var media in obj)
                    values.Add(media.Key.ToLowerInvariant());
        }
    }

    private static JsonNode ExtractSchemas(JsonObject document, params JsonNode[] consumedSurfaces)
    {
        var schemas = new JsonObject();
        foreach (var surface in consumedSurfaces)
            Visit(surface);
        return schemas;

        void Visit(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                if (node is JsonArray array)
                    foreach (var item in array)
                        Visit(item);
                return;
            }

            if (obj["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
            {
                var name = reference["#/components/schemas/".Length..];
                if (!schemas.ContainsKey(name) && document["components"]?["schemas"]?[name] is JsonNode schema)
                {
                    schemas[name] = ProjectSchema(schema);
                    Visit(schema);
                }
            }

            foreach (var property in obj)
                Visit(property.Value);
        }
    }

    private static JsonNode ProjectSchema(JsonNode node)
    {
        if (node is not JsonObject obj)
            return node.DeepClone();
        var result = new JsonObject();
        foreach (var property in new[] { "type", "format", "nullable", "required", "enum", "items", "properties", "additionalProperties", "$ref", "oneOf", "anyOf", "allOf" }
                     .Select(key => (Key: key, Value: obj[key]))
                     .Where(property => property.Value is not null))
        {
            var value = property.Value!;
            result[property.Key] = property.Key == "properties" && value is JsonObject properties
                ? new JsonObject(properties.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => (JsonNode?)ProjectSchema(x.Value!), StringComparer.Ordinal))
                : value.DeepClone();
        }
        return result;
    }
}
