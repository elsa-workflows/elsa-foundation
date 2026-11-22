using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonConversionContractTests
{
    [Fact]
    public void Default_round_trips_camel_case_and_warns_without_rewriting_carried_data()
    {
        var contract = JsonConversionContract.For(typeof(Order));
        var written = contract.Write(new Order { CustomerName = "Ann", Total = 5 }, typeof(Order));
        var warnings = new List<JsonConversionDiagnostic>();
        var result = (Order)contract.Read(written, typeof(Order), warnings.Add)!;
        Assert.Equal("Ann", result.CustomerName);
        Assert.Equal(5, result.Total);
        Assert.Empty(warnings);
        var input = Element("{\"CustomerName\":\"Ann\",\"unknown\":null}");
        var before = input.GetRawText();
        result = (Order)contract.Read(input, typeof(Order), warnings.Add)!;
        Assert.Null(result.CustomerName);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, x => Assert.Equal("json-unmatched-member", x.Code));
        Assert.Equal(before, input.GetRawText());
    }

    [Fact]
    public void Member_overrides_do_not_leak_between_inputs_of_the_same_type()
    {
        var one = typeof(Inputs).GetProperty(nameof(Inputs.One))!;
        var two = typeof(Inputs).GetProperty(nameof(Inputs.Two))!;
        var input = Element("{\"CUSTOMERNAME\":\"Ann\"}");
        Assert.Equal("Ann", ((Order)JsonConversionContract.For(typeof(Order), one).Read(input, typeof(Order))!).CustomerName);
        Assert.Null(((Order)JsonConversionContract.For(typeof(Order), two).Read(input, typeof(Order))!).CustomerName);
        Assert.Same(JsonConversionContract.For(typeof(Order), one), JsonConversionContract.For(typeof(Order), one));
    }

    [Fact]
    public void Scanner_descends_through_arrays_and_dictionary_values_and_honors_extension_data()
    {
        var contract = JsonConversionContract.For(typeof(Container));
        var input = Element("{\"orders\":[{\"extra\":1}],\"byKey\":{\"ArbitraryKey\":{\"more\":2}},\"extension\":{\"kept\":3}}");
        var warnings = UnmatchedMemberScanner.Scan(input, typeof(Container), contract);
        Assert.Equal(new[] { "$[\"orders\"][0][\"extra\"]", "$[\"byKey\"][\"ArbitraryKey\"][\"more\"]" }, warnings.Select(x => x.Path));
        Assert.NotNull(contract.Read(input, typeof(Container)));
    }

    [Fact]
    public void Nested_type_contract_and_annotated_names_match_the_actual_converter()
    {
        var input = Element("{\"nested\":{\"CUSTOMER_ID\":\"x\"}}");
        var contract = JsonConversionContract.For(typeof(WithNested));
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(WithNested), contract));
        Assert.Equal("x", ((WithNested)contract.Read(input, typeof(WithNested))!).Nested!.CustomerId);
    }

    [Fact]
    public void Explicit_mappings_only_apply_to_the_selected_type()
    {
        var contract = JsonConversionContract.For(typeof(Mapped));
        var value = (Mapped)contract.Read(Element("{\"invoiceID\":1,\"child\":{\"id\":2}}"), typeof(Mapped))!;
        Assert.Equal(1, value.Id);
        Assert.Equal(2, value.Child!.Id);
        Assert.Equal("{\"invoiceID\":1,\"child\":{\"id\":2}}", contract.Write(value, typeof(Mapped)).GetRawText());
    }

    [Fact]
    public void Custom_converter_reports_unavailable_coverage_without_false_unknown_keys()
    {
        var contract = JsonConversionContract.For(typeof(CustomHolder));
        var input = Element("{\"value\":{\"alternate\":1}}");
        var warning = Assert.Single(UnmatchedMemberScanner.Scan(input, typeof(CustomHolder), contract));
        Assert.Equal("json-unmatched-coverage-unavailable", warning.Code);
        Assert.Contains(nameof(CustomOrderConverter), warning.Converter);
        Assert.NotNull(contract.Read(input, typeof(CustomHolder)));
    }

    [Fact]
    public void Strict_and_required_contracts_refuse()
    {
        var strict = JsonConversionContract.For(typeof(StrictOrder));
        Assert.Contains("json-unmatched-member-refused", Assert.ThrowsAny<JsonException>(() => strict.Read(Element("{\"wrong\":1}"), typeof(StrictOrder))).Message);
        Assert.ThrowsAny<JsonException>(() => JsonConversionContract.For(typeof(RequiredOrder)).Read(Element("{}"), typeof(RequiredOrder)));
    }

    [Theory]
    [InlineData("\"42\"", typeof(int))]
    [InlineData("5.0", typeof(int))]
    [InlineData("\"true\"", typeof(bool))]
    [InlineData("\"P1M\"", typeof(TimeSpan))]
    [InlineData("\"P1Y\"", typeof(TimeSpan))]
    [InlineData("\"P10675200D\"", typeof(TimeSpan))]
    [InlineData("42", typeof(DateTime))]
    [InlineData("false", typeof(DateTimeOffset))]
    [InlineData("{}", typeof(TimeSpan))]
    public void Implicit_coercions_and_invalid_temporal_values_refuse_as_json_errors(string json, Type type) =>
        Assert.ThrowsAny<JsonException>(() => JsonConversionContract.For(type).Read(Element(json), type));

    [Fact]
    public void Number_handling_annotations_cannot_reenable_text_to_number_parsing()
    {
        var contract = JsonConversionContract.For(typeof(AnnotatedNumber));
        Assert.ThrowsAny<JsonException>(() => contract.Read(Element("{\"number\":\"42\"}"), typeof(AnnotatedNumber)));
    }

    [Fact]
    public void Nullable_scoped_structs_remain_strict_and_nested_date_paths_remain_complete()
    {
        var contract = JsonConversionContract.For(typeof(StrictValue?));
        Assert.ThrowsAny<JsonException>(() => contract.Read(Element("{\"unknown\":1}"), typeof(StrictValue?)));
        var date = JsonConversionContract.For(typeof(NestedDateHolder));
        var exception = Assert.ThrowsAny<JsonException>(() => date.Read(Element("{\"invoice\":{\"startAt\":\"2026-10-28T09:15:00\"}}"), typeof(NestedDateHolder)));
        Assert.Equal("$[\"invoice\"][\"startAt\"]", exception.Path);
        Assert.Contains("json-datetime-offset-missing", exception.Message);
    }

    [Fact]
    public void Dates_require_offsets_keep_conversion_offsets_and_write_instants_in_utc()
    {
        var contract = JsonConversionContract.For(typeof(DateTimeOffset));
        Assert.Contains("json-datetime-offset-missing", Assert.ThrowsAny<JsonException>(() => contract.Read(Element("\"2026-10-28T09:15:00\""), typeof(DateTimeOffset))).Message);
        var offset = (DateTimeOffset)contract.Read(Element("\"2026-10-28T09:15:00+01:00\""), typeof(DateTimeOffset))!;
        Assert.Equal(TimeSpan.FromHours(1), offset.Offset);
        Assert.EndsWith("+01:00\"", contract.Write(offset, typeof(DateTimeOffset)).GetRawText());
        var date = (DateTime)JsonConversionContract.For(typeof(DateTime)).Read(Element("\"2026-10-28T09:15:00+01:00\""), typeof(DateTime))!;
        Assert.Equal(DateTimeKind.Utc, date.Kind);
        Assert.Equal(8, date.Hour);
        Assert.ThrowsAny<JsonException>(() => JsonConversionContract.For(typeof(DateTime)).Write(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), typeof(DateTime)));
        var options = JsonBaseline.CreateCopy();
        StrictDateTimeConverters.Configure(options, apiMode: true);
        Assert.Contains("08:15:00", JsonSerializer.Serialize(offset, options));
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), options));
    }

    [Fact]
    public void Date_only_and_time_only_keep_the_agreed_wall_clock_rules()
    {
        Assert.Equal(new DateOnly(2026, 10, 27), JsonConversionContract.For(typeof(DateOnly)).Read(Element("\"2026-10-28T00:15:00+02:00\""), typeof(DateOnly)));
        Assert.Equal(new DateOnly(2026, 10, 28), JsonConversionContract.For(typeof(DateOnly)).Read(Element("\"2026-10-28T00:15:00\""), typeof(DateOnly)));
        Assert.Equal(new TimeOnly(0, 15), JsonConversionContract.For(typeof(TimeOnly)).Read(Element("\"2026-10-28T00:15:00+02:00\""), typeof(TimeOnly)));
        Assert.Equal(TimeSpan.FromMinutes(90), JsonConversionContract.For(typeof(TimeSpan)).Read(Element("\"01:30:00\""), typeof(TimeSpan)));
        Assert.Equal("\"PT1H30M\"", JsonConversionContract.For(typeof(TimeSpan)).Write(TimeSpan.FromMinutes(90), typeof(TimeSpan)).GetRawText());
    }

    private static JsonElement Element(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private sealed class Order { public string? CustomerName { get; set; } public int Total { get; set; } }
    private sealed class Inputs { [JsonConversion(CaseInsensitive = true)] public Order? One { get; set; } public Order? Two { get; set; } }
    private sealed class Container { public Order[]? Orders { get; set; } public Dictionary<string, Order>? ByKey { get; set; } public Extension? Extension { get; set; } }
    private sealed class Extension { [JsonExtensionData] public Dictionary<string, JsonElement>? Data { get; set; } }
    private sealed class WithNested { public Nested? Nested { get; set; } }
    [JsonConversion(CaseInsensitive = true)] private sealed class Nested { [JsonPropertyName("customer_id")] public string? CustomerId { get; set; } }
    [JsonConversion(MemberMappings = ["invoiceID=Id"])] private sealed class Mapped { public int Id { get; set; } public Child? Child { get; set; } }
    private sealed class Child { public int Id { get; set; } }
    [JsonConversion(RefuseUnmatchedMembers = true)] private sealed class StrictOrder { public string? Name { get; set; } }
    private sealed class RequiredOrder { public required string Name { get; set; } }
    private sealed class AnnotatedNumber { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Number { get; set; } }
    private sealed class CustomHolder { [JsonConverter(typeof(CustomOrderConverter))] public Order? Value { get; set; } }
    [JsonConversion(RefuseUnmatchedMembers = true)] private struct StrictValue { public int Number { get; set; } }
    private sealed class NestedDateHolder { public ScopedDate? Invoice { get; set; } }
    [JsonConversion] private sealed class ScopedDate { public DateTime StartAt { get; set; } }
    private sealed class CustomOrderConverter : JsonConverter<Order>
    {
        public override Order Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { using var _ = JsonDocument.ParseValue(ref reader); return new(); }
        public override void Write(Utf8JsonWriter writer, Order value, JsonSerializerOptions options) { writer.WriteStartObject(); writer.WriteEndObject(); }
    }
}
