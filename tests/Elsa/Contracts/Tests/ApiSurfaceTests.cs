using System.Text.Json;
using Elsa.Contracts.Generator;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 FR-C14/C15. The benchmark named the missing API surface the single largest gap: /swagger and
/// /openapi both 404, so every session obtained the publish/execute/instances endpoints by blind probing
/// or by reading an existing consumer suite. It is published as OpenAPI so consumer tooling (client
/// generators, validators, agents) reads it without being taught a private schema.
/// </summary>
public sealed class ApiSurfaceTests
{
    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string DocumentPath() => Path.Combine(ContractsRoot(), "openapi.json");

    private static JsonDocument Raw() => JsonDocument.Parse(File.ReadAllBytes(DocumentPath()));

    /// <summary>
    /// Parsed by the real reader, not by our own assumptions: a document our writer is happy with but
    /// consumer tooling rejects would be worse than publishing nothing.
    /// </summary>
    [Fact]
    public void The_published_document_parses_as_valid_openapi()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(DocumentPath()));
        var result = OpenApiDocument.Load(stream, "json");

        Assert.NotNull(result.Document);
        var errors = result.Diagnostic?.Errors ?? [];
        Assert.True(errors.Count == 0,
            "docs/contracts/openapi.json is not a valid OpenAPI document: " +
            string.Join("; ", errors.Select(error => $"{error.Pointer}: {error.Message}")));
        Assert.NotEmpty(result.Document!.Paths);
    }

    /// <summary>
    /// Body schemas carry root-relative JSON Pointers into their own root; hoisting them into
    /// components without re-prefixing would break every one of them while still looking well-formed.
    /// </summary>
    [Fact]
    public void Every_schema_reference_resolves_inside_the_document()
    {
        using var raw = Raw();
        var components = raw.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var reference in References(raw.RootElement))
        {
            Assert.StartsWith("#/components/schemas/", reference, StringComparison.Ordinal);
            var componentName = reference["#/components/schemas/".Length..].Split('/')[0];
            Assert.True(components.TryGetProperty(componentName, out _),
                $"'{reference}' points at a component that does not exist.");
        }
    }

    private static IEnumerable<string> References(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;
                    foreach (var nested in References(property.Value))
                        yield return nested;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in References(item))
                        yield return nested;
                }

                break;
        }
    }

    [Theory]
    [InlineData("post", "/design/workflows/definitions/submit")]
    [InlineData("get", "/design/workflows/definitions/submit/schema")]
    [InlineData("get", "/design/activities/catalog")]
    [InlineData("post", "/publishing/workflows/{versionId}/publish")]
    public void The_authoring_and_publishing_path_is_published(string verb, string path)
    {
        using var raw = Raw();
        var paths = raw.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty(path, out var pathItem),
            $"{path} is absent from the published API surface, so a contracts-only consumer must probe for it.");
        Assert.True(pathItem.TryGetProperty(verb, out _), $"{verb.ToUpperInvariant()} {path} is not published.");
    }

    [Fact]
    public void The_publish_operation_documents_both_statuses_and_the_rule_between_them()
    {
        using var raw = Raw();
        var responses = raw.RootElement
            .GetProperty("paths")
            .GetProperty("/publishing/workflows/{versionId}/publish")
            .GetProperty("post")
            .GetProperty("responses");

        // 201 alone would be as wrong as 200 alone: it creates (201) or updates in place (200).
        Assert.True(responses.TryGetProperty("201", out var created));
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.Contains("201", created.GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undeclared_success_status_documents_default_rather_than_asserting_200()
    {
        using var raw = Raw();

        var withDefault = raw.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value.GetProperty("responses"))
            .Where(responses => responses.TryGetProperty("default", out _))
            .ToArray();

        // The point of the opt-in attribute: an endpoint that does not declare its status must not be
        // published as if it returned 200.
        Assert.NotEmpty(withDefault);
        Assert.All(withDefault, responses => Assert.False(responses.TryGetProperty("200", out _)));
    }

    [Fact]
    public void Route_constraints_are_stripped_and_path_parameters_declared()
    {
        using var raw = Raw();

        foreach (var path in raw.RootElement.GetProperty("paths").EnumerateObject())
        {
            Assert.DoesNotContain(":", path.Name, StringComparison.Ordinal);

            var declared = path.Value.EnumerateObject()
                .SelectMany(operation => operation.Value.TryGetProperty("parameters", out var parameters)
                    ? parameters.EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()!)
                    : [])
                .ToHashSet(StringComparer.Ordinal);

            foreach (var segment in path.Name.Split('/'))
            {
                if (segment.StartsWith('{') && segment.EndsWith('}'))
                    Assert.Contains(segment.Trim('{', '}'), declared);
            }
        }
    }

    /// <summary>
    /// Authentication is the first thing a consumer needs and the last thing it can guess. This endpoint
    /// was absent from the first cut because its <c>Configure()</c> queries the host for registered auth
    /// schemes; projecting against an empty host publishes it, flagged, instead of dropping it.
    /// </summary>
    [Fact]
    public void The_token_endpoint_is_published()
    {
        using var raw = Raw();

        var token = raw.RootElement.GetProperty("paths").EnumerateObject()
            .Single(path => path.Name.EndsWith("/identity/token", StringComparison.Ordinal));

        Assert.True(token.Value.TryGetProperty("get", out var operation));
        Assert.True(operation.GetProperty("x-elsa-configuration-dependent").GetBoolean());
    }

    /// <summary>
    /// An operation configured from host options publishes this build's defaults. Saying so is the whole
    /// point — a route a host can move must not read as a fixed fact.
    /// </summary>
    [Fact]
    public void Configuration_dependent_operations_say_so_in_their_description()
    {
        using var raw = Raw();

        var flagged = raw.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(operation => operation.Value)
            .Where(operation => operation.TryGetProperty("x-elsa-configuration-dependent", out var flag) && flag.GetBoolean())
            .ToArray();

        Assert.NotEmpty(flagged);
        Assert.All(flagged, operation =>
            Assert.Contains("defaults", operation.GetProperty("description").GetString()!, StringComparison.Ordinal));
    }

    [Fact]
    public void The_document_is_fingerprinted_in_the_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "manifest.json")));

        Assert.Equal(
            manifest.RootElement.GetProperty("openApi").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(DocumentPath())));
    }
}
