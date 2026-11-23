using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Elsa.Serialization.Testing;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class ObjectConversionContractTests
{
    [Fact]
    public void A_dictionary_is_a_document_and_reports_unknown_members_without_changing_it()
    {
        var source = new Dictionary<string, object?> { ["customerName"] = "Ada", ["Unknown"] = 5 };
        var warnings = new List<JsonConversionDiagnostic>();
        var converter = SerializationServices.ObjectConverter();
        var result = converter.ConvertTo<Order>(source, new ObjectConverterOptions { ReportJsonDiagnostic = warnings.Add });
        Assert.Equal("Ada", result!.CustomerName);
        Assert.Equal(5, source["Unknown"]);
        Assert.Equal("json-unmatched-member", Assert.Single(warnings).Code);
    }

    [Theory]
    [InlineData("\"true\"", typeof(bool))]
    [InlineData("\"5\"", typeof(int))]
    [InlineData("5.0", typeof(int))]
    [InlineData("\"2026-01-01T12:30:00\"", typeof(DateTimeOffset))]
    public void JSON_tokens_cannot_escape_the_contract(string json, Type target)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.False(SerializationServices.ObjectConverter().TryConvertTo(doc.RootElement, target).IsSuccess);
    }

    [Fact]
    public void CLR_scalar_text_and_floating_point_values_cannot_be_implicitly_parsed_or_narrowed()
    {
        var converter = SerializationServices.ObjectConverter();
        Assert.False(converter.TryConvertTo("true", typeof(bool)).IsSuccess);
        Assert.False(converter.TryConvertTo("5", typeof(int)).IsSuccess);
        Assert.False(converter.TryConvertTo(5.0, typeof(int)).IsSuccess);
        Assert.False(converter.TryConvertTo(5.0m, typeof(int)).IsSuccess);
        Assert.Equal(5L, converter.ConvertTo<long>(5));
    }

    [Fact]
    public void A_scoped_contract_is_used_for_the_read_and_temporal_values_have_no_local_fallback()
    {
        var warnings = new List<JsonConversionDiagnostic>();
        var member = typeof(Inputs).GetProperty(nameof(Inputs.Order))!;
        var options = new ObjectConverterOptions { ConversionContract = JsonConversionContract.For(typeof(Order), member), ReportJsonDiagnostic = warnings.Add };
        using var document = JsonDocument.Parse("{\"CustomerName\":\"Ada\"}");
        Assert.Equal("Ada", SerializationServices.ObjectConverter().ConvertTo<Order>(document.RootElement, options)!.CustomerName);
        Assert.Empty(warnings);
        Assert.False(SerializationServices.ObjectConverter().TryConvertTo(new DateTime(2026, 1, 1), typeof(DateTime)).IsSuccess);
    }

    public sealed class Order { public string? CustomerName { get; set; } }
    private sealed class Inputs { [JsonConversion(NamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.Unspecified)] public Order? Order { get; set; } }
}
