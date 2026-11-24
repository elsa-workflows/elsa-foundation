using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeSchemaPresenceSerializationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{ \"name\":\"Ren\\u00e9<+>\", \"n\":1.0, \"nil\":null }")]
    public void Artifact_bindings_keep_schema_presence_and_raw_text_with_opaque_identity_options(string? schema)
    {
        var type = new ValueTypeDescriptor("Acme.Dto", schema: Parse(schema), schemaVersion: 7);
        var binding = new RuntimeInputBinding("value", type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement("text", JsonBaseline.Options), ValueProtectionPolicy.InstanceInline));

        var written = RuntimeArtifactJson.Serialize(binding);
        using var document = JsonDocument.Parse(written);
        Assert.Equal(schema is not null, document.RootElement.GetProperty("targetType").TryGetProperty("schema", out _));
        var restored = RuntimeArtifactJson.Deserialize<RuntimeInputBinding>(written);
        Assert.Equal(type.Alias, restored.TargetType.Alias);
        Assert.Equal(type.SchemaVersion, restored.TargetType.SchemaVersion);
        Assert.Equal(schema is not null, restored.TargetType.Schema.HasValue);
        Assert.Equal(schema is not null, restored.Literal!.Type.Schema.HasValue);
        if (schema is not null)
        {
            Assert.Equal(schema, restored.TargetType.Schema!.Value.GetRawText());
            Assert.Equal(schema, restored.Literal.Type.Schema!.Value.GetRawText());
        }
    }

    [Theory]
    [InlineData("absent", false)]
    [InlineData("absent", true)]
    [InlineData("explicit-null", false)]
    [InlineData("explicit-null", true)]
    [InlineData("external", false)]
    [InlineData("external", true)]
    [InlineData("withheld", false)]
    [InlineData("withheld", true)]
    [InlineData("inline-null", false)]
    [InlineData("inline-null", true)]
    public void Schema_preservation_does_not_change_envelope_payload_placeholder_semantics(string kind, bool artifact)
    {
        var type = new ValueTypeDescriptor("Acme.Dto");
        var policy = ValueProtectionPolicy.InstanceInline;
        var source = kind switch
        {
            "absent" => ValueEnvelope.Absent(type, policy),
            "explicit-null" => ValueEnvelope.Null(type, policy),
            "external" => ValueEnvelope.External(type, new("fixture", "locator", new Dictionary<string, string>()), policy),
            "withheld" => ValueEnvelope.Withheld(type, WithheldValue.PolicyRequiresEncryption(), policy),
            _ => ValueEnvelope.Inline(type, Parse("null")!.Value, policy)
        };
        var written = artifact ? RuntimeArtifactJson.Serialize(source) : JsonSerializer.Serialize(source, JsonBaseline.Options);
        var restored = artifact ? RuntimeArtifactJson.Deserialize<ValueEnvelope>(written)
            : JsonSerializer.Deserialize<ValueEnvelope>(written, JsonBaseline.Options)!;

        Assert.Equal(source.Presence, restored.Presence);
        Assert.Equal(source.InlineValue.HasValue, restored.InlineValue.HasValue);
        Assert.Equal(source.ExternalReference?.StorageProfile, restored.ExternalReference?.StorageProfile);
        Assert.Equal(source.ExternalReference?.Locator, restored.ExternalReference?.Locator);
        Assert.Equal(source.WithheldValue, restored.WithheldValue);
        Assert.False(restored.Type.Schema.HasValue);
    }

    [Theory]
    [InlineData(DurableValueStorage.None, false)]
    [InlineData(DurableValueStorage.None, true)]
    [InlineData(DurableValueStorage.External, false)]
    [InlineData(DurableValueStorage.External, true)]
    [InlineData(DurableValueStorage.Custom, false)]
    [InlineData(DurableValueStorage.Custom, true)]
    public void Schema_preservation_keeps_non_inline_durable_payload_placeholders_absent(DurableValueStorage storage, bool artifact)
    {
        var external = storage == DurableValueStorage.External ? new DurableValueExternalReference("fixture", "locator", new Dictionary<string, string>()) : null;
        var source = new DurableValueState("value", "workflow", "value", new("json", null, null),
            storage == DurableValueStorage.None ? DurableValueLifecycle.None : DurableValueLifecycle.Instance,
            storage, null, external, null, DateTimeOffset.UnixEpoch, new Dictionary<string, string>());
        var written = artifact ? RuntimeArtifactJson.Serialize(source) : JsonSerializer.Serialize(source, JsonBaseline.Options);
        var restored = artifact ? RuntimeArtifactJson.Deserialize<DurableValueState>(written)
            : JsonSerializer.Deserialize<DurableValueState>(written, JsonBaseline.Options)!;

        Assert.Equal(storage, restored.Storage);
        Assert.False(restored.InlineValue.HasValue);
        Assert.Equal(external?.Locator, restored.ExternalReference?.Locator);
    }

    private static JsonElement? Parse(string? value)
    {
        if (value is null) return null;
        using var document = JsonDocument.Parse(value); return document.RootElement.Clone();
    }
}
