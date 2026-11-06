using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StimulusIngressSourceCodecTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Header_round_trip_preserves_opaque_utf16_and_case_distinct_metadata()
    {
        var source = Source();
        var restored = StimulusIngressSourceCodec.Decode(StimulusIngressSourceCodec.Encode(source));

        Assert.Equal(source.SourceId, restored.SourceId);
        Assert.Equal(source.ClientKey, restored.ClientKey);
        Assert.Equal(source.OriginScope.PersistenceScope, restored.OriginScope.PersistenceScope);
        Assert.Equal(source.OriginScope.TenantId, restored.OriginScope.TenantId);
        Assert.Equal(source.OriginScope.Partition, restored.OriginScope.Partition);
        Assert.Equal(source.Request.RequestedBy, restored.Request.RequestedBy);
        Assert.Equal(source.Request.Metadata["Key"], restored.Request.Metadata["Key"]);
        Assert.Equal(source.Request.Metadata["key"], restored.Request.Metadata["key"]);
        Assert.Equal(2, restored.Request.Metadata.Count);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIngressSource(source), RuntimeCheckpointCommitFingerprint.ComputeIngressSource(restored));
    }

    [Fact]
    public void Business_input_and_schema_remain_raw_json_outside_the_protocol_field_checks()
    {
        using var input = JsonDocument.Parse("{\"value\":\"business-value\",\"metadata\":{\"unknown\":1},\"version\":1,\"version\":2}");
        using var schema = JsonDocument.Parse("{\"$id\":\"document\",\"additionalProperties\":true,\"customKeyword\":{\"unknown\":1}}");
        var source = Source(input.RootElement, schema.RootElement);
        var header = StimulusIngressSourceCodec.Encode(source);
        Assert.Equal("business-value", header.GetProperty("request").GetProperty("input").GetProperty("value").GetString());
        var restored = StimulusIngressSourceCodec.Decode(header);

        Assert.Equal(input.RootElement.GetRawText(), restored.Request.Input!.Value.GetRawText());
        Assert.Equal(schema.RootElement.GetRawText(), restored.Request.PayloadType!.Schema!.Value.GetRawText());
        Assert.Equal(source.RequestFingerprint, restored.RequestFingerprint);
    }

    [Fact]
    public void Decoded_header_owns_its_input_and_schema_after_the_transport_document_is_disposed()
    {
        StimulusIngressSource restored;
        using (var input = JsonDocument.Parse("{\"value\":\"retained\"}"))
        using (var schema = JsonDocument.Parse("{\"type\":\"object\"}"))
        using (var transport = JsonDocument.Parse(StimulusIngressSourceCodec.Encode(Source(input.RootElement, schema.RootElement)).GetRawText()))
            restored = StimulusIngressSourceCodec.Decode(transport.RootElement);

        Assert.Equal("retained", restored.Request.Input!.Value.GetProperty("value").GetString());
        Assert.Equal("object", restored.Request.PayloadType!.Schema!.Value.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Header_round_trip_distinguishes_absent_input_from_explicit_json_null(bool present)
    {
        var source = Source(JsonSerializer.SerializeToElement<object?>(null), inputPresent: present);
        var restored = StimulusIngressSourceCodec.Decode(StimulusIngressSourceCodec.Encode(source));
        Assert.Equal(present, restored.Request.InputPresent);
        if (present) Assert.Equal(JsonValueKind.Null, restored.Request.Input!.Value.ValueKind);
        else Assert.Null(restored.Request.Input);
        Assert.Equal(source.RequestFingerprint, restored.RequestFingerprint);
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-version")]
    [InlineData("invalid-string")]
    [InlineData("partial-code-unit")]
    [InlineData("missing-input-presence")]
    [InlineData("contradictory-input-presence")]
    public void Invalid_protocol_headers_are_rejected_before_a_family_consumer_receives_them(string defect)
    {
        var json = JsonNode.Parse(StimulusIngressSourceCodec.Encode(Source()).GetRawText())!.AsObject();
        switch (defect)
        {
            case "missing-field": Assert.True(json.Remove("request")); break;
            case "unknown-field": json["futureProtocol"] = true; break;
            case "unknown-version": json["version"] = 2; break;
            case "invalid-string": json["sourceId"] = "!"; break;
            case "partial-code-unit": json["sourceId"] = "AA=="; break;
            case "missing-input-presence": Assert.True(json["request"]!.AsObject().Remove("inputPresent")); break;
            case "contradictory-input-presence": json["request"]!["inputPresent"] = false; break;
        }
        var wire = json.ToJsonString();
        if (defect == "duplicate-field") wire = "{\"version\":1," + wire[1..];
        using var document = JsonDocument.Parse(wire);
        Assert.Throws<InvalidDataException>(() => StimulusIngressSourceCodec.Decode(document.RootElement));
    }

    private static StimulusIngressSource Source(JsonElement? input = null, JsonElement? schema = null, bool inputPresent = true)
    {
        var scope = new PersistenceScope("scope-\ud803");
        var origin = new StimulusDeliveryScope(scope, "tenant-\ud802", new("partition-\ud804"), WorkflowRunKind.PublishedRun, null);
        var request = new StimulusIngressRequest(1, "event", "hash",
            inputPresent ? input ?? JsonSerializer.SerializeToElement(new { value = "business-value" }) : null,
            null, StimulusRoutingMode.StartAndResume, "caller-\ud805",
            new Dictionary<string, string> { ["Key"] = "first-\ud806", ["key"] = "second-\ud807" },
            schema is null ? null : new ValueTypeDescriptor("document", schema: schema), schema is null ? null : "json",
            null, StimulusStartKeyScope.Artifact, false, null);
        return new(1, "source-\ud800", origin, "key-\ud801", request, Now, new(scope, Now, [], [], [], []));
    }
}
