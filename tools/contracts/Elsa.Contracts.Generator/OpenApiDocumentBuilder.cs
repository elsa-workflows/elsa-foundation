using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Builds <c>docs/contracts/openapi.json</c> — an OpenAPI 3.1 document over the projected endpoints
/// (spec 150 FR-C14).
/// </summary>
/// <remarks>
/// OpenAPI rather than a bespoke index because it is what consumer tooling already speaks: client
/// generators, validators, and agents recognise it without being taught a private schema. It is built
/// here rather than served from a running host so the RFC's promise holds — a consumer reads the pinned
/// commit and needs no server.
/// <para>
/// Two mechanical details carry the correctness of the whole document. Body schemas exported by
/// <c>AuthoringSchemaExporter</c> contain root-relative JSON Pointers (<c>#/properties/...</c>) into their
/// own root; embedding one unchanged would resolve those against the OpenAPI root and break every
/// reference, so each schema is hoisted into <c>components/schemas</c> and its pointers re-prefixed.
/// And ASP.NET route constraints (<c>{versionId:regex(...)}</c>) are stripped from the path template,
/// because OpenAPI paths carry the parameter name alone.
/// </para>
/// </remarks>
public static class OpenApiDocumentBuilder
{
    /// <summary>
    /// Splits the surface into one document per route group, each self-contained (its own
    /// <c>components/schemas</c>), plus a root document that lists the parts.
    /// </summary>
    /// <remarks>
    /// A single 1.2 MB / ~301k-token document is unusable by anything that cannot stream it, and a consumer
    /// looking for five endpoints had to scan all of it. Grouping by first route segment matches how a
    /// consumer actually arrives — it knows it wants to publish, or to query runtime instances — and keeps
    /// each part near the rest of the corpus in size. Each part is a valid OpenAPI document on its own, so
    /// existing tooling works against one part without the root.
    /// </remarks>
    public static IReadOnlyDictionary<string, JsonObject> BuildParts(
        IReadOnlyList<OpenApiEndpoint> endpoints,
        string contractVersion)
    {
        var parts = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var group in endpoints.GroupBy(GroupOf))
            parts[group.Key] = Build(group.OrderBy(endpoint => endpoint.Route, StringComparer.Ordinal).ToArray(), contractVersion);

