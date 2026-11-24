using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class ValueTypeDescriptorJsonTests
{
    [Theory]
    [InlineData("baseline")]
    [InlineData("copy")]
    [InlineData("host")]
    [InlineData("payload")]
    public void Shared_boundaries_preserve_absent_null_and_raw_constructor_schemas(string boundary)
    {
        var options = boundary switch
        {
            "baseline" => JsonBaseline.Options,
            "payload" => new JsonPayloadSerializer(new JsonPayloadConverterRegistry()).GetOptions(),
            "host" => HostOptions(),
            _ => JsonBaseline.CreateCopy()
        };
        const string raw = """{ "Name":"Ren\u00e9<+>", "n":1.0, "large":9007199254740993, "nil":null }""";
        foreach (var schema in new[] { null, "null", raw })
        {
            var json = "{\"alias\":\"Acme.Dto\",\"collectionKind\":0,\"schemaVersion\":7" +
                       (schema is null ? "}" : ",\"schema\":" + schema + "}");
            var value = JsonSerializer.Deserialize<ValueTypeDescriptor>(json, options)!;
            Assert.Equal(schema is not null, value.Schema.HasValue);
            if (schema is not null) Assert.Equal(schema, value.Schema!.Value.GetRawText());
            var written = JsonSerializer.Serialize(value, options);
            using var document = JsonDocument.Parse(written);
            Assert.Equal(schema is not null, document.RootElement.TryGetProperty("schema", out _));
            var restored = JsonSerializer.Deserialize<ValueTypeDescriptor>(written, options)!;
            Assert.Equal(value.Alias, restored.Alias);
            Assert.Equal(value.CollectionKind, restored.CollectionKind);
            Assert.Equal(value.SchemaVersion, restored.SchemaVersion);
            Assert.True(JsonCanonicalForm.Equals(value.Schema, restored.Schema));
            if (schema is not null) Assert.Equal(schema, restored.Schema!.Value.GetRawText());
        }
    }

    [Fact]
    public void Configuring_host_options_is_idempotent_and_keeps_nullable_placeholder_semantics()
    {
        var options = HostOptions();
        var converters = options.Converters.ToArray();
        JsonBaseline.Configure(options);
        Assert.Equal(converters, options.Converters);
        Assert.Null(JsonSerializer.Deserialize<NullablePlaceholder>("{\"value\":null}", options)!.Value);
        Assert.Null(JsonSerializer.Deserialize<ValueTypeDescriptor>("null", options));
        Assert.Equal("null", JsonSerializer.Serialize<ValueTypeDescriptor?>(null, options));
        Assert.Throws<ArgumentNullException>(() => JsonSerializer.Deserialize<ValueTypeDescriptor>("{}", options));
        var required = HostOptions(); required.RespectRequiredConstructorParameters = true;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ValueTypeDescriptor>("{}", required));
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize<ValueTypeDescriptor>("{\"alias\":\"Acme.Dto\",\"schemaVersion\":0}", options));
    }

    [Fact]
    public void Descriptor_shape_keeps_active_identity_and_enum_converters_without_decoding_raw_schema()
    {
        var options = JsonBaseline.CreateCopy();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new IdentityStringConverter());
        const string raw = """{ "name":"identity:kept", "nil":null }""";
        var value = new ValueTypeDescriptor("Acme.Dto", CollectionKind.List, Parse(raw), 7, new("String", CollectionKind.Array));

        var written = JsonSerializer.SerializeToElement(value, options);
        Assert.Equal("identity:Acme.Dto", written.GetProperty("alias").GetString());
        Assert.Equal("list", written.GetProperty("collectionKind").GetString());
        Assert.Equal("identity:String", written.GetProperty("element").GetProperty("alias").GetString());
        Assert.Equal(raw, written.GetProperty("schema").GetRawText());
        var restored = written.Deserialize<ValueTypeDescriptor>(options)!;
        Assert.Equal(value.Alias, restored.Alias);
        Assert.Equal(value.CollectionKind, restored.CollectionKind);
        Assert.Equal(value.Element, restored.Element);
        Assert.Equal(raw, restored.Schema!.Value.GetRawText());
    }

    [Fact]
    public void Generated_owner_context_and_composed_module_metadata_keep_present_null_schema()
    {
        var context = new DescriptorBoundaryContext(JsonBaseline.CreateCopy());
        var value = new DescriptorBoundary(new("Acme.Dto", schema: Parse("null")));
        var ownInfo = context.GetTypeInfo(typeof(DescriptorBoundary))!;
        var json = JsonSerializer.Serialize(value, ownInfo);
        var own = Assert.IsType<DescriptorBoundary>(JsonSerializer.Deserialize(json, ownInfo));
        Assert.Equal(JsonValueKind.Null, own.Type.Schema!.Value.ValueKind);

        var host = HostOptions();
        var info = JsonModuleMetadata.Resolve(context, typeof(DescriptorBoundary), host, "Elsa.Serialization.Tests.Unit")!;
        var module = Assert.IsType<DescriptorBoundary>(JsonSerializer.Deserialize(JsonSerializer.Serialize(value, info), info));
        Assert.Equal(JsonValueKind.Null, module.Type.Schema!.Value.ValueKind);
    }

    [Fact]
    public void Generated_context_only_enum_metadata_survives_the_schema_adapter()
    {
        var context = new StringEnumDescriptorBoundaryContext(JsonBaseline.CreateCopy());
        var info = context.GetTypeInfo(typeof(DescriptorBoundary))!;
        var value = new DescriptorBoundary(new("Acme.Dto", CollectionKind.List, Parse("null"), 7,
            new("String", CollectionKind.Array)));

        var json = JsonSerializer.Serialize(value, info);
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type");
        Assert.Equal("List", type.GetProperty("collectionKind").GetString());
        Assert.Equal("Array", type.GetProperty("element").GetProperty("collectionKind").GetString());
        var restored = Assert.IsType<DescriptorBoundary>(JsonSerializer.Deserialize(json, info));
        Assert.Equal(value.Type.CollectionKind, restored.Type.CollectionKind);
        Assert.Equal(value.Type.Element, restored.Type.Element);
        Assert.Equal(JsonValueKind.Null, restored.Type.Schema!.Value.ValueKind);
    }

    [Fact]
    public void Resolver_only_nested_shapes_property_settings_and_callbacks_survive_the_schema_adapter()
    {
        var resolver = new DescriptorOwnerResolver();
        var options = JsonBaseline.CreateCopy(); options.TypeInfoResolver = resolver;
        const string raw = """{ "name":"Ren\u00e9", "n":1.0, "nil":null }""";
        var value = new ValueTypeDescriptor("Acme.Dto", CollectionKind.List, Parse(raw), 7, new("String", CollectionKind.Array));

        var json = JsonSerializer.SerializeToElement(value, options);
        Assert.Equal("kind:List", json.GetProperty("collectionKind").GetString());
        Assert.Equal("reference:String:Array", json.GetProperty("element").GetString());
        Assert.Equal("7", json.GetProperty("schemaVersion").GetString());
        Assert.Equal(raw, json.GetProperty("ownerSchema").GetRawText());
        Assert.False(json.TryGetProperty("schema", out _));
        Assert.Equal(1, resolver.Serialized);
        var restored = json.Deserialize<ValueTypeDescriptor>(options)!;
        Assert.Equal(value.Alias, restored.Alias);
        Assert.Equal(value.CollectionKind, restored.CollectionKind);
        Assert.Equal(value.Element, restored.Element);
        Assert.Equal(value.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(raw, restored.Schema!.Value.GetRawText());
        Assert.Equal(1, resolver.Deserialized);
    }

    [Fact]
    public void Schema_raw_writes_respect_enclosing_depth_and_constructor_reads_refuse_excess_depth()
    {
        var options = JsonBaseline.CreateCopy(); options.MaxDepth = 3;
        var allowed = new ValueTypeDescriptor("Acme.Dto", schema: Parse("[[]]"));
        var written = JsonSerializer.Serialize(allowed, options);
        Assert.Equal("[[]]", JsonSerializer.Deserialize<ValueTypeDescriptor>(written, options)!.Schema!.Value.GetRawText());
        var excessive = new ValueTypeDescriptor("Acme.Dto", schema: Parse("[[[]]]"));
        Assert.Throws<JsonConversionException>(() => JsonSerializer.Serialize(excessive, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ValueTypeDescriptor>("{\"alias\":\"Acme.Dto\",\"schema\":[[[]]]}", options));
    }

    [Fact]
    public void Independent_hash_serialization_and_opaque_projection_distinguish_absent_and_null_schema()
    {
        var absent = new ValueTypeDescriptor("Acme.Dto");
        var nil = new ValueTypeDescriptor("Acme.Dto", schema: Parse("null"));
        Assert.False(JsonHashInput.SerializeToElement(absent).TryGetProperty("schema", out _));
        Assert.Equal(JsonValueKind.Null, JsonHashInput.SerializeToElement(nil).GetProperty("schema").ValueKind);
        Assert.NotEqual(JsonHashInput.Hash(absent), JsonHashInput.Hash(nil));
        var absentOpaque = JsonHashInput.ProjectOpaqueClr(absent, _ => null);
        var nilOpaque = JsonHashInput.ProjectOpaqueClr(nil, _ => null);
        Assert.False(absentOpaque.TryGetProperty("schema", out _));
        Assert.Equal(JsonValueKind.Null, nilOpaque.GetProperty("schema").ValueKind);
        Assert.NotEqual(JsonHashInput.Hash(absentOpaque), JsonHashInput.Hash(nilOpaque));
    }

    private static JsonSerializerOptions HostOptions()
    {
        var options = new JsonSerializerOptions(); JsonBaseline.Configure(options); return options;
    }
    private static JsonElement Parse(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private sealed record NullablePlaceholder(JsonElement? Value);
    private sealed class IdentityStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()![9..];
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue("identity:" + value);
    }

    private sealed class DescriptorOwnerResolver : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver reflection = new();
        public int Serialized { get; private set; }
        public int Deserialized { get; private set; }
        public DescriptorOwnerResolver() => reflection.Modifiers.Add(info =>
        {
            if (info.Type != typeof(ValueTypeDescriptor) || info.Kind != JsonTypeInfoKind.Object) return;
            info.Properties.Single(property => property.PropertyType == typeof(JsonElement?)).Name = "ownerSchema";
            info.Properties.Single(property => property.PropertyType == typeof(int?)).NumberHandling =
                JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString;
            info.OnSerialized = _ => Serialized++;
            info.OnDeserialized = _ => Deserialized++;
        });
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(CollectionKind)
            ? JsonMetadataServices.CreateValueInfo<CollectionKind>(options, new OwnerCollectionKindConverter())
            : type == typeof(TypeReference)
                ? JsonMetadataServices.CreateValueInfo<TypeReference>(options, new OwnerTypeReferenceConverter())
                : reflection.GetTypeInfo(type, options);
    }

    private sealed class OwnerCollectionKindConverter : JsonConverter<CollectionKind>
    {
        public override CollectionKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Enum.Parse<CollectionKind>(reader.GetString()![5..]);
        public override void Write(Utf8JsonWriter writer, CollectionKind value, JsonSerializerOptions options) => writer.WriteStringValue("kind:" + value);
    }
    private sealed class OwnerTypeReferenceConverter : JsonConverter<TypeReference>
    {
        public override TypeReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var fields = reader.GetString()!.Split(':');
            return new(fields[1], Enum.Parse<CollectionKind>(fields[2]));
        }
        public override void Write(Utf8JsonWriter writer, TypeReference value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"reference:{value.Alias}:{value.CollectionKind}");
    }
}

public sealed record DescriptorBoundary(ValueTypeDescriptor Type);
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorBoundary))]
internal partial class DescriptorBoundaryContext : JsonSerializerContext;
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DescriptorBoundary))]
internal partial class StringEnumDescriptorBoundaryContext : JsonSerializerContext;
