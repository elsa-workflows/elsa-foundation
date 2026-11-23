using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

public sealed class ArgumentValueJsonIntegrityTests
{
    [Fact]
    public void The_authored_data_member_is_JSON_and_detaches_raw_text_from_document_lifetimes()
    {
        const string raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>" }""";
        ArgumentValue argument;
        using (var document = JsonDocument.Parse(raw)) argument = new(document.RootElement, "Literal");
        Assert.Equal(typeof(JsonElement?), typeof(ArgumentValue).GetProperty(nameof(ArgumentValue.Value))!.PropertyType);
        Assert.Equal(raw, argument.Value!.Value.GetRawText());
        var wire = JsonSerializer.SerializeToElement(argument, JsonBaseline.Options);
        Assert.Equal(raw, wire.GetProperty("value").GetRawText());
        var restored = wire.Deserialize<ArgumentValue>(JsonBaseline.Options)!;
        Assert.Equal(raw, restored.Value!.Value.GetRawText());
        using (var replacement = JsonDocument.Parse("{ \"replacement\":2.00 }")) argument = argument with { Value = replacement.RootElement };
        Assert.Equal("{ \"replacement\":2.00 }", argument.Value!.Value.GetRawText());
    }

    [Fact]
    public void A_CLR_producer_applies_its_scoped_contract_before_the_document_serializer_sees_it()
    {
        var argument = ArgumentValue.FromClr(new ScopedLiteral { Name = "René <+>" }, "Literal");
        Assert.Equal("René <+>", argument.Value!.Value.GetProperty("EXTERNAL_NAME").GetString());
        Assert.False(argument.Value.Value.TryGetProperty("name", out _));
        var wire = JsonSerializer.SerializeToElement(argument, JsonBaseline.Options);
        var restored = wire.Deserialize<ArgumentValue>(JsonBaseline.Options)!;
        Assert.Equal(argument.Value.Value.GetRawText(), restored.Value!.Value.GetRawText());
        Assert.Equal("René <+>", JsonConversionContract.Deserialize<ScopedLiteral>(restored.Value.Value)!.Name);
    }

    [Fact]
    public void The_carrier_preserves_its_existing_null_and_undefined_binding_states()
    {
        Assert.Null(ArgumentValue.FromClr(null).Value);
        Assert.Null(new ArgumentValue(null).Value);
        Assert.Equal(JsonValueKind.Undefined, ArgumentValue.FromClr(default(JsonElement)).Value!.Value.ValueKind);
        using var document = JsonDocument.Parse("null");
        Assert.Equal(JsonValueKind.Null, new ArgumentValue(document.RootElement).Value!.Value.ValueKind);
    }

    [JsonConversion(MemberMappings = ["EXTERNAL_NAME=Name"])]
    public sealed class ScopedLiteral
    {
        public string Name { get; set; } = string.Empty;
    }
}
