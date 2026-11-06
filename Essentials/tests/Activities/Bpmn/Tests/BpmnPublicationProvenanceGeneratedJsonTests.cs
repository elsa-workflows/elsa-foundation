using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnPublicationProvenanceGeneratedJsonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_external_generated_reader_preserves_legacy_and_authored_provenance(bool respectRequiredConstructorParameters)
    {
        // This context lives in the Tests assembly, outside the owning Core assembly. Pass its actual generated
        // JsonTypeInfo directly: no reflection fallback or resolver modification can hide an inaccessible constructor.
        var context = new BpmnPublicationProvenanceJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { RespectRequiredConstructorParameters = respectRequiredConstructorParameters });
        var typeInfo = context.BpmnModelPublicationProvenance;
        var legacy = JsonSerializer.Deserialize(
            """{"versionId":"version-1","version":"1.0.0"}""", typeInfo)!;
        Assert.Equal("version-1", legacy.VersionId);
        Assert.Equal("1.0.0", legacy.Version);
        Assert.Null(legacy.DefinitionId);
        Assert.Empty(legacy.Layout);
        Assert.Empty(legacy.ActivityPresentation);

        var originalConstructor = new BpmnModelPublicationProvenance("version-1", "1.0.0");
        var legacyJson = JsonSerializer.Serialize(originalConstructor, typeInfo);
        using var legacyDocument = JsonDocument.Parse(legacyJson);
        Assert.False(legacyDocument.RootElement.TryGetProperty("definitionId", out _));
        Assert.Null(JsonSerializer.Deserialize(legacyJson, typeInfo)!.DefinitionId);

        var authored = BpmnModelPublicationProvenance.ForAuthoredDefinition(" orders ", "version-2", "2.0.0");
        var authoredJson = JsonSerializer.Serialize(authored, typeInfo);
        var restored = JsonSerializer.Deserialize(authoredJson, typeInfo)!;
        Assert.Equal(" orders ", restored.DefinitionId);
        Assert.Equal("version-2", restored.VersionId);
        Assert.Equal("2.0.0", restored.Version);
        Assert.Empty(restored.Layout);
        Assert.Empty(restored.ActivityPresentation);
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize(
            """{"versionId":"version-1","version":"1.0.0","definitionId":" "}""", typeInfo));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BpmnModelPublicationProvenance))]
internal partial class BpmnPublicationProvenanceJsonContext : JsonSerializerContext
{
}
