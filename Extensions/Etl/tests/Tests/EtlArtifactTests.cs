using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;

namespace Elsa.Etl.Tests;

public sealed class EtlArtifactTests
{
    [Fact]
    public void Serialized_artifact_preserves_the_format_version_for_runtime_compatibility_checks()
    {
        var artifact = CreateArtifact(formatVersion: 37);

        var serialized = JsonSerializer.Serialize(artifact);
        var restored = JsonSerializer.Deserialize<EtlArtifact>(serialized);

        Assert.NotNull(restored);
        Assert.Equal(37, restored.FormatVersion);
    }

    [Fact]
    public void Deserialization_rejects_a_missing_saved_format_version()
    {
        const string savedArtifact = """
            {
              "ArtifactId": "artifact-1",
              "ContentHash": "hash",
              "DefinitionId": "definition-1",
              "DefinitionVersion": 1,
              "Pipeline": {}
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EtlArtifact>(savedArtifact));
    }

    [Fact]
    public void Pipeline_reader_rejects_unknown_fields_and_numeric_enum_values()
    {
        var document = JsonNode.Parse(EtlArtifactJson.Write(CreatePipeline()).GetRawText())!.AsObject();
        document["futureOption"] = true;

        Assert.Throws<JsonException>(() => EtlArtifactJson.Read(ToElement(document)));

        document.Remove("futureOption");
        document["sourceSchema"]![0]!["type"] = (int)EtlColumnType.String;

        Assert.Throws<JsonException>(() => EtlArtifactJson.Read(ToElement(document)));
    }

    [Fact]
    public void Typed_pipeline_hash_is_repeatable_and_matches_its_compact_json_bytes()
    {
        var document = EtlArtifactJson.Write(CreatePipeline());
        var repeatedDocument = EtlArtifactJson.Write(CreatePipeline());

        var hash = EtlArtifactJson.Hash(document);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.GetRawText())))
            .ToLowerInvariant();

        Assert.Equal(document.GetRawText(), repeatedDocument.GetRawText());
        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    [Fact]
    public void Artifact_retains_its_pipeline_document_after_the_source_document_is_disposed()
    {
        EtlArtifact artifact;
        using (var source = JsonDocument.Parse("""{"pipeline":{"version":1}}"""))
        {
            artifact = CreateArtifact(formatVersion: 1, pipeline: source.RootElement.GetProperty("pipeline"));
        }

        Assert.Equal(1, artifact.Pipeline.GetProperty("version").GetInt32());
    }

    private static EtlArtifact CreateArtifact(int formatVersion, JsonElement? pipeline = null) =>
        new(formatVersion, "artifact-1", "hash-1", "definition-1", 1, pipeline ?? EtlArtifactJson.Write(CreatePipeline()));

    private static EtlPipeline CreatePipeline() => new(
        new EtlFileBindings("source", "lookup", "output", "errors"),
        [new EtlColumn("id", EtlColumnType.Int64, Required: true)],
        [new EtlColumn("key", EtlColumnType.String, Required: true), new EtlColumn("label", EtlColumnType.String, Required: false)],
        "id",
        "key",
        [new EtlOutputField("id", EtlFieldOrigin.Source, "id"), new EtlOutputField("label", EtlFieldOrigin.Lookup, "label")],
        [],
        new EtlLimits());

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
