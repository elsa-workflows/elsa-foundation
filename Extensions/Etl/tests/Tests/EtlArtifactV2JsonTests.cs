using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;

namespace Elsa.Etl.Tests;

public sealed class EtlArtifactV2JsonTests
{
    [Fact]
    public void Http_pipeline_round_trips_as_canonical_typed_json()
    {
        var pipeline = EtlV2TestPipelines.Create();

        var document = EtlArtifactV2Json.Write(pipeline);
        var text = document.GetRawText();
        var restored = EtlArtifactV2Json.Read(Encoding.UTF8.GetBytes(text));
        var repeated = EtlArtifactV2Json.Write(restored);

        Assert.Equal(text, repeated.GetRawText());
        Assert.Equal(2, EtlArtifactV2Json.FormatVersion);
        Assert.Equal("httpJson", document.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("token", document.GetProperty("source").GetProperty("pagination").GetProperty("kind").GetString());
        Assert.Equal("http", document.GetProperty("routes").GetProperty("main").GetProperty("kind").GetString());
        Assert.Null(restored.Lookup);
        Assert.Equal(2, ((EtlHttpJsonSource)restored.Source).Fields.Count);
    }

    [Fact]
    public void Csv_source_optional_lookup_and_mixed_routes_round_trip()
    {
        var pipeline = new EtlPipelineV2(
            new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, Required: true)]),
            new EtlCsvLookup(
                "lookup",
                [new EtlColumn("key", EtlColumnType.String, Required: true), new EtlColumn("label", EtlColumnType.String, Required: false)],
                "id",
                "key"),
            new EtlRoutes(new EtlJsonlDestination("main-jsonl"), new EtlHttpDestination("dead-letter")),
            [
                new EtlOutputField("id", EtlFieldOrigin.Source, "id"),
                new EtlOutputField("label", EtlFieldOrigin.Lookup, "label")
            ],
            [],
            new EtlConnectorLimits(),
            new EtlHttpPolicy());

        var document = EtlArtifactV2Json.Write(pipeline);
        var restored = EtlArtifactV2Json.Read(document);

        Assert.Equal(document.GetRawText(), EtlArtifactV2Json.Write(restored).GetRawText());
        Assert.IsType<EtlCsvSource>(restored.Source);
        Assert.NotNull(restored.Lookup);
        Assert.IsType<EtlJsonlDestination>(restored.Routes.Main);
        Assert.IsType<EtlHttpDestination>(restored.Routes.Errors);
        Assert.Equal("id", restored.Lookup.SourceKeyField);
        Assert.Equal("key", restored.Lookup.LookupKeyField);
    }

    [Theory]
    [InlineData("singlePage")]
    [InlineData("nextLink")]
    public void Http_pagination_variants_round_trip(string kind)
    {
        var baseline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)baseline.Source;
        EtlHttpPagination pagination = kind == "singlePage"
            ? new EtlSinglePagePagination()
            : new EtlNextLinkPagination("/next");
        var pipeline = baseline with { Source = source with { Pagination = pagination } };

        var document = EtlArtifactV2Json.Write(pipeline);
        var restored = EtlArtifactV2Json.Read(document);

        Assert.Equal(kind, document.GetProperty("source").GetProperty("pagination").GetProperty("kind").GetString());
        Assert.Equal(document.GetRawText(), EtlArtifactV2Json.Write(restored).GetRawText());
        if (kind == "singlePage")
            Assert.IsType<EtlSinglePagePagination>(((EtlHttpJsonSource)restored.Source).Pagination);
        else
            Assert.IsType<EtlNextLinkPagination>(((EtlHttpJsonSource)restored.Source).Pagination);
    }

    [Fact]
    public void Http_source_can_use_jsonl_main_and_http_error_routes()
    {
        var baseline = EtlV2TestPipelines.Create();
        var pipeline = baseline with
        {
            Routes = new EtlRoutes(new EtlJsonlDestination("output"), new EtlHttpDestination("errors"))
        };

        var document = EtlArtifactV2Json.Write(pipeline);
        var restored = EtlArtifactV2Json.Read(document);

        Assert.IsType<EtlHttpJsonSource>(restored.Source);
        Assert.IsType<EtlJsonlDestination>(restored.Routes.Main);
        Assert.IsType<EtlHttpDestination>(restored.Routes.Errors);
    }

    [Fact]
    public void Reader_accepts_discriminators_after_other_properties_and_writer_restores_canonical_order()
    {
        var canonical = EtlArtifactV2Json.Write(EtlV2TestPipelines.Create());
        var node = CreateNode();
        MoveDiscriminatorToEnd(node["source"]!.AsObject());
        MoveDiscriminatorToEnd(node["source"]!["pagination"]!.AsObject());
        MoveDiscriminatorToEnd(node["routes"]!["main"]!.AsObject());

        var restored = ReadNode(node);

        Assert.Equal(canonical.GetRawText(), EtlArtifactV2Json.Write(restored).GetRawText());
    }

    [Fact]
    public void Reader_rejects_unknown_fields_at_root_and_nested_levels()
    {
        var root = CreateNode();
        root["futureOption"] = true;
        Assert.Throws<JsonException>(() => ReadNode(root));

        var nested = CreateNode();
        nested["source"]!["futureOption"] = true;
        Assert.Throws<JsonException>(() => ReadNode(nested));

        var wrongCase = CreateNode();
        wrongCase["source"]!["Binding"] = wrongCase["source"]!["binding"]!.DeepClone();
        wrongCase["source"]!.AsObject().Remove("binding");
        Assert.Throws<JsonException>(() => ReadNode(wrongCase));
    }

    [Fact]
    public void Reader_requires_root_and_shared_mapping_members_but_lookup_is_optional()
    {
        var missingSource = CreateNode();
        missingSource.AsObject().Remove("source");
        Assert.Throws<JsonException>(() => ReadNode(missingSource));

        var missingFieldName = CreateNode();
        missingFieldName["mapping"]![0]!.AsObject().Remove("name");
        Assert.Throws<JsonException>(() => ReadNode(missingFieldName));

        var missingFieldSelector = CreateNode();
        missingFieldSelector["source"]!["fields"]![0]!.AsObject().Remove("pointer");
        Assert.Throws<JsonException>(() => ReadNode(missingFieldSelector));

        var noLookup = CreateNode();
        noLookup.AsObject().Remove("lookup");
        Assert.Null(ReadNode(noLookup).Lookup);
    }

    [Fact]
    public void Reader_rejects_explicit_null_required_members_and_collection_entries()
    {
        foreach (var path in new[] { "source", "routes", "limits", "http", "mapping", "filters", "routes.main", "source.binding", "source.fields", "source.pagination", "source.fields.0", "source.fields.0.name", "mapping.0", "mapping.0.field" })
        {
            var node = CreateNode();
            var parts = path.Split('.');
            var parent = node;
            foreach (var part in parts[..^1])
                parent = parent is JsonArray array ? array[int.Parse(part)]! : parent[part]!;
            if (parent is JsonArray targetArray)
                targetArray[int.Parse(parts[^1])] = null;
            else
                parent[parts[^1]] = null;
            Assert.Throws<JsonException>(() => ReadNode(node));
        }

        var nullFilter = CreateNode();
        nullFilter["filters"]!.AsArray().Add((JsonNode?)null);
        Assert.Throws<JsonException>(() => ReadNode(nullFilter));
    }

    [Fact]
    public void Writer_rejects_null_required_members_and_collection_entries()
    {
        var p = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)p.Source;
        var invalid = new[]
        {
            p with { Source = null! }, p with { Routes = null! }, p with { Limits = null! },
            p with { Http = null! }, p with { Mapping = null! }, p with { Filters = null! },
            p with { Routes = new(null!, new EtlHttpDestination("errors")) },
            p with { Source = source with { Binding = null! } },
            p with { Source = source with { Fields = [null!] } },
            p with { Source = source with { Pagination = null! } },
            p with { Source = new EtlCsvSource("source", [null!]) },
            p with { Lookup = new("lookup", [null!], "id", "key") },
            p with { Mapping = [null!] }, p with { Filters = [null!] },
            p with { Mapping = [new(null!, EtlFieldOrigin.Source, "name")] }
        };
        foreach (var pipeline in invalid)
            Assert.Throws<JsonException>(() => EtlArtifactV2Json.Write(pipeline));
    }

    [Fact]
    public void Reader_requires_all_filter_members_and_rejects_invalid_filter_fields_as_json_errors()
    {
        using var value = JsonDocument.Parse("\"active\"");
        var complete = WithFilterValue(value.RootElement);
        var document = JsonNode.Parse(EtlArtifactV2Json.Write(complete).GetRawText())!;

        foreach (var member in new[] { "field", "comparison", "value" })
        {
            var missing = document.DeepClone();
            missing["filters"]![0]!.AsObject().Remove(member);
            Assert.Throws<JsonException>(() => ReadNode(missing));
        }

        var nullField = document.DeepClone();
        nullField["filters"]![0]!["field"] = null;
        Assert.Throws<JsonException>(() => ReadNode(nullField));

        var blankField = document.DeepClone();
        blankField["filters"]![0]!["field"] = "  ";
        Assert.Throws<JsonException>(() => ReadNode(blankField));

        var unknownMember = document.DeepClone();
        unknownMember["filters"]![0]!["Field"] = "active";
        Assert.Throws<JsonException>(() => ReadNode(unknownMember));
    }

    [Fact]
    public void Reader_rejects_unrecognized_source_destination_and_pagination_kinds()
    {
        var source = CreateNode();
        source["source"]!["kind"] = "futureSource";
        Assert.Throws<JsonException>(() => ReadNode(source));

        var destination = CreateNode();
        destination["routes"]!["main"]!["kind"] = "futureDestination";
        Assert.Throws<JsonException>(() => ReadNode(destination));

        var pagination = CreateNode();
        pagination["source"]!["pagination"]!["kind"] = "futurePagination";
        Assert.Throws<JsonException>(() => ReadNode(pagination));

        var missingKind = CreateNode();
        missingKind["source"]!.AsObject().Remove("kind");
        Assert.Throws<JsonException>(() => ReadNode(missingKind));
    }

    [Fact]
    public void Reader_rejects_unknown_enum_values_numeric_values_and_wrong_enum_case()
    {
        var unknown = CreateNode();
        unknown["source"]!["fields"]![0]!["type"] = "integer";
        Assert.Throws<JsonException>(() => ReadNode(unknown));

        var numeric = CreateNode();
        numeric["source"]!["fields"]![0]!["type"] = (int)EtlColumnType.Int64;
        Assert.Throws<JsonException>(() => ReadNode(numeric));

        var wrongCase = CreateNode();
        wrongCase["source"]!["fields"]![0]!["type"] = "Int64";
        Assert.Throws<JsonException>(() => ReadNode(wrongCase));
    }

    [Fact]
    public void Reader_rejects_duplicate_properties_recursively()
    {
        var text = EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText()
            .Replace("\"binding\":\"source\"", "\"binding\":\"source\",\"binding\":\"source\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Read(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Writer_rejects_duplicate_properties_inside_a_filter_literal()
    {
        using var value = JsonDocument.Parse("{\"key\":1,\"key\":2}");
        var pipeline = WithFilterValue(value.RootElement);

        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Write(pipeline));
    }

    [Fact]
    public void Reader_rejects_null_oversized_and_over_depth_documents_before_typed_read()
    {
        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Read(Encoding.UTF8.GetBytes("null")));
        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Read(new byte[EtlArtifactV2Json.MaximumByteLength + 1]));

        var baseText = EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText();
        var nestedValue = new string('[', 70) + "0" + new string(']', 70);
        var deepText = baseText.Replace("\"filters\":[]", $"\"filters\":[{{\"field\":\"name\",\"comparison\":\"equal\",\"value\":{nestedValue}}}]", StringComparison.Ordinal);
        using var deepDocument = JsonDocument.Parse(deepText, new JsonDocumentOptions { MaxDepth = 128 });

        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Read(deepDocument.RootElement));
    }

    [Fact]
    public void Writer_bounds_actual_encoded_bytes_for_ascii_and_escaped_strings()
    {
        using var emptyStringValue = JsonDocument.Parse("\"\"");
        var emptyFilterDocument = EtlArtifactV2Json.Write(WithFilterValue(emptyStringValue.RootElement));
        var emptyFilterBytes = Encoding.UTF8.GetByteCount(emptyFilterDocument.GetRawText());
        var nearLimitAsciiLength = EtlArtifactV2Json.MaximumByteLength - emptyFilterBytes - 1;
        using var asciiValue = JsonDocument.Parse(JsonSerializer.Serialize(new string('a', nearLimitAsciiLength)));
        var asciiDocument = EtlArtifactV2Json.Write(WithFilterValue(asciiValue.RootElement));
        Assert.Equal(EtlArtifactV2Json.MaximumByteLength - 1, Encoding.UTF8.GetByteCount(asciiDocument.GetRawText()));

        using var escapedValue = JsonDocument.Parse(JsonSerializer.Serialize(new string('"', 20_000)));
        var escapedDocument = EtlArtifactV2Json.Write(WithFilterValue(escapedValue.RootElement));
        Assert.InRange(Encoding.UTF8.GetByteCount(escapedDocument.GetRawText()), 40_000, EtlArtifactV2Json.MaximumByteLength);

        using var nonAsciiValue = JsonDocument.Parse(JsonSerializer.Serialize(new string('é', 20_000)));
        var nonAsciiDocument = EtlArtifactV2Json.Write(WithFilterValue(nonAsciiValue.RootElement));
        Assert.InRange(Encoding.UTF8.GetByteCount(nonAsciiDocument.GetRawText()), 40_000, EtlArtifactV2Json.MaximumByteLength);
    }

    [Fact]
    public void Writer_rejects_when_actual_serialized_bytes_exceed_the_limit()
    {
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(new string('x', EtlArtifactV2Json.MaximumByteLength)));

        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Write(WithFilterValue(value.RootElement)));
    }

    [Fact]
    public void Writer_rejects_over_depth_filter_literals()
    {
        var valueText = new string('[', 70) + "0" + new string(']', 70);
        using var value = JsonDocument.Parse(valueText, new JsonDocumentOptions { MaxDepth = 128 });

        Assert.Throws<JsonException>(() => EtlArtifactV2Json.Write(WithFilterValue(value.RootElement)));
    }

    private static JsonNode CreateNode() => JsonNode.Parse(EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText())!;

    private static EtlPipelineV2 ReadNode(JsonNode node) => EtlArtifactV2Json.Read(ToElement(node));

    private static EtlPipelineV2 WithFilterValue(JsonElement value) => EtlV2TestPipelines.Create() with
    {
        Filters = [new EtlFilter("name", EtlComparison.Equal, value)]
    };

    private static void MoveDiscriminatorToEnd(JsonObject value)
    {
        var discriminator = value["kind"]!.DeepClone();
        value.Remove("kind");
        value["kind"] = discriminator;
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
