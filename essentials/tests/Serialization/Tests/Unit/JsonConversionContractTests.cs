using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonConversionContractTests
{
    [Fact]
    public void Scalar_reads_keep_exact_numeric_null_and_selection_refusal_rules()
    {
        Assert.Equal(9007199254740993L, JsonConversionContract.Deserialize<long>("9007199254740993"));
        Assert.Equal(ulong.MaxValue, JsonConversionContract.Deserialize<ulong>("18446744073709551615"));
        Assert.Equal(0.100000000000000000000001m, JsonConversionContract.Deserialize<decimal>("0.100000000000000000000001"));
        Assert.Equal('é', JsonConversionContract.Deserialize<char>("\"é\""));
        Assert.True(JsonConversionContract.Deserialize<bool>("true"));
        Assert.Equal(42, JsonConversionContract.Deserialize<int?>("42"));
        Assert.Null(JsonConversionContract.Deserialize<int?>("null"));
        Assert.Throws<JsonException>(() => JsonConversionContract.Deserialize<int>("null"));
        Assert.Throws<JsonException>(() => JsonConversionContract.Deserialize<int>("2147483648"));
        Assert.Throws<JsonException>(() => JsonConversionContract.Deserialize<char>("\"xy\""));
        var property = typeof(InvalidScalarSelection).GetProperty(nameof(InvalidScalarSelection.Value))!;
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonConversionContract.For(typeof(int), property).Read(Element("1"), typeof(int)));
    }

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
    public void Scanner_skips_ignored_and_unbound_read_only_values_without_false_warnings()
    {
        const string raw = """{"ignoredAt":"2026-10-28T09:15:00","ignoredNested":{"unknown":1},"readOnlyAt":"2026-10-28T09:15:00","readOnlyNested":{"startAt":"2026-10-28T09:15:00","unknown":1},"accepted":3}""";
        var input = Element(raw);
        var contract = JsonConversionContract.For(typeof(IgnoredReadMembers));
        var warnings = new List<JsonConversionDiagnostic>();
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(IgnoredReadMembers), contract));
        var value = (IgnoredReadMembers)contract.Read(input, typeof(IgnoredReadMembers), warnings.Add)!;
        Assert.Empty(warnings);
        Assert.Equal(3, value.Accepted);
        Assert.Equal(default, value.IgnoredAt);
        Assert.Null(value.IgnoredNested);
        Assert.Equal(default, value.ReadOnlyAt);
        Assert.Equal(default, value.ReadOnlyNested.StartAt);
        Assert.Equal(raw, input.GetRawText());
        var unmatched = Assert.Throws<JsonConversionException>(() => contract.Read(Element("""{"unknown":1}"""), typeof(IgnoredReadMembers)));
        Assert.Equal("json-unmatched-member-refused", unmatched.Code);
        Assert.Equal("$[\"unknown\"]", unmatched.Path);
    }

    [Fact]
    public void Constructor_associated_getters_still_validate_dates_and_unmatched_members()
    {
        var contract = JsonConversionContract.For(typeof(ConstructorReadMembers));
        var valid = Element("""{"startAt":"2026-10-28T09:15:00+01:00","order":{"customerName":"Ada"}}""");
        var warnings = new List<JsonConversionDiagnostic>();
        Assert.Empty(UnmatchedMemberScanner.Scan(valid, typeof(ConstructorReadMembers), contract));
        var value = (ConstructorReadMembers)contract.Read(valid, typeof(ConstructorReadMembers), warnings.Add)!;
        Assert.Equal(new DateTime(2026, 10, 28, 8, 15, 0, DateTimeKind.Utc), value.StartAt);
        Assert.Equal("Ada", value.Order.CustomerName);
        Assert.Empty(warnings);
        var dateError = Assert.Throws<JsonConversionException>(() => UnmatchedMemberScanner.Scan(
            Element("""{"startAt":"2026-10-28T09:15:00"}"""), typeof(ConstructorReadMembers), contract));
        Assert.Equal("json-datetime-offset-missing", dateError.Code);
        Assert.Equal("$[\"startAt\"]", dateError.Path);
        var unmatched = Assert.Throws<JsonConversionException>(() => UnmatchedMemberScanner.Scan(
            Element("""{"order":{"unknown":1}}"""), typeof(ConstructorReadMembers), contract));
        Assert.Equal("json-unmatched-member-refused", unmatched.Code);
        Assert.Equal("$[\"order\"][\"unknown\"]", unmatched.Path);
    }

    [Theory]
    [InlineData(typeof(MemberPopulateReadMembers))]
    [InlineData(typeof(TypePopulateReadMembers))]
    public void Populate_getters_still_scan_and_populate_their_read_values(Type type)
    {
        var contract = JsonConversionContract.For(type);
        var valid = Element("""{"orders":[{"customerName":"Ada"}]}""");
        var warnings = new List<JsonConversionDiagnostic>();
        Assert.Empty(UnmatchedMemberScanner.Scan(valid, type, contract));
        var value = contract.Read(valid, type, warnings.Add)!;
        var orders = Assert.IsType<List<Order>>(type.GetProperty("Orders")!.GetValue(value));
        Assert.Equal("Ada", Assert.Single(orders).CustomerName);
        Assert.Empty(warnings);
        var unmatched = Assert.Throws<JsonConversionException>(() => UnmatchedMemberScanner.Scan(
            Element("""{"orders":[{"unknown":1}]}"""), type, contract));
        Assert.Equal("json-unmatched-member-refused", unmatched.Code);
        Assert.Equal("$[\"orders\"][0][\"unknown\"]", unmatched.Path);
    }

    [Fact]
    public void Explicit_replace_overrides_type_populate_for_an_unbound_getter()
    {
        var input = Element("""{"orders":[{"unknown":1}]}""");
        var contract = JsonConversionContract.For(typeof(ReplaceReadMembers));
        var warnings = new List<JsonConversionDiagnostic>();
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(ReplaceReadMembers), contract));
        var value = (ReplaceReadMembers)contract.Read(input, typeof(ReplaceReadMembers), warnings.Add)!;
        Assert.Empty(value.Orders);
        Assert.Empty(warnings);
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

    [Theory]
    [InlineData("{\"value\":{\"child\":{\"customer_name\":\"Ann\"},\"unexpected\":1}}", "$[\"value\"][\"unexpected\"]")]
    [InlineData("{\"value\":{\"child\":{\"customer_name\":\"Ann\",\"unexpected\":1}}}", "$[\"value\"][\"child\"][\"unexpected\"]")]
    public void Property_factory_matcher_enforces_parent_and_child_strict_contracts(string json, string expectedPath)
    {
        var type = typeof(PropertyMatchedFactoryHolder<StrictMatchedChild>);
        var contract = JsonConversionContract.For(type);
        var input = Element(json);
        var error = Assert.Throws<JsonConversionException>(() => contract.Read(input, type));
        Assert.Equal("json-unmatched-member-refused", error.Code);
        Assert.Equal(expectedPath, error.Path);
        Assert.Equal(json, input.GetRawText());
    }

    [Fact]
    public void Property_factory_matcher_honors_child_scoped_naming_and_case()
    {
        const string raw = """{ "Name":"Ren\u00e9", "N":1.0, "nil":null }""";
        var input = Element($$"""{ "value": { "child": { "CUSTOMER_NAME":"Ada", "RAW_DATA":{{raw}} } } }""");
        var type = typeof(PropertyMatchedFactoryHolder<InsensitiveMatchedChild>);
        var contract = JsonConversionContract.For(type);
        var warnings = new List<JsonConversionDiagnostic>();
        var value = (PropertyMatchedFactoryHolder<InsensitiveMatchedChild>)contract.Read(input, type, warnings.Add)!;
        Assert.Empty(warnings);
        Assert.Equal("Ada", value.Value!.Child.CustomerName);
        Assert.Equal(raw, value.Value.Child.RawData.GetRawText());
        var written = contract.Write(value, type).GetProperty("value").GetProperty("child");
        Assert.Equal("Ada", written.GetProperty("customer_name").GetString());
        Assert.Equal(raw, written.GetProperty("raw_data").GetRawText());
    }

    [Fact]
    public void Property_factory_without_a_matcher_retains_unavailable_coverage()
    {
        var type = typeof(OpaqueFactoryHolder);
        var contract = JsonConversionContract.For(type);
        var warnings = new List<JsonConversionDiagnostic>();
        Assert.NotNull(contract.Read(Element("""{"value":{"alternate":1}}"""), type, warnings.Add));
        var warning = Assert.Single(warnings);
        Assert.Equal("json-unmatched-coverage-unavailable", warning.Code);
        Assert.Contains(nameof(CustomOrderConverter), warning.Converter);
        Assert.Equal("$[\"value\"]", warning.Path);
    }

    [Fact]
    public void Nullable_property_factory_matcher_uses_the_declared_type_and_active_options()
    {
        var type = typeof(NullableFactoryHolder);
        var contract = JsonConversionContract.For(type);
        var warnings = new List<JsonConversionDiagnostic>();
        var value = (NullableFactoryHolder)contract.Read(Element("""{"value":{"WIRE_NUMBER":7}}"""), type, warnings.Add)!;
        Assert.Equal(7, value.Value!.Value.Number);
        Assert.Empty(warnings);
        value = (NullableFactoryHolder)contract.Read(Element("""{"value":null}"""), type, warnings.Add)!;
        Assert.Null(value.Value);
        Assert.Empty(warnings);
        var error = Assert.Throws<JsonConversionException>(() => contract.Read(Element("""{"value":{"WIRE_NUMBER":7,"unknown":1}}"""), type));
        Assert.Equal("json-unmatched-member-refused", error.Code);
        Assert.Equal("$[\"value\"][\"unknown\"]", error.Path);
    }

    [Theory]
    [InlineData("{\"child\":{\"customer_name\":\"Ann\",\"unexpected\":1}}", "$[\"child\"][\"unexpected\"]")]
    [InlineData("{\"child\":{\"Customer_Name\":\"Ann\"}}", "$[\"child\"][\"Customer_Name\"]")]
    public void Custom_matcher_refuses_unmatched_members_under_the_child_scoped_contract(string json, string expectedPath)
    {
        var contract = JsonConversionContract.For(typeof(MatchedChildEnvelope<StrictMatchedChild>));
        var input = Element(json);
        var error = Assert.Throws<JsonConversionException>(() => contract.Read(input, typeof(MatchedChildEnvelope<StrictMatchedChild>)));
        Assert.Equal("json-unmatched-member-refused", error.Code);
        Assert.Equal(expectedPath, error.Path);
        Assert.Equal(json, input.GetRawText());
    }

    [Fact]
    public void Custom_matcher_uses_child_scoped_naming_and_case_without_rewriting_json()
    {
        const string raw = """{ "Name":"Ren\u00e9", "N":1.0, "nil":null }""";
        var strictInput = Element($$"""{ "child": { "customer_name":"Ann", "raw_data":{{raw}} } }""");
        var strictBefore = strictInput.GetRawText();
        var strictWarnings = new List<JsonConversionDiagnostic>();
        var strictContract = JsonConversionContract.For(typeof(MatchedChildEnvelope<StrictMatchedChild>));
        var strict = (MatchedChildEnvelope<StrictMatchedChild>)strictContract.Read(strictInput, typeof(MatchedChildEnvelope<StrictMatchedChild>), strictWarnings.Add)!;
        Assert.Equal("Ann", strict.Child.CustomerName);
        Assert.Empty(strictWarnings);
        Assert.Equal(raw, strict.Child.RawData.GetRawText());
        Assert.Equal(strictBefore, strictInput.GetRawText());
        var strictWritten = strictContract.Write(strict, typeof(MatchedChildEnvelope<StrictMatchedChild>)).GetProperty("child");
        Assert.Equal("Ann", strictWritten.GetProperty("customer_name").GetString());
        Assert.Equal(raw, strictWritten.GetProperty("raw_data").GetRawText());

        var insensitiveInput = Element($$"""{ "child": { "CUSTOMER_NAME":"Ada", "RAW_DATA":{{raw}} } }""");
        var insensitiveBefore = insensitiveInput.GetRawText();
        var insensitiveWarnings = new List<JsonConversionDiagnostic>();
        var insensitiveContract = JsonConversionContract.For(typeof(MatchedChildEnvelope<InsensitiveMatchedChild>));
        var insensitive = (MatchedChildEnvelope<InsensitiveMatchedChild>)insensitiveContract.Read(insensitiveInput, typeof(MatchedChildEnvelope<InsensitiveMatchedChild>), insensitiveWarnings.Add)!;
        Assert.Equal("Ada", insensitive.Child.CustomerName);
        Assert.Empty(insensitiveWarnings);
        Assert.Equal(raw, insensitive.Child.RawData.GetRawText());
        Assert.Equal(insensitiveBefore, insensitiveInput.GetRawText());
        var insensitiveWritten = insensitiveContract.Write(insensitive, typeof(MatchedChildEnvelope<InsensitiveMatchedChild>)).GetProperty("child");
        Assert.Equal("Ada", insensitiveWritten.GetProperty("customer_name").GetString());
        Assert.Equal(raw, insensitiveWritten.GetProperty("raw_data").GetRawText());
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
    [InlineData("\"NaN\"", typeof(double))]
    [InlineData("\"Infinity\"", typeof(float))]
    [InlineData("42", typeof(string))]
    [InlineData("false", typeof(string))]
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
    public void API_refusal_messages_retain_owned_paths_while_runtime_owners_choose_path_protection()
    {
        const string json = "{\"privateDate\":\"2026-01-15T09:15:00\"}";
        var runtime = Assert.Throws<JsonConversionException>(() => JsonConversionContract.For(typeof(DateInput)).Read(Element(json), typeof(DateInput)));
        Assert.Equal("$[\"privateDate\"]", runtime.Path);
        Assert.DoesNotContain("privateDate", runtime.Message);
        var options = JsonBaseline.CreateCopy(); StrictDateTimeConverters.Configure(options, apiMode: true);
        var api = Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<DateInput>(json, options));
        Assert.Contains("json-datetime-offset-missing", api.Message);
        Assert.Contains("$.privateDate", api.Message);
    }

    private sealed record DateInput(DateTimeOffset PrivateDate);

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

    [Theory]
    [InlineData("garbageT09:15+garbage")]
    [InlineData("2026-02-30T09:15+99:99")]
    [InlineData("2026-02-30T09:15:00Z")]
    [InlineData("2026-10-28T09:15:00+02:00junk")]
    public void Time_only_refuses_malformed_date_and_offset_wrappers(string text) =>
        Assert.ThrowsAny<JsonException>(() => JsonConversionContract.Deserialize<TimeOnly>(JsonSerializer.Serialize(text, JsonBaseline.Options)));
    private sealed class Order { public string? CustomerName { get; set; } public int Total { get; set; } }
    private sealed class Inputs { [JsonConversion(CaseInsensitive = true)] public Order? One { get; set; } public Order? Two { get; set; } }
    private sealed class Container { public Order[]? Orders { get; set; } public Dictionary<string, Order>? ByKey { get; set; } public Extension? Extension { get; set; } }
    private sealed class Extension { [JsonExtensionData] public Dictionary<string, JsonElement>? Data { get; set; } }
    private sealed class WithNested { public Nested? Nested { get; set; } }
    [JsonConversion(CaseInsensitive = true)] private sealed class Nested { [JsonPropertyName("customer_id")] public string? CustomerId { get; set; } }
    [JsonConversion(MemberMappings = ["invoiceID=Id"])] private sealed class Mapped { public int Id { get; set; } public Child? Child { get; set; } }
    private sealed class Child { public int Id { get; set; } }
    private sealed class InvalidScalarSelection { [JsonConversion(NamingPolicy = (JsonKnownNamingPolicy)999)] public int Value { get; set; } }
    [JsonConversion(RefuseUnmatchedMembers = true)] private sealed class StrictOrder { public string? Name { get; set; } }
    private sealed class RequiredOrder { public required string Name { get; set; } }
    private sealed class AnnotatedNumber { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Number { get; set; } }
    private sealed class CustomHolder { [JsonConverter(typeof(CustomOrderConverter))] public Order? Value { get; set; } }
    [JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed class IgnoredReadMembers
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public DateTime IgnoredAt { get; set; }
        [JsonIgnore] public ScopedDate? IgnoredNested { get; set; }
        public DateTime ReadOnlyAt { get; }
        public ScopedDate ReadOnlyNested { get; } = new();
        public int Accepted { get; set; }
    }
    [JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed class ConstructorReadMembers(DateTime startAt, Order order)
    {
        public DateTime StartAt { get; } = startAt;
        public Order Order { get; } = order;
    }
    [JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed class MemberPopulateReadMembers
    {
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)] public List<Order> Orders { get; } = [];
    }
    [JsonConversion(RefuseUnmatchedMembers = true), JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    private sealed class TypePopulateReadMembers { public List<Order> Orders { get; } = []; }
    [JsonConversion(RefuseUnmatchedMembers = true), JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    private sealed class ReplaceReadMembers
    {
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)] public List<Order> Orders { get; } = [];
    }
    [JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed class PropertyMatchedFactoryHolder<T>
    {
        [JsonConverter(typeof(MatchedChildEnvelopeConverterFactory))] public MatchedChildEnvelope<T>? Value { get; set; }
    }
    [JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed class OpaqueFactoryHolder { [JsonConverter(typeof(OpaqueOrderConverterFactory))] public Order? Value { get; set; } }
    private sealed class OpaqueOrderConverterFactory : JsonConverterFactory
    {
        public OpaqueOrderConverterFactory() { }
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Order);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new CustomOrderConverter();
    }
    private readonly record struct NullableMatchedValue(int Number);
    [JsonConversion(CaseInsensitive = true, RefuseUnmatchedMembers = true)]
    private sealed class NullableFactoryHolder
    {
        [JsonConverter(typeof(NullableMatchedValueConverterFactory))] public NullableMatchedValue? Value { get; set; }
    }
    private sealed class NullableMatchedValueConverterFactory : JsonConverterFactory
    {
        public NullableMatchedValueConverterFactory() { }
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(NullableMatchedValue?);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            typeToConvert == typeof(NullableMatchedValue?) ? new NullableMatchedValueConverter(options.PropertyNameCaseInsensitive)
                : throw new InvalidOperationException("The member factory needs its nullable declared type.");
    }
    private sealed class NullableMatchedValueConverter(bool caseInsensitive) : JsonConverter<NullableMatchedValue?>, IJsonConversionMemberMatcher
    {
        public bool TryGetMemberType(string name, out Type memberType)
        {
            memberType = typeof(int);
            return name.Equals("wire_number", caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        public override NullableMatchedValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            using var document = JsonDocument.ParseValue(ref reader);
            return new NullableMatchedValue(document.RootElement.EnumerateObject().First(property => TryGetMemberType(property.Name, out _)).Value.GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, NullableMatchedValue? value, JsonSerializerOptions options)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            writer.WriteStartObject(); writer.WriteNumber("wire_number", value.Value.Number); writer.WriteEndObject();
        }
    }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RefuseUnmatchedMembers = true)]
    private sealed class StrictMatchedChild { public string? CustomerName { get; set; } public JsonElement RawData { get; set; } }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, CaseInsensitive = true)]
    private sealed class InsensitiveMatchedChild { public string? CustomerName { get; set; } public JsonElement RawData { get; set; } }
    [JsonConverter(typeof(MatchedChildEnvelopeConverterFactory))]
    private sealed record MatchedChildEnvelope<T>(T Child);

    private sealed class MatchedChildEnvelopeConverterFactory : JsonConverterFactory
    {
        public MatchedChildEnvelopeConverterFactory() { }
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(MatchedChildEnvelope<>);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(MatchedChildEnvelopeConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    private sealed class MatchedChildEnvelopeConverter<T> : JsonConverter<MatchedChildEnvelope<T>>, IJsonConversionMemberMatcher
    {
        public MatchedChildEnvelopeConverter() { }
        public bool TryGetMemberType(string name, out Type memberType)
        {
            memberType = typeof(T);
            return name == "child";
        }
        public override MatchedChildEnvelope<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return new(document.RootElement.GetProperty("child").Deserialize<T>(options)!);
        }
        public override void Write(Utf8JsonWriter writer, MatchedChildEnvelope<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("child");
            JsonSerializer.Serialize(writer, value.Child, options);
            writer.WriteEndObject();
        }
    }
    [JsonConversion(RefuseUnmatchedMembers = true)] private struct StrictValue { public int Number { get; set; } }
    private sealed class NestedDateHolder { public ScopedDate? Invoice { get; set; } }
    [JsonConversion] private sealed class ScopedDate { public DateTime StartAt { get; set; } }
    private sealed class CustomOrderConverter : JsonConverter<Order>
    {
        public override Order Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { using var _ = JsonDocument.ParseValue(ref reader); return new(); }
        public override void Write(Utf8JsonWriter writer, Order value, JsonSerializerOptions options) { writer.WriteStartObject(); writer.WriteEndObject(); }
    }
}