        return parts;
    }

    /// <summary>
    /// The first two literal route segments — `design/workflows`, `publishing/workflows`, `runtime/instances`
    /// — which is how the API is already organised and how a consumer arrives at it.
    /// </summary>
    /// <remarks>
    /// One segment was too coarse: grouping by `design` alone still produced a 660 KB part, because that
    /// prefix covers workflows, activities and their drafts. Two segments keeps each part in the same size
    /// class as the rest of the corpus without inventing a taxonomy the API does not already have.
    /// </remarks>
    public static string GroupOf(OpenApiEndpoint endpoint)
    {
        var segments = endpoint.Route
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            // A `{parameter}` segment names an instance, not a group, so it never contributes to the name.
            .TakeWhile(segment => !segment.StartsWith('{'))
            .Take(2)
            // `_elsa` prefixes the first-party studio/identity surface; keep it readable as a file name.
            .Select(segment => segment.TrimStart('_').ToLowerInvariant())
            .Where(segment => segment.Length > 0)
            .ToArray();

        return segments.Length == 0 ? "root" : string.Join('-', segments);
    }

    public static JsonObject BuildRoot(IReadOnlyDictionary<string, JsonObject> parts, string contractVersion)
    {
        var partList = new JsonArray();
        foreach (var (name, document) in parts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            partList.Add(new JsonObject
            {
                ["group"] = name,
                ["file"] = $"openapi/{name}.json",
                ["operations"] = document["paths"] is JsonObject paths
                    ? paths.Sum(path => path.Value is JsonObject item ? item.Count(member => !member.Key.StartsWith("x-", StringComparison.Ordinal)) : 0)
                    : 0
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = contractVersion,
            ["description"] =
                "The HTTP surface, split by route group so no single document has to be read whole. Each part "
                + "in `parts` is a complete OpenAPI 3.1 document with its own components — open only the one "
                + "you need. `index.json` maps 'VERB /path' straight to its part.",
            ["parts"] = partList
        };
    }

    public static JsonObject Build(IReadOnlyList<OpenApiEndpoint> endpoints, string contractVersion)
    {
        var components = new JsonObject();
        var paths = new JsonObject();

        foreach (var endpoint in endpoints
                     .OrderBy(endpoint => endpoint.Route, StringComparer.Ordinal)
                     .ThenBy(endpoint => endpoint.Verb, StringComparer.Ordinal))
        {
            var (template, parameters) = ToOpenApiPath(endpoint.Route);
            if (paths[template] is not JsonObject pathItem)
            {
                pathItem = new JsonObject();
                paths[template] = pathItem;
            }

            pathItem[endpoint.Verb.ToLowerInvariant()] = BuildOperation(endpoint, parameters, components);
        }

        var document = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Elsa HTTP API",
                // The published-contract version, not a product version: it tells a consumer whether the
                // shape of this document changed (spec 150 Part D).
                ["version"] = contractVersion,
                ["description"] =
                    "Generated from the built assemblies at this commit. Success statuses appear only where an endpoint "
                    + "declares them; an operation without a declared status documents its body under `default` rather "
                    + "than asserting a guessed 200. Permissions are published as `x-elsa-permissions`, and the "
                    + "providing assembly/feature as `x-elsa-assembly`/`x-elsa-feature-id` so operations can be "
                    + "intersected with hosts.json exactly like features and expression types."
            },
            ["paths"] = paths
        };

        if (components.Count > 0)
            document["components"] = new JsonObject { ["schemas"] = components };

        return document;
    }

    private static JsonObject BuildOperation(OpenApiEndpoint endpoint, JsonArray parameters, JsonObject components)
    {
        var operation = new JsonObject
        {
            ["operationId"] = OperationId(endpoint),
            ["description"] = Describe(endpoint),
            ["x-elsa-assembly"] = endpoint.Assembly
        };

        if (endpoint.FeatureId is not null)
            operation["x-elsa-feature-id"] = endpoint.FeatureId;
        if (endpoint.Permissions.Count > 0)
            operation["x-elsa-permissions"] = new JsonArray(endpoint.Permissions.Select(p => (JsonNode?)p).ToArray());
        if (endpoint.AllowsAnonymous)
            operation["x-elsa-allows-anonymous"] = true;
        if (endpoint.ConfigurationDependent)
            operation["x-elsa-configuration-dependent"] = true;
        if (endpoint.RequestSchema is { } request && endpoint.RequestType is { } requestType)
        {
            if (BindsFromQuery(endpoint.Verb))
                AppendQueryParameters(request, parameters);
            else
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = true,
                    ["content"] = new JsonObject
                    {
                        ["application/json"] = new JsonObject { ["schema"] = Reference(requestType, request, components, PathParameterNames(parameters)) }
                    }
                };
        }

        if (parameters.Count > 0)
            operation["parameters"] = parameters;

        operation["responses"] = BuildResponses(endpoint, components);
        return operation;
    }

    /// <summary>Verbs whose request record binds from the route and query string rather than from a body.</summary>
    private static bool BindsFromQuery(string verb) =>
        verb is "GET" or "DELETE" or "HEAD";

    /// <summary>
    /// Publishes a query-bound request record as query parameters (spec 150 FR-C16).
    /// </summary>
    /// <remarks>
    /// Publishing these as a <c>requestBody</c> was both wrong for OpenAPI and the reason the supported filter
    /// set was invisible: a benchmarked session passed <c>?definitionVersionId=</c> to the instances endpoint,
    /// got a 200 with an unfiltered list, and concluded the filter was broken. It was never a filter — the
    /// binder has no such target and ignores what it cannot bind. Publishing the parameters makes the
    /// supported set explicit and machine-readable, so a client generator cannot offer one that does not exist.
    /// </remarks>
    private static void AppendQueryParameters(JsonElement schema, JsonArray parameters)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return;

        var declared = parameters
            .Select(parameter => parameter?["name"]?.GetValue<string>())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var required = schema.TryGetProperty("required", out var requiredNames) && requiredNames.ValueKind == JsonValueKind.Array
            ? requiredNames.EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var property in properties.EnumerateObject())
        {
            // A route placeholder of the same name is already declared as a path parameter; the binder fills it
            // from the route, so publishing it twice would describe an argument that does not exist.
            if (!declared.Add(property.Name))
                continue;

            parameters.Add(new JsonObject
            {
                ["name"] = property.Name,
                ["in"] = "query",
                ["required"] = required.Contains(property.Name),
                ["schema"] = JsonNode.Parse(property.Value.GetRawText())
            });
        }
    }

    private static JsonObject BuildResponses(OpenApiEndpoint endpoint, JsonObject components)
    {
        var responses = new JsonObject();
        JsonNode? BodyContent() =>
            endpoint.ResponseSchema is { } response && endpoint.ResponseType is { } responseType
                ? new JsonObject
                {
                    ["application/json"] = new JsonObject { ["schema"] = Reference(responseType, response, components) }
                }
                : null;

        if (endpoint.SuccessStatuses is { Count: > 0 })
        {
            foreach (var status in endpoint.SuccessStatuses)
            {
                var response = new JsonObject
                {
                    ["description"] = endpoint.SuccessStatusCondition ?? "Success."
                };
                if (BodyContent() is { } content)
                    response["content"] = content;
                responses[status.ToString()] = response;
            }

            return responses;
        }

        // No declared status: document the body under `default` rather than inventing a 200. OpenAPI's
        // `default` means "any status not otherwise listed", which is exactly the honest statement here.
        var fallback = new JsonObject
        {
            ["description"] = "Success. This endpoint does not declare its success status, so the specific code is not published."
        };
        if (BodyContent() is { } fallbackContent)
            fallback["content"] = fallbackContent;
        responses["default"] = fallback;
        return responses;
    }

    private static string Describe(OpenApiEndpoint endpoint)
    {
        var description = new StringBuilder();
        if (endpoint.SuccessStatusCondition is { } condition)
            description.Append(condition).Append(' ');
        description.Append(endpoint.Permissions.Count > 0
            ? $"Requires one of: {string.Join(", ", endpoint.Permissions)}."
            : "No permission is declared for this endpoint.");
        if (endpoint.ConfigurationDependent)
            description.Append(" This endpoint configures itself from host options; the route and authentication "
                               + "shown are this build's defaults and a host that overrides them serves something else.");
        return description.ToString();
    }

    /// <summary>Verb + route, stable and unique, so generated clients get deterministic method names.</summary>
    private static string OperationId(OpenApiEndpoint endpoint)
    {
        var builder = new StringBuilder(endpoint.Verb.ToLowerInvariant());
        foreach (var segment in ToOpenApiPath(endpoint.Route).Template.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = new string(segment.Where(char.IsLetterOrDigit).ToArray());
            if (cleaned.Length == 0)
                continue;
            builder.Append(char.ToUpperInvariant(cleaned[0])).Append(cleaned[1..]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Strips ASP.NET route constraints and declares each path parameter. <c>{versionId:regex(^(?!drafts$).+$)}</c>
    /// becomes <c>{versionId}</c> plus a required string parameter.
    /// </summary>
    private static (string Template, JsonArray Parameters) ToOpenApiPath(string route)
    {
        var template = new StringBuilder(route.Length);
        var parameters = new JsonArray();

        for (var index = 0; index < route.Length; index++)
        {
            if (route[index] != '{')
            {
                template.Append(route[index]);
                continue;
            }

            var depth = 1;
            var end = index + 1;
            while (end < route.Length && depth > 0)
            {
                if (route[end] == '{') depth++;
                else if (route[end] == '}') depth--;
                if (depth > 0) end++;
            }

            var inner = route[(index + 1)..end];
            var name = inner.Split(':', 2)[0].TrimEnd('?');
            template.Append('{').Append(name).Append('}');
            parameters.Add(new JsonObject
            {
                ["name"] = name,
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JsonObject { ["type"] = "string" }
            });
            index = end;
        }

        return (template.ToString(), parameters);
    }

    /// <summary>
    /// Registers the schema under <c>components/schemas</c> (once per body type) and returns a reference
    /// to it, re-prefixing the schema's own root-relative pointers so they still resolve.
    /// </summary>
    private static JsonObject Reference(
        string typeName,
        JsonElement schema,
        JsonObject components,
        IReadOnlySet<string>? boundFromRoute = null)
    {
        var componentName = ToComponentName(typeName);
        if (components[componentName] is null)
        {
            var node = JsonNode.Parse(schema.GetRawText())!;
            RewritePointers(node, $"#/components/schemas/{componentName}/");
            if (boundFromRoute is { Count: > 0 })
                RemoveRouteBoundProperties(node, boundFromRoute);
            components[componentName] = NormalizeBooleanSchemas(node);
        }

        return new JsonObject { ["$ref"] = $"#/components/schemas/{componentName}" };
    }

    private static IReadOnlySet<string> PathParameterNames(JsonArray parameters) =>
        parameters
            .Where(parameter => parameter?["in"]?.GetValue<string>() == "path")
            .Select(parameter => parameter?["name"]?.GetValue<string>())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Drops properties the binder fills from the route, so they are not also demanded in the body.
    /// </summary>
    /// <remarks>
    /// <c>POST /publishing/workflows/{versionId}/publish</c> published <c>versionId</c> as a REQUIRED body
    /// property while the server accepts <c>{}</c> — the value comes from the path. A consumer following the
    /// document sends a field that is ignored, and one validating against it rejects the body the server
    /// actually wants. The parameter is still declared, once, in <c>parameters</c>.
    /// </remarks>
    private static void RemoveRouteBoundProperties(JsonNode node, IReadOnlySet<string> boundFromRoute)
    {
        if (node is not JsonObject schema)
            return;

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var name in properties.Select(property => property.Key).Where(boundFromRoute.Contains).ToArray())
                properties.Remove(name);
        }

        if (schema["required"] is JsonArray required)
        {
            for (var index = required.Count - 1; index >= 0; index--)
            {
                if (required[index]?.GetValue<string>() is { } name && boundFromRoute.Contains(name))
                    required.RemoveAt(index);
            }

            if (required.Count == 0)
                schema.Remove("required");
        }
    }

    /// <summary>
    /// OpenAPI restricts component keys to <c>^[a-zA-Z0-9.\-_]+$</c>, but a CLR full name for a generic
    /// type carries backticks and an assembly-qualified argument list
    /// (<c>AgentApiResponse`1[[Elsa.Agent.…, Elsa.Agent.Api, Version=…]]</c>). Reduce it to a readable,
    /// still-unique key: <c>Elsa.Agent.Api.Models.AgentApiResponse_AgentBootstrapResponse</c>.
    /// </summary>
    internal static string ToComponentName(string typeFullName)
    {
        var builder = new StringBuilder(typeFullName.Length);
        var depth = 0;

        for (var index = 0; index < typeFullName.Length; index++)
        {
            var character = typeFullName[index];
            switch (character)
            {
                case '[':
                    depth++;
                    // Entering an argument list: separate it from the open generic's name.
                    if (builder.Length > 0 && builder[^1] != '_')
                        builder.Append('_');
                    continue;
                case ']':
                    depth--;
                    continue;
                case '`':
                    // Skip the arity digits that follow.
                    while (index + 1 < typeFullName.Length && char.IsDigit(typeFullName[index + 1]))
                        index++;
                    continue;
                case ',' when depth > 0:
                    // Everything after the argument's type name is assembly identity; skip to the
                    // matching close bracket.
                    var skipDepth = depth;
                    while (index + 1 < typeFullName.Length && skipDepth >= depth)
                    {
                        index++;
                        if (typeFullName[index] == '[') skipDepth++;
                        else if (typeFullName[index] == ']') skipDepth--;
                    }

                    depth = skipDepth;
                    continue;
            }

            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_');
        }

        // Inside an argument list the namespace of the argument adds noise without adding uniqueness
        // beyond the type name; collapse repeated separators for readability.
        var name = builder.ToString().Trim('_');
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_", StringComparison.Ordinal);
        return name;
    }

    /// <summary>
    /// JSON Schema 2020-12 allows a boolean in any schema position (<c>true</c> = anything,
    /// <c>false</c> = nothing) and the exporter emits <c>true</c> for opaque members. OpenAPI 3.1 inherits
    /// that vocabulary but the readers consumers actually use reject it, so booleans are rewritten to the
    /// equivalent object form.
    /// </summary>
    private static JsonNode NormalizeBooleanSchemas(JsonNode schema)
    {
        if (schema is JsonValue value && value.TryGetValue<bool>(out var permissive))
            return permissive ? new JsonObject() : new JsonObject { ["not"] = new JsonObject() };

        if (schema is not JsonObject jsonObject)
            return schema;

        foreach (var keyword in new[] { "items", "additionalProperties", "not", "contains", "propertyNames" })
        {
            if (jsonObject[keyword] is { } nested)
                jsonObject[keyword] = NormalizeBooleanSchemas(nested.DeepClone());
        }

        foreach (var container in new[] { "properties", "$defs", "definitions", "patternProperties" })
        {
            if (jsonObject[container] is not JsonObject map)
                continue;
            foreach (var entry in map.ToArray())
                map[entry.Key] = NormalizeBooleanSchemas(entry.Value!.DeepClone());
        }

        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
        {
            if (jsonObject[combinator] is not JsonArray array)
                continue;
            var normalized = new JsonArray(array.Select(item => NormalizeBooleanSchemas(item!.DeepClone())).ToArray());
            jsonObject[combinator] = normalized;
        }

        return jsonObject;
    }

    private static void RewritePointers(JsonNode? node, string prefix)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                if (jsonObject["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var pointer) &&
                    pointer.StartsWith("#/", StringComparison.Ordinal))
                {
                    jsonObject["$ref"] = prefix + pointer[2..];
                }

                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Key != "$ref")
                        RewritePointers(property.Value, prefix);
                }

                break;
            case JsonArray array:
                foreach (var item in array.ToArray())
                    RewritePointers(item, prefix);
                break;
        }
    }
}

/// <summary>Flattened endpoint input for the OpenAPI builder.</summary>
public sealed record OpenApiEndpoint(
    string Verb,
    string Route,
    string Assembly,
    string? FeatureId,
    IReadOnlyList<int>? SuccessStatuses,
    string? SuccessStatusCondition,
    bool AllowsAnonymous,
    IReadOnlyList<string> Permissions,
    JsonElement? RequestSchema,
    string? RequestType,
    JsonElement? ResponseSchema,
    string? ResponseType,
    bool ConfigurationDependent = false);
