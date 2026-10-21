using System.Text.Json;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Etl.Tests;

public sealed class EtlRuntimeJsonTests
{
    [Fact]
    public void Write_and_read_preserve_opaque_utf16_code_units()
    {
        var value = new RuntimeJsonDocument("ordinary\0\uD800\uDC00\uDFFF\uDC00", RuntimeJsonStatus.Ready, null);

        var json = EtlRuntimeJson.Write(value);
        var roundTrip = EtlRuntimeJson.Read<RuntimeJsonDocument>(json, 4096);

        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public void JsonElement_round_trip_survives_ordinary_state_serialization()
    {
        var value = new RuntimeJsonDocument("opaque\uD800tail", RuntimeJsonStatus.Ready, null);
        var element = EtlRuntimeJson.WriteElement(value);
        var stateJson = JsonSerializer.Serialize(new RuntimeJsonState(1, element));
        var restoredState = JsonSerializer.Deserialize<RuntimeJsonState>(stateJson)!;

        var roundTrip = EtlRuntimeJson.Read<RuntimeJsonDocument>(restoredState.Document, 4096);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public void Write_preserves_the_existing_ef_v1_wire_shape()
    {
        var value = new RuntimeJsonDocument("A", RuntimeJsonStatus.Ready, null);
        const string expected = "{\"name\":\"u16:QQA=\",\"status\":\"ready\",\"optional\":null}";

        Assert.Equal(expected, EtlRuntimeJson.Write(value));
        Assert.Equal(expected, EtlRuntimeRecordCodec.Write(value));
    }

    [Fact]
    public void Read_rejects_unknown_members_and_noncanonical_string_values()
    {
        const string unknownMember = "{\"name\":\"u16:QQA=\",\"status\":\"ready\",\"optional\":null,\"extra\":true}";
        const string malformedUtf16 = "{\"name\":\"u16:QQ==\",\"status\":\"ready\",\"optional\":null}";
        const string noncanonicalBase64 = "{\"name\":\"u16:QQA= \",\"status\":\"ready\",\"optional\":null}";
        const string ordinaryStringFallback = "{\"name\":\"A\",\"status\":\"ready\",\"optional\":null}";

        Assert.Throws<JsonException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(unknownMember, 4096));
        Assert.Throws<JsonException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(malformedUtf16, 4096));
        Assert.Throws<JsonException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(noncanonicalBase64, 4096));
        Assert.Throws<JsonException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(ordinaryStringFallback, 4096));
    }

    [Fact]
    public void Read_rejects_numeric_enum_values()
    {
        const string json = "{\"name\":\"u16:QQA=\",\"status\":0,\"optional\":null}";

        Assert.Throws<JsonException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(json, 4096));
    }

    [Fact]
    public void Read_enforces_byte_bounds_for_strings_and_elements()
    {
        var json = EtlRuntimeJson.Write(new RuntimeJsonDocument("bounded", RuntimeJsonStatus.Ready, null));
        using var document = JsonDocument.Parse(json);

        Assert.Throws<InvalidDataException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(json, 8));
        Assert.Throws<InvalidDataException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(document.RootElement, 8));
        Assert.Throws<InvalidDataException>(() => EtlRuntimeJson.Read<RuntimeJsonDocument>(default(JsonElement), 4096));
    }

    private sealed record RuntimeJsonDocument(string Name, RuntimeJsonStatus Status, string? Optional);
    private sealed record RuntimeJsonState(int FormatVersion, JsonElement Document);
    private enum RuntimeJsonStatus { Ready }
}
