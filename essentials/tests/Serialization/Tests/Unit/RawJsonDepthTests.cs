using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class RawJsonDepthTests
{
    private const string Leaf = """{ "Name":"\u0041 <+>", "n":1.00, "nil":null, "large":9007199254740993 }""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_serializer_roundtrips_raw_values_at_the_existing_root_depth_limit(bool objects)
    {
        var serializer = CreateSerializer();
        Assert.Equal(0, serializer.GetOptions().MaxDepth);
        var raw = AtDepth(64, objects);
        var source = Parse(raw);
        var written = serializer.Serialize(source);
        Assert.Equal(raw, written);
        Assert.Equal(raw, serializer.Deserialize<JsonElement>(written).GetRawText());
        Assert.Equal(raw, serializer.SerializeToElement(source).GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_serializer_refuses_raw_values_beyond_its_root_depth_limit(bool objects)
    {
        var serializer = CreateSerializer();
        var source = Parse(AtDepth(65, objects));
        AssertDepthRefusal(() => serializer.Serialize(source));
        AssertDepthRefusal(() => serializer.SerializeToElement(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_serializer_counts_the_enclosing_carrier_depth(bool objects)
    {
        var serializer = CreateSerializer();
        var raw = AtDepth(63, objects);
        var source = new RawEnvelope { Value = Parse(raw) };
        var written = serializer.Serialize(source);
        Assert.Equal("{\"value\":" + raw + "}", written);
        Assert.Equal(raw, serializer.Deserialize<RawEnvelope>(written).Value.GetRawText());
        Assert.Equal(raw, serializer.SerializeToElement(source).GetProperty("value").GetRawText());

        var tooDeep = new RawEnvelope { Value = Parse(AtDepth(64, objects)) };
        AssertDepthRefusal(() => serializer.Serialize(tooDeep));
        AssertDepthRefusal(() => serializer.SerializeToElement(tooDeep));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_nullable_carriers_apply_the_same_depth_limit_and_keep_raw_text(bool objects)
    {
        var serializer = CreateSerializer();
        var raw = AtDepth(63, objects);
        var written = serializer.Serialize(new NullableEnvelope { Value = Parse(raw) });
        Assert.Equal("{\"value\":" + raw + "}", written);
        Assert.Equal(raw, serializer.Deserialize<NullableEnvelope>(written).Value!.Value.GetRawText());
        var tooDeep = new NullableEnvelope { Value = Parse(AtDepth(64, objects)) };
        AssertDepthRefusal(() => serializer.Serialize(tooDeep));
        AssertDepthRefusal(() => serializer.SerializeToElement(tooDeep));
    }

    [Fact]
    public void Nullable_carriers_keep_absent_and_explicit_JSON_null_read_states()
    {
        var serializer = CreateSerializer();
        Assert.Null(serializer.Deserialize<NullableEnvelope>("{}").Value);
        Assert.Equal(JsonValueKind.Null, serializer.Deserialize<NullableEnvelope>("{\"value\":null}").Value!.Value.ValueKind);
        Assert.Equal("{\"value\":null}", serializer.Serialize(new NullableEnvelope()));
        Assert.Equal("{\"value\":null}", serializer.Serialize(new NullableEnvelope { Value = Parse("null") }));
    }

    [Fact]
    public void Depth_validation_visits_later_siblings_and_both_container_kinds()
    {
        var serializer = CreateSerializer();
        var raw = "{\"first\":[],\"last\":" + AtDepth(63, objects: false) + "}";
        Assert.Equal(raw, serializer.Deserialize<JsonElement>(serializer.Serialize(Parse(raw))).GetRawText());
        var tooDeep = "[{}," + AtDepth(64, objects: true) + "]";
        AssertDepthRefusal(() => serializer.Serialize(Parse(tooDeep)));
    }

    [Theory]
    [InlineData(4, 64)]
    [InlineData(1000, 4)]
    public void Raw_values_respect_both_explicit_writer_and_serializer_limits_before_writing(int writerLimit, int serializerLimit)
    {
        var options = JsonBaseline.CreateCopy();
        options.MaxDepth = serializerLimit;
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = writerLimit });
        writer.WriteStartObject();
        writer.WritePropertyName("outer");
        writer.WriteStartArray();
        Assert.Equal(2, writer.CurrentDepth);

        var raw = AtDepth(2, objects: true);
        JsonSerializer.Serialize(writer, Parse(raw), options);
        AssertDepthRefusal(() => JsonSerializer.Serialize(writer, Parse(AtDepth(3, objects: false)), options));
        // A depth refusal must precede the raw write and leave the caller's writer usable.
        writer.WriteNullValue();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        using var roundtrip = JsonDocument.Parse(buffer.WrittenMemory, new JsonDocumentOptions { MaxDepth = 4 });
        var values = roundtrip.RootElement.GetProperty("outer");
        Assert.Equal(raw, values[0].GetRawText());
        Assert.Equal(JsonValueKind.Null, values[1].ValueKind);
    }

    [Fact]
    public void Scalar_raw_values_fit_at_the_container_depth_limit()
    {
        var options = JsonBaseline.CreateCopy();
        options.MaxDepth = 64;
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = 2 });
        writer.WriteStartArray();
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, Parse("1.00"), options);
        JsonSerializer.Serialize(writer, Parse("null"), options);
        writer.WriteEndArray();
        writer.WriteEndArray();
        writer.Flush();
        using var roundtrip = JsonDocument.Parse(buffer.WrittenMemory, new JsonDocumentOptions { MaxDepth = 2 });
        Assert.Equal("1.00", roundtrip.RootElement[0][0].GetRawText());
        Assert.Equal(JsonValueKind.Null, roundtrip.RootElement[0][1].ValueKind);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(128)]
    public void An_explicit_serializer_depth_is_respected_without_hard_coding_the_baseline_limit(int limit)
    {
        var options = JsonBaseline.CreateCopy();
        options.MaxDepth = limit;
        var raw = AtDepth(limit, objects: false);
        Assert.Equal(raw, JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(Parse(raw), options), options).GetRawText());
        AssertDepthRefusal(() => JsonSerializer.Serialize(Parse(AtDepth(limit + 1, objects: true)), options));
    }

    private static void AssertDepthRefusal(Action write)
    {
        var error = Assert.Throws<JsonConversionException>(write);
        Assert.Equal("json-depth-exceeded", error.Code);
        Assert.Equal("$", error.Path);
        Assert.DoesNotContain("Name", error.Message, StringComparison.Ordinal);
    }

    private static JsonPayloadSerializer CreateSerializer() => new(new JsonPayloadConverterRegistry());

    private static JsonElement Parse(string source)
    {
        using var document = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 1000 });
        return document.RootElement.Clone();
    }

    private static string AtDepth(int depth, bool objects) =>
        string.Concat(Enumerable.Repeat(objects ? "{\"next\":" : "[", depth - 1)) + Leaf +
        string.Concat(Enumerable.Repeat(objects ? "}" : "]", depth - 1));

    public sealed class RawEnvelope
    {
        public JsonElement Value { get; set; }
    }

    public sealed class NullableEnvelope
    {
        [JsonConverter(typeof(NullableJsonElementConverter))]
        public JsonElement? Value { get; set; }
    }
}
