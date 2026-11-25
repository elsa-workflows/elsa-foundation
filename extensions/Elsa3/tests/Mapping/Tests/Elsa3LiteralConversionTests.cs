using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3LiteralConversionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Discriminator_rewrites_keep_the_declared_scope_including_Default_and_refuse_ambiguity(bool useDefault, bool ambiguous)
    {
        const string carried = "{ \"Name\":\"Ren\\u00e9\", \"n\":1.0, \"x\":null }";
        var input = Parse("{\"kind\":\"scoped\",\"CUSTOMER_NAME\":\"Ada\",\"START_AT\":\"2026-07-15T09:15:00\",\"carried\":" + carried +
            (ambiguous ? ",\"customer_name\":\"Second\"}" : "}"));
        var raw = input.GetRawText();
        var contract = useDefault ? JsonConversionContract.Default : JsonConversionContract.For(typeof(ScopedImportBase));
        var notes = new List<Elsa3LiteralConversion.Note>();
        JsonElement Convert() => Elsa3LiteralConversion.Rewrite(input, typeof(ScopedImportBase), Options(Elsa3DateInterpretation.SourceTimeZone), notes.Add, contract);
        if (ambiguous) Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Convert()).Code);
        else
        {
            var converted = Convert();
            Assert.Equal("scoped", converted.GetProperty("kind").GetString());
            Assert.Equal("Ada", converted.GetProperty("customer_name").GetString());
            Assert.Equal("2026-07-15T09:15:00+02:00", converted.GetProperty("start_at").GetString());
            Assert.Equal(carried, converted.GetProperty("carried").GetRawText());
            Assert.Empty(UnmatchedMemberScanner.Scan(converted, typeof(ScopedImportBase), contract));
            Assert.Equal("Ada", Assert.IsType<ScopedImportDerived>(contract.Read(converted, typeof(ScopedImportBase))).CustomerName);
            Assert.DoesNotContain(notes, note => note.Code == "json-unmatched-coverage-unavailable");

            var direct = Elsa3LiteralConversion.Rewrite(Parse("""{"CUSTOMERNAME":"Direct","STARTAT":"2026-07-15T09:15:00"}"""),
                typeof(ScopedImportDerived), Options(Elsa3DateInterpretation.SourceTimeZone), _ => { });
            Assert.Equal("Direct", direct.GetProperty("CustomerName").GetString());
            Assert.Equal("2026-07-15T09:15:00+02:00", direct.GetProperty("StartAt").GetString());
        }
        Assert.Equal(raw, input.GetRawText());
    }

    [Theory]
    [InlineData("order_value", false)]
    [InlineData("order_values", false)]
    [InlineData("orders_by_key", false)]
    [InlineData("order_value", true)]
    [InlineData("order_values", true)]
    [InlineData("orders_by_key", true)]
    public void Unannotated_polymorphic_children_rewrite_keys_and_dates_under_the_enclosing_scope(string shape, bool useDefault)
    {
        const string carried = "{ \"Name\":\"Ren\\u00e9\", \"n\":1.0 }";
        var child = "{\"CUSTOMER_NAME\":\"Ada\",\"START_AT\":\"2026-07-15T09:15:00\",\"carried\":" + carried + "}";
        var contents = shape switch { "order_value" => child, "order_values" => "[" + child + "]", _ => "{\"KeepKey\":" + child + "}" };
        var input = Parse("{\"" + shape + "\":" + contents + "}");
        var raw = input.GetRawText();
        var contract = useDefault ? JsonConversionContract.Default : JsonConversionContract.For(typeof(ImportPolymorphicChildren));
        var notes = new List<Elsa3LiteralConversion.Note>();
        var converted = Elsa3LiteralConversion.Rewrite(input, typeof(ImportPolymorphicChildren), Options(Elsa3DateInterpretation.SourceTimeZone), notes.Add, contract);
        var nested = shape switch { "order_value" => converted.GetProperty(shape), "order_values" => converted.GetProperty(shape)[0], _ => converted.GetProperty(shape).GetProperty("KeepKey") };
        Assert.Equal("Ada", nested.GetProperty("customer_name").GetString());
        Assert.Equal("2026-07-15T09:15:00+02:00", nested.GetProperty("start_at").GetString());
        Assert.Equal(carried, nested.GetProperty("carried").GetRawText());
        Assert.DoesNotContain(notes, note => note.Code == "json-unmatched-coverage-unavailable");
        Assert.Empty(UnmatchedMemberScanner.Scan(converted, typeof(ImportPolymorphicChildren), contract));
        var typed = (ImportPolymorphicChildren)contract.Read(converted, typeof(ImportPolymorphicChildren))!;
        var value = shape switch { "order_value" => typed.OrderValue!, "order_values" => typed.OrderValues![0], _ => typed.OrdersByKey!["KeepKey"] };
        Assert.Equal("Ada", value.CustomerName);
        var refused = Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Elsa3LiteralConversion.Rewrite(input, typeof(ImportPolymorphicChildren), new(), _ => { }, contract));
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing, refused.Code);
        Assert.Equal(raw, input.GetRawText());
    }

    [Theory]
    [InlineData("Child", false)]
    [InlineData("Children", false)]
    [InlineData("ListChildren", false)]
    [InlineData("Named", false)]
    [InlineData("Child", true)]
    [InlineData("Children", true)]
    [InlineData("ListChildren", true)]
    [InlineData("Named", true)]
    public void Recursive_contract_adapters_rewrite_nested_keys_and_dates_and_still_refuse_ambiguity(string shape, bool ambiguous)
    {
        const string carried = "{ \"Name\":\"Ren\\u00e9\", \"n\":1.0 }";
        var child = "{\"CUSTOMER_NAME\":\"Nested\",\"AT\":\"2026-07-15T09:15:00\",\"CARRIED\":" + carried +
            (ambiguous ? ",\"customer_name\":\"Second\"}" : "}");
        var children = shape switch { "Child" => child, "Children" or "ListChildren" => "[" + child + "]", _ => "{\"KeepKey\":" + child + "}" };
        var input = Parse("{\"CustomerName\":\"Root\",\"" + shape + "\":" + children + "}");
        var original = input.GetRawText();
        var member = typeof(RecursiveInputs).GetProperty(nameof(RecursiveInputs.Value))!;
        var contract = JsonConversionContract.For(typeof(RecursiveOrder), member);
        var notes = new List<Elsa3LiteralConversion.Note>();
        JsonElement Convert() => Elsa3LiteralConversion.Rewrite(input, typeof(RecursiveOrder), Options(Elsa3DateInterpretation.SourceTimeZone), notes.Add, contract);
        if (ambiguous)
            Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Convert()).Code);
        else
        {
            var converted = Convert();
            var nested = shape switch { "Child" => converted.GetProperty(shape), "Children" or "ListChildren" => converted.GetProperty(shape)[0], _ => converted.GetProperty(shape).GetProperty("KeepKey") };
            Assert.Equal("Nested", nested.GetProperty("customer_name").GetString());
            Assert.Equal("2026-07-15T09:15:00+02:00", nested.GetProperty("at").GetString());
            Assert.Equal(carried, nested.GetProperty("carried").GetRawText());
            Assert.DoesNotContain(notes, note => note.Code == "json-unmatched-coverage-unavailable");
            var typed = (RecursiveOrder)contract.Read(converted, typeof(RecursiveOrder))!;
            Assert.Equal("Root", typed.CustomerName);
            Assert.Equal("Nested", (shape switch { "Child" => typed.Child, "Children" => typed.Children![0], "ListChildren" => typed.ListChildren![0], _ => typed.Named!["KeepKey"] })!.CustomerName);
        }
        Assert.Equal(original, input.GetRawText());
    }

    [Theory]
    [InlineData("https://example.test/items?OwnerKey=%C3%A9")]
    [InlineData("items/next")]
    public async Task A_converter_backed_URI_literal_keeps_its_string_token(string text)
    {
        var typed = Elsa3TypedValues.CreateExpression("System.Uri", "Literal", text);
        var definition = ConversionServices.Definition(Activity("root", "Acme.JsonOrder", ("Url", typed)));
        await using var provider = ConversionServices.Create(RealActivityCatalog.CreateWith(typeof(JsonOrderImportActivity).Assembly));
        var registry = provider.GetRequiredService<IWellKnownTypeRegistry>();
        if (!registry.TryGetTypeOrDefault("System.Uri", out var uriType))
            registry.RegisterType(typeof(Uri), "System.Uri");
        else Assert.Equal(typeof(Uri), uriType);
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>().ConvertAsync(new(definition));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var data = Assert.IsType<JsonElement>(Input(Node(result.Value!.State, "root"), "Url").Value.Value);
        Assert.Equal(JsonValueKind.String, data.ValueKind);
        Assert.Equal(text, data.GetString());
    }

    [Theory]
    [InlineData("String", "{\"name\":\"Ada\"}", "\"{\\\"name\\\":\\\"Ada\\\"}\"")]
    [InlineData("String", "[]", "\"[]\"")]
    [InlineData("Int32?", "5", "5")]
    [InlineData("Boolean?", "True", "true")]
    public void Legacy_scalar_defaults_keep_their_declared_kind(string alias, string value, string expected)
    {
        var source = new Elsa3Variable { Id = "value", Name = "value", TypeName = alias, Value = value };
        Assert.True(Elsa3VariableMapper.TryMap(ConversionServices.TypeRegistry(), SerializationServices.ObjectConverter(), source, out var mapped, out var refusal), refusal?.Message);
        Assert.Equal(expected, Assert.IsType<JsonElement>(mapped.Default!.Value).GetRawText());
        source.Value = null;
        Assert.True(Elsa3VariableMapper.TryMap(ConversionServices.TypeRegistry(), SerializationServices.ObjectConverter(), source, out mapped, out refusal));
        Assert.Null(mapped.Default);
    }

    [Fact]
    public void Nullable_struct_members_and_collection_elements_use_their_scoped_names()
    {
        var value = Parse("""{"one":{"ORDER_ID":"one"},"items":[{"ORDER_ID":"two"}],"byKey":{"OrderTotal":{"ORDER_ID":"three"}}}""");
        var converted = Elsa3LiteralConversion.Rewrite(value, typeof(NullableScopes), new(), _ => { });
        Assert.Equal("one", converted.GetProperty("one").GetProperty("order_id").GetString());
        Assert.Equal("two", converted.GetProperty("items")[0].GetProperty("order_id").GetString());
        Assert.Equal("three", converted.GetProperty("byKey").GetProperty("OrderTotal").GetProperty("order_id").GetString());
    }

    [Fact]
    public void A_property_converters_wire_shape_is_preserved_with_a_coverage_diagnostic()
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var value = Parse("""{"order":{ "NAME":"Ada" }}""");
        var converted = Elsa3LiteralConversion.Rewrite(value, typeof(CustomHolder), new(), notes.Add);
        Assert.Equal(value.GetRawText(), converted.GetRawText());
        Assert.Equal("json-unmatched-coverage-unavailable", Assert.Single(notes).Code);
    }

    [Fact]
    public void A_polymorphic_derived_contract_converts_its_keys_and_dates()
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var converted = Elsa3LiteralConversion.Rewrite(Parse("""{"kind":"dated","NAME":"Ada","AT":"2026-07-15T09:15:00"}"""),
            typeof(PolymorphicOrder), Options(Elsa3DateInterpretation.SourceTimeZone), notes.Add);
        Assert.Equal("Ada", converted.GetProperty("name").GetString());
        Assert.Equal("2026-07-15T09:15:00+02:00", converted.GetProperty("at").GetString());
        Assert.Contains(notes, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_actual_activity_member_scope_drives_import_and_raw_unknown_data_survives(bool ambiguous)
    {
        const string extra = "{ \"escaped\":\"Ren\\u00e9\", \"x\":null, \"n\":1.0 }";
        var raw = "{\"ORDER_ID\":\"one\",\"extra\":" + extra + (ambiguous ? ",\"order_id\":\"two\"}" : "}");
        var typed = Parse("{\"typeName\":\"" + typeof(JsonImportOrder).FullName + "\",\"expression\":{\"type\":\"Object\",\"value\":" + raw + "}}");
        var definition = ConversionServices.Definition(Activity("root", "Acme.JsonOrder", ("order", typed)));
        await using var provider = ConversionServices.Create(RealActivityCatalog.CreateWith(typeof(JsonOrderImportActivity).Assembly));
        var registry = provider.GetRequiredService<IWellKnownTypeRegistry>();
        foreach (var type in new[] { typeof(JsonImportOrder), typeof(JsonOrderImportActivity) }) registry.RegisterType(type, TypeAliasConvention.CanonicalAlias(type));
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>().ConvertAsync(new(definition));
        Assert.Equal(!ambiguous, result.Succeeded);
        if (ambiguous) Assert.Contains(result.Diagnostics, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous);
        else
        {
            var data = Assert.IsType<JsonElement>(Input(Node(result.Value!.State, "root"), "order").Value.Value);
            Assert.Equal("one", data.GetProperty("order_id").GetString());
            Assert.Equal(extra, data.GetProperty("extra").GetRawText());
            Assert.Contains(result.Diagnostics, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten);
        }
        Assert.Equal(raw, definition.Root.AdditionalProperties["order"].GetProperty("expression").GetProperty("value").GetRawText());
    }

    [Fact]
    public void Unknown_members_of_typed_variable_defaults_are_retained_and_reported()
    {
        var registry = ConversionServices.TypeRegistry(); registry.RegisterType(typeof(JsonImportOrder), TypeAliasConvention.CanonicalAlias(typeof(JsonImportOrder)));
        var notes = new List<Elsa3LiteralConversion.Note>();
        var source = new Elsa3Variable { Id = "order", Name = "order", TypeName = typeof(JsonImportOrder).FullName!, Value = "{\"id\":\"one\",\"extra\":null}" };
        Assert.True(Elsa3VariableMapper.TryMap(registry, SerializationServices.ObjectConverter(), source, out var mapped, out var refusal, report: notes.Add), refusal?.Message);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<JsonElement>(mapped.Default!.Value).GetProperty("extra").ValueKind);
        Assert.Contains(notes, note => note.Code == "json-unmatched-member" && note.Path == "$[\"extra\"]");
    }

    [Theory]
    [InlineData("At", "DateTime?")]
    [InlineData("Offset", "DateTimeOffset?")]
    [InlineData("Day", "DateOnly?")]
    public async Task Nullable_date_expressions_report_the_possible_wall_clock_change(string key, string alias)
    {
        var typed = Elsa3TypedValues.CreateExpression(alias, "JavaScript", "'2026-07-15T09:15:00Z'");
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.CreateWith(typeof(JsonOrderImportActivity).Assembly),
            ConversionServices.Definition(Activity("root", "Acme.JsonOrder", (key, typed))));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralDateWallClock && note.Path!.Contains(key, StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Case_only_keys_are_rewritten_recursively_without_changing_dictionary_keys_or_carried_data()
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var value = Parse("""{ "NAME":"Ada", "items":[{"CODE":"x"}], "byKey":{"OrderTotal":{"CODE":"y"}}, "extra":{ "n":1.0 } }""");
        var converted = Elsa3LiteralConversion.Rewrite(value, typeof(Order), new(), notes.Add);
        Assert.Equal("Ada", converted.GetProperty("name").GetString());
        Assert.Equal("x", converted.GetProperty("items")[0].GetProperty("code").GetString());
        Assert.Equal("y", converted.GetProperty("byKey").GetProperty("OrderTotal").GetProperty("code").GetString());
        Assert.Equal(value.GetProperty("extra").GetRawText(), converted.GetProperty("extra").GetRawText());
        Assert.Equal(3, notes.Count(note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten));
        Assert.Equal("NAME", value.EnumerateObject().First().Name);
    }

    [Theory]
    [InlineData("{\"name\":\"first\",\"NAME\":\"second\"}")]
    [InlineData("{\"NAME\":\"first\",\"name\":\"second\"}")]
    public void A_rewrite_that_would_overwrite_an_exact_member_is_refused(string json)
    {
        var refusal = Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Elsa3LiteralConversion.Rewrite(Parse(json), typeof(Order), new(), _ => { }));
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous, refusal.Code);
        Assert.Contains("exact JSON", refusal.Guidance);
    }

    [Fact]
    public void Scoped_names_and_explicit_json_names_define_the_rewrite_target()
    {
        var converted = Elsa3LiteralConversion.Rewrite(Parse("""{"ORDER_ID":"id","DISPLAY":"name"}"""), typeof(ScopedOrder), new(), _ => { });
        Assert.Equal("id", converted.GetProperty("order_id").GetString());
        Assert.Equal("name", converted.GetProperty("display").GetString());
    }

    [Theory]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.SourceTimeZone, "2026-01-15T09:15:00+01:00")]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.SourceTimeZone, "2026-07-15T09:15:00+02:00")]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.AssumeUtc, "2026-01-15T09:15:00Z")]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.AssumeUtc, "2026-07-15T09:15:00Z")]
    public void The_one_import_setting_establishes_the_date_specific_instant(string text, Elsa3DateInterpretation interpretation, string expected)
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var options = Options(interpretation);
        var converted = Elsa3LiteralConversion.Rewrite(JsonSerializer.SerializeToElement(text, JsonBaseline.Options), typeof(DateTimeOffset), options, notes.Add);
        Assert.Equal(expected, converted.GetString());
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted, Assert.Single(notes).Code);
        Assert.Equal(interpretation == Elsa3DateInterpretation.SourceTimeZone ? Elsa3MigrationDiagnosticSeverity.Info : Elsa3MigrationDiagnosticSeverity.Warning, notes[0].Severity);
    }

    [Theory]
    [InlineData("2026-01-15T09:15:00", Elsa3DateInterpretation.Refuse)]
    [InlineData("2026-07-15T09:15:00", Elsa3DateInterpretation.Refuse)]
    [InlineData("2026-03-29T02:30:00", Elsa3DateInterpretation.SourceTimeZone)]
    [InlineData("2026-10-25T02:30:00", Elsa3DateInterpretation.SourceTimeZone)]
    public void Missing_or_ambiguous_instants_are_refused_with_import_guidance(string text, Elsa3DateInterpretation interpretation)
    {
        var refusal = Assert.Throws<Elsa3LiteralConversion.LiteralRefusal>(() => Elsa3LiteralConversion.Rewrite(
            JsonSerializer.SerializeToElement(text, JsonBaseline.Options), typeof(DateTime), Options(interpretation), _ => { }));
        Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing, refusal.Code);
        Assert.Contains("SourceTimeZone", refusal.Guidance);
    }

    [Theory]
    [InlineData(typeof(DateTime), 1)]
    [InlineData(typeof(DateOnly), 1)]
    [InlineData(typeof(DateTimeOffset), 0)]
    public void An_explicit_offset_is_preserved_and_wall_clock_changes_are_visible(Type type, int warnings)
    {
        var notes = new List<Elsa3LiteralConversion.Note>();
        var value = Parse("\"2026-01-15T00:15:00+05:00\"");
        Assert.Equal(value.GetRawText(), Elsa3LiteralConversion.Rewrite(value, type, new(), notes.Add).GetRawText());
        Assert.Equal(warnings, notes.Count);
        Assert.All(notes, note => Assert.Equal(Elsa3ActivityDiagnosticCodes.LiteralDateWallClock, note.Code));
    }

    [Theory]
    [InlineData(Elsa3DateInterpretation.Refuse, false)]
    [InlineData(Elsa3DateInterpretation.SourceTimeZone, true)]
    [InlineData(Elsa3DateInterpretation.AssumeUtc, true)]
    public async Task Workflow_variable_defaults_use_the_feature_setting_and_record_it(Elsa3DateInterpretation interpretation, bool succeeds)
    {
        var definition = ConversionServices.Definition(Sequence("root"), variables:
            [new Elsa3Variable { Id = "at", Name = "at", TypeName = "DateTime", Value = "2026-07-15T09:15:00" }]);
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, feature: new Elsa3MappingFeature
        { DateInterpretation = interpretation, SourceTimeZone = interpretation == Elsa3DateInterpretation.SourceTimeZone ? "Europe/Amsterdam" : null });
        Assert.Equal(succeeds, result.Succeeded);
        if (!succeeds) Assert.Contains(result.Diagnostics, note => note.Code == Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing);
        else
        {
            Assert.Single(result.Diagnostics.Where(note => note.Code == Elsa3.Mapping.Services.Elsa3ImportDiagnosticCodes.DateInterpretationApplied));
            var initial = Assert.IsType<JsonElement>(Assert.Single(result.Value!.State.Variables).Default!.Value);
            Assert.Equal(interpretation == Elsa3DateInterpretation.SourceTimeZone ? "2026-07-15T07:15:00Z" : "2026-07-15T09:15:00Z", initial.GetString());
        }
        Assert.Equal("2026-07-15T09:15:00", Assert.Single(definition.Variables).Value);
    }

    private static Elsa3ConversionOptions Options(Elsa3DateInterpretation interpretation) => new()
    { DateInterpretation = interpretation, SourceTimeZone = interpretation == Elsa3DateInterpretation.SourceTimeZone ? "Europe/Amsterdam" : null };
    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private sealed record Item(string Code);
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, RefuseUnmatchedMembers = true)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(ScopedImportDerived), "scoped")]
    private abstract class ScopedImportBase;
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.Unspecified, RefuseUnmatchedMembers = true)]
    private sealed class ScopedImportDerived : ScopedImportBase
    {
        public string? CustomerName { get; set; }
        public DateTimeOffset StartAt { get; set; }
        public JsonElement? Carried { get; set; }
    }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    private sealed class ImportPolymorphicChildren
    {
        public ConcreteImportBase? OrderValue { get; set; }
        public List<ConcreteImportBase>? OrderValues { get; set; }
        public Dictionary<string, ConcreteImportBase>? OrdersByKey { get; set; }
    }
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(ConcreteImportDerived), "derived")]
    private class ConcreteImportBase
    {
        public string? CustomerName { get; set; }
        public DateTimeOffset StartAt { get; set; }
        public JsonElement? Carried { get; set; }
    }
    private sealed class ConcreteImportDerived : ConcreteImportBase;
    private sealed class RecursiveInputs
    {
        [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.Unspecified)] public RecursiveOrder? Value { get; set; }
    }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    private sealed class RecursiveOrder
    {
        public string? CustomerName { get; set; }
        public RecursiveOrder? Child { get; set; }
        public RecursiveOrder[]? Children { get; set; }
        public List<RecursiveOrder>? ListChildren { get; set; }
        public Dictionary<string, RecursiveOrder>? Named { get; set; }
        public DateTimeOffset? At { get; set; }
        public JsonElement? Carried { get; set; }
    }
    private sealed record Order(string? Name, Item[]? Items, Dictionary<string, Item>? ByKey);
    [JsonConversion(MemberMappings = ["order_id=Id"])]
    private sealed record ScopedOrder(string Id, [property: JsonPropertyName("display")] string Name);
    [JsonConversion(MemberMappings = ["order_id=Id"])] private readonly record struct ScopedStruct(string Id);
    private sealed record NullableScopes(ScopedStruct? One, List<ScopedStruct?> Items, Dictionary<string, ScopedStruct?> ByKey);
    private sealed class CustomHolder { [JsonConverter(typeof(CustomOrderConverter))] public Order? Order { get; init; } }
    private sealed class CustomOrderConverter : JsonConverter<Order>
    {
        public override Order? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Order value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(DatedOrder), "dated")]
    private abstract class PolymorphicOrder;
    private sealed class DatedOrder : PolymorphicOrder { public string? Name { get; init; } public DateTimeOffset At { get; init; } }
}

public sealed record JsonImportOrder(string? Id);
[ReplacesActivityType("Acme.JsonOrder")]
public sealed class JsonOrderImportActivity : ReplacingTestActivity
{
    [ActivityInput] public Uri? Url { get; set; }
    [ActivityInput(Key = "order"), JsonConversion(MemberMappings = ["order_id=Id"])]
    public JsonImportOrder? Payload { get; set; }
    [ActivityInput] public DateTime? At { get; set; }
    [ActivityInput] public DateTimeOffset? Offset { get; set; }
    [ActivityInput] public DateOnly? Day { get; set; }
}
