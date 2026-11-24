using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ValueConversionPlanTests
{
    [Theory]
    [InlineData("{\"Id\":9007199254740993,\"amount\":1234567890.123456789012345}")]
    [InlineData("[12345678901234567890,1.0,true,null]")]
    [InlineData("\"{text stays text}\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void Json_targets_preserve_value_kinds_casing_and_exact_numbers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var source = new ValueTypeDescriptor("Object");
        var target = new ValueTypeDescriptor("System.Text.Json.JsonElement");
        var plan = Plan(source, target, ValueRepresentation.TypedValue, ValueConversionOperation.CanonicalAny);
        var result = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(source, document.RootElement, ValueProtectionPolicy.InstanceInline), plan);

        Assert.Equal(json, result.InlineValue!.Value.GetRawText());
        Assert.Equal(document.RootElement.ValueKind, result.InlineValue.Value.ValueKind);
        Assert.Equal(target, result.Type);
    }

    [Fact]
    public void Identity_plan_pins_the_complete_versioned_contract_with_a_stable_fingerprint()
    {
        var type = new ValueTypeDescriptor("String");

        var first = ValueConversionPlan.Identity(type, ValueRepresentation.TextValue);
        var second = ValueConversionPlan.Identity(type, ValueRepresentation.TextValue);

        Assert.Equal(ValueConversionPlan.CurrentSchemaVersion, first.SchemaVersion);
        Assert.Equal(ValueRepresentation.TextValue, first.SourceRepresentation);
        Assert.Equal(type, first.SourceType);
        Assert.Equal(type, first.TargetType);
        Assert.Equal(ValueConversionMode.Auto, first.Mode);
        Assert.Equal(ValueConversionOperation.Identity, first.Operation);
        Assert.Null(first.Profile);
        Assert.StartsWith("sha256:", first.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Identity_plan_preserves_a_present_value_and_its_protection_policy()
    {
        var sourceType = new ValueTypeDescriptor("String");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var source = ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement("raw text"), policy);

        var converted = new RuntimeValueConversionExecutor().Convert(
            source,
            ValueConversionPlan.Identity(sourceType, ValueRepresentation.TextValue));

        Assert.Equal(ValuePresence.Present, converted.Presence);
        Assert.Equal(sourceType, converted.Type);
        Assert.Equal(policy, converted.Policy);
        Assert.Equal("raw text", converted.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Conversion_refuses_a_withheld_value_with_the_fixed_code()
    {
        // The shape a secret read takes: the envelope is typed to the Int32 input, while its plan converts from text.
        // Conversion reads the value, so it refuses before comparing the envelope's type with the plan's source.
        var targetType = new ValueTypeDescriptor("Int32");
        var plan = JsonPlan(targetType, sourceRepresentation: ValueRepresentation.TextValue);
        var withheld = WithheldValues.Secret(targetType, plan);

        var exception = Assert.Throws<WithheldValueException>(() => new RuntimeValueConversionExecutor().Convert(withheld, plan));

        Assert.Equal(SecretBindingDiagnostics.WithheldValueNotConverted("Int32").Message, exception.Message);
    }

    [Fact]
    public void Nullable_compatibility_preserves_an_explicit_null()
    {
        var type = new ValueTypeDescriptor("Int32");
        var nullableType = new ValueTypeDescriptor("Int32?");
        var plan = Plan(type, nullableType, ValueRepresentation.TypedValue, ValueConversionOperation.NullableCompatibility);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(ValuePresence.ExplicitNull, converted.Presence);
        Assert.Equal(nullableType, converted.Type);
    }

    [Fact]
    public void Numeric_widening_preserves_the_exact_json_number()
    {
        var sourceType = new ValueTypeDescriptor("Int32");
        var targetType = new ValueTypeDescriptor("Int64");
        var plan = Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.NumericWidening);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement(42), ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(targetType, converted.Type);
        Assert.Equal(42, converted.InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData(19.25, "19.25")]
    [InlineData(0.1, "0.1")]
    [InlineData(-0.0, "-0")]
    [InlineData(double.Epsilon, "5E-324")]
    public void Numeric_text_round_trips_independently_of_worker_culture(double value, string expected)
    {
        var plan = Plan(new("Double"), new("String"), ValueRepresentation.TypedValue, ValueConversionOperation.NumericToString);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var source = ValueEnvelope.Inline(plan.SourceType, JsonSerializer.SerializeToElement(value), policy);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var result = new RuntimeValueConversionExecutor().Convert(source, plan);
            Assert.Equal(expected, result.InlineValue!.Value.GetString());
            Assert.Equal(plan.TargetType, result.Type);
            Assert.Equal(policy, result.Policy);
            Assert.Equal(JsonValueKind.Number, source.InlineValue!.Value.ValueKind);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(ValuePresence.Absent)]
    [InlineData(ValuePresence.ExplicitNull)]
    public void Numeric_text_preserves_presence(ValuePresence presence)
    {
        var plan = Plan(new("Double"), new("String"), ValueRepresentation.TypedValue, ValueConversionOperation.NumericToString);
        var source = new ValueEnvelope(plan.SourceType, presence, null, null, ValueProtectionPolicy.InstanceInline);
        var result = new RuntimeValueConversionExecutor().Convert(source, plan);
        Assert.Equal(presence, result.Presence);
        Assert.Null(result.InlineValue);
    }

    [Theory]
    [InlineData("1e999")]
    [InlineData("\"19.25\"")]
    public void Numeric_text_rejects_invalid_double_payloads(string json)
    {
        var plan = Plan(new("Double"), new("String"), ValueRepresentation.TypedValue, ValueConversionOperation.NumericToString);
        using var document = JsonDocument.Parse(json);
        var source = ValueEnvelope.Inline(plan.SourceType, document.RootElement, ValueProtectionPolicy.InstanceInline);
        Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(source, plan));
    }

    [Fact]
    public void Numeric_narrowing_is_rejected_with_the_pinned_contract_diagnostic()
    {
        var plan = Plan(
            new ValueTypeDescriptor("Int64"),
            new ValueTypeDescriptor("Int32"),
            ValueRepresentation.TypedValue,
            ValueConversionOperation.NumericWidening);

        var exception = Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(plan.SourceType, JsonSerializer.SerializeToElement(42L), ValueProtectionPolicy.InstanceInline),
            plan));

        Assert.Contains("source representation 'TypedValue'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("target contract 'Int32 (Single)'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mode/profile 'Auto/none'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("safe widening", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_source_contract_must_match_the_pinned_plan()
    {
        var plan = Plan(
            new ValueTypeDescriptor("Int32"),
            new ValueTypeDescriptor("Int64"),
            ValueRepresentation.TypedValue,
            ValueConversionOperation.NumericWidening);
        var source = ValueEnvelope.Inline(
            new ValueTypeDescriptor("UInt32"),
            JsonSerializer.SerializeToElement(42U),
            ValueProtectionPolicy.InstanceInline);

        var exception = Assert.Throws<RuntimeValueConversionException>(() =>
            new RuntimeValueConversionExecutor().Convert(source, plan));

        Assert.Contains("runtime source contract 'UInt32 (Single)'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pinned source contract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recursive_collection_widening_keeps_each_numeric_element()
    {
        var sourceType = new ValueTypeDescriptor("Int32", CollectionKind.List);
        var targetType = new ValueTypeDescriptor("Int64", CollectionKind.List);
        var plan = Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.RecursiveCollection);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }), ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(targetType, converted.Type);
        Assert.Equal(new[] { 1, 2, 3 }, converted.InlineValue!.Value.EnumerateArray().Select(item => item.GetInt32()).ToArray());
    }

    [Fact]
    public void Collection_shape_conversion_preserves_the_text_of_every_carried_element()
    {
        const string json = """[ { "\u00e9<+>" : "Ren\u00e9<+>", "nested" : [1.0,9007199254740993,null,{"x":"a\/b"}] }, "é<+>","\u00e9<+>",1e0,0.10,null ]""";
        const string expected = """[{ "\u00e9<+>" : "Ren\u00e9<+>", "nested" : [1.0,9007199254740993,null,{"x":"a\/b"}] },"é<+>","\u00e9<+>",1e0,0.10,null]""";
        var sourceType = new ValueTypeDescriptor("Any", CollectionKind.List);
        var targetType = new ValueTypeDescriptor("Any", CollectionKind.Array);
        var plan = Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.RecursiveCollection);
        using var document = JsonDocument.Parse(json);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, document.RootElement, ValueProtectionPolicy.InstanceInline), plan);

        Assert.Equal(expected, converted.InlineValue!.Value.GetRawText());
        Assert.Equal(document.RootElement.EnumerateArray().Select(item => item.GetRawText()),
            converted.InlineValue.Value.EnumerateArray().Select(item => item.GetRawText()));
        Assert.Equal(targetType, converted.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canonical_any_projection_uses_the_relaxed_format_for_keys_and_nested_values(bool formattedContent)
    {
        const string json = """{ "\u00e9<+>" : {"raw":"a\/b","list":[{"null":null,"name":"Ren\u00e9<+>","n":1.0},9007199254740993]}, "z":null }""";
        const string expected = """{"z":null,"é<+>":{"list":[{"n":1.0,"name":"René<+>","null":null},9007199254740993],"raw":"a/b"}}""";
        var sourceType = new ValueTypeDescriptor("Acme.Customer");
        var targetType = new ValueTypeDescriptor("Any");
        var plan = formattedContent
            ? JsonPlan(targetType)
            : Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.CanonicalAny);
        using var document = JsonDocument.Parse(json);
        var source = formattedContent ? JsonSource(json)
            : ValueEnvelope.Inline(sourceType, document.RootElement, ValueProtectionPolicy.InstanceInline);

        var converted = new RuntimeValueConversionExecutor().Convert(source, plan);

        Assert.Equal(expected, converted.InlineValue!.Value.GetRawText());
        Assert.True(JsonCanonicalForm.Equals(document.RootElement, converted.InlineValue.Value));
        Assert.Equal(targetType, converted.Type);
    }

    [Fact]
    public void Canonical_any_projection_returns_json_not_an_arbitrary_clr_identity()
    {
        var sourceType = new ValueTypeDescriptor("Acme.Customer");
        var targetType = new ValueTypeDescriptor("Any");
        var plan = Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.CanonicalAny);
        using var document = JsonDocument.Parse("{\"z\":1,\"address\":{\"postal\":\"1000\",\"city\":\"Brussels\"},\"name\":\"Ada\"}");

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, document.RootElement, ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(targetType, converted.Type);
        Assert.Equal("{\"address\":{\"city\":\"Brussels\",\"postal\":\"1000\"},\"name\":\"Ada\",\"z\":1}", converted.InlineValue!.Value.GetRawText());
    }

    [Fact]
    public void Json_profile_decodes_formatted_object_content_to_canonical_any()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Any"));
        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("{\"z\":1,\"customer\":{\"name\":\"Ada\"}}"),
                ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal("{\"customer\":{\"name\":\"Ada\"},\"z\":1}", converted.InlineValue!.Value.GetRawText());
    }

    [Fact]
    public void Json_profile_decodes_formatted_array_content_to_canonical_any()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Any"));
        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("[{\"id\":2},{\"id\":3}]"),
                ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(2, converted.InlineValue!.Value.EnumerateArray().First().GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData(ValueRepresentation.TypedValue)]
    [InlineData(ValueRepresentation.StructuredValue)]
    public void Json_string_profile_serializes_the_inline_json_without_changing_content_or_protection(ValueRepresentation representation)
    {
        const string json = "{\"wire_name\":9007199254740993,\"amount\":1234567890.123456789012345,\"nested\":{\"value\":null}}";
        var sourceType = new ValueTypeDescriptor("Acme.Customer");
        var targetType = new ValueTypeDescriptor("System.String");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var plan = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            representation,
            sourceType,
            targetType,
            ValueConversionMode.Profile,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json-string", "1"),
            ValueConversionLimits.Default,
            null);
        using var document = JsonDocument.Parse(json);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, document.RootElement, policy), plan);

        Assert.Equal(ValuePresence.Present, converted.Presence);
        Assert.Equal(targetType, converted.Type);
        Assert.Equal(policy, converted.Policy);
        Assert.Equal(json, converted.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Json_string_profile_preserves_explicit_null_and_rejects_an_over_limit_payload()
    {
        var sourceType = new ValueTypeDescriptor("Acme.Customer");
        var targetType = new ValueTypeDescriptor("String");
        var plan = JsonStringPlan(sourceType, targetType, ValueRepresentation.StructuredValue);
        var absent = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Absent(sourceType, ValueProtectionPolicy.InstanceInline), plan);
        var nullValue = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Null(sourceType, ValueProtectionPolicy.InstanceInline), plan);
        using var rootNull = JsonDocument.Parse("null");
        var jsonNull = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, rootNull.RootElement, ValueProtectionPolicy.InstanceInline), plan);
        using var document = JsonDocument.Parse("{\"name\":\"Ada\"}");
        var limitedPlan = JsonStringPlan(sourceType, targetType, ValueRepresentation.StructuredValue,
            new ValueConversionLimits(64, 10_000, 4));

        Assert.Equal(ValuePresence.Absent, absent.Presence);
        Assert.Equal(ValuePresence.ExplicitNull, nullValue.Presence);
        Assert.Equal(targetType, nullValue.Type);
        Assert.Equal(ValuePresence.Present, jsonNull.Presence);
        Assert.Equal("null", jsonNull.InlineValue!.Value.GetString());
        Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, document.RootElement, ValueProtectionPolicy.InstanceInline), limitedPlan));
    }

    [Fact]
    public void Json_string_profile_serializes_structured_collections_as_ordered_json_arrays()
    {
        const string json = "[{\"id\":1},null,{\"id\":2}]";
        var sourceType = new ValueTypeDescriptor("Acme.Customer", CollectionKind.List);
        using var document = JsonDocument.Parse(json);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, document.RootElement, ValueProtectionPolicy.InstanceInline),
            JsonStringPlan(sourceType, new ValueTypeDescriptor("String"), ValueRepresentation.StructuredValue));

        Assert.Equal(json, converted.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Json_profile_requires_object_shape_for_json_object_targets()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("JsonObject"));

        var exception = Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("[1,2]"),
                ValueProtectionPolicy.InstanceInline),
            plan));

        Assert.Contains("JSON object root", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_json_reports_malformed_formatted_content()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Any"), ValueConversionMode.Json);

        var exception = Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("{not-json"),
                ValueProtectionPolicy.InstanceInline),
            plan));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_value_to_any_remains_a_json_string_under_auto_without_json_sniffing()
    {
        var sourceType = new ValueTypeDescriptor("String");
        var targetType = new ValueTypeDescriptor("Any");
        var plan = Plan(sourceType, targetType, ValueRepresentation.TextValue, ValueConversionOperation.CanonicalAny);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement("{\"name\":\"Ada\"}"), ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(JsonValueKind.String, converted.InlineValue!.Value.ValueKind);
        Assert.Equal("{\"name\":\"Ada\"}", converted.InlineValue.Value.GetString());
    }

    [Fact]
    public void Json_profile_enforces_payload_depth_and_node_limits()
    {
        var smallPayload = JsonPlan(new ValueTypeDescriptor("Any"), limits: new ValueConversionLimits(8, 10, 4));
        var shallow = JsonPlan(new ValueTypeDescriptor("Any"), limits: new ValueConversionLimits(1, 10, 1_024));
        var fewNodes = JsonPlan(new ValueTypeDescriptor("Any"), limits: new ValueConversionLimits(8, 2, 1_024));
        var executor = new RuntimeValueConversionExecutor();

        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("{\"name\":\"Ada\"}"), ValueProtectionPolicy.InstanceInline),
            smallPayload));
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("{\"nested\":{\"name\":\"Ada\"}}"), ValueProtectionPolicy.InstanceInline),
            shallow));
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("{\"a\":1,\"b\":2}"), ValueProtectionPolicy.InstanceInline),
            fewNodes));
    }

    [Fact]
    public void Json_profile_converts_formatted_object_content_to_registered_typed_alias()
    {
        var plan = JsonPlan(new ValueTypeDescriptor("Acme.Customer"));
        var converted = TypedExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("""
                {"name":"Ada","loyaltyPoints":42,"address":{"city":"Brussels"},"orders":[{"id":"A-1","total":19.5}],"age":null}
                """),
                ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal("Ada", converted.InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal("Brussels", converted.InlineValue.Value.GetProperty("address").GetProperty("city").GetString());
        Assert.Equal("A-1", converted.InlineValue.Value.GetProperty("orders")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, converted.InlineValue.Value.GetProperty("age").ValueKind);
    }

    [Fact]
    public void Json_profile_converts_structured_arrays_to_registered_collection_aliases()
    {
        var plan = JsonPlan(
            new ValueTypeDescriptor("Acme.Customer", CollectionKind.List),
            sourceRepresentation: ValueRepresentation.StructuredValue,
            sourceType: new ValueTypeDescriptor("JsonNode"));
        using var source = JsonDocument.Parse("""[{"name":"Ada","loyaltyPoints":1},{"name":"Lin","loyaltyPoints":2}]""");

        var converted = TypedExecutor().Convert(
            ValueEnvelope.Inline(new ValueTypeDescriptor("JsonNode"), source.RootElement.Clone(), ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal("Lin", converted.InlineValue!.Value[1].GetProperty("name").GetString());
        Assert.Equal(2, converted.InlineValue.Value[1].GetProperty("loyaltyPoints").GetInt64());
    }

    [Fact]
    public void Json_profile_reports_typed_member_failures_with_target_contract_and_location()
    {
        var executor = TypedExecutor();
        var plan = JsonPlan(new ValueTypeDescriptor("Acme.Customer"));

        var missing = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            JsonSource("""{"loyaltyPoints":42}"""),
            plan));
        var numeric = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            JsonSource("""{"name":"Ada","loyaltyPoints":9223372036854775808}"""),
            plan));

        Assert.Contains("Acme.Customer", missing.Message, StringComparison.Ordinal);
        Assert.Contains("name", missing.Reason, StringComparison.Ordinal);
        Assert.Contains("'$'", missing.Reason, StringComparison.Ordinal);
        Assert.Contains("$.loyaltyPoints", numeric.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("default", "{ \"customerName\": \"Ren\\u00e9\" }")]
    [InlineData("snake", "{ \"customer_name\": \"Ren\\u00e9\" }")]
    [InlineData("mapping", "{ \"customer_id\": \"Ren\\u00e9\" }")]
    [InlineData("insensitive", "{ \"CUSTOMERNAME\": \"Ren\\u00e9\" }")]
    [InlineData("nested", "{ \"customer\": { \"customer_name\": \"Ren\\u00e9\" } }")]
    [InlineData("custom", "{ \"wireName\": \"Ren\\u00e9\" }")]
    public void Json_profile_validates_the_selected_type_contract_without_rewriting_data(string alias, string json)
    {
        var executor = ContractExecutor();
        var diagnostics = new List<Elsa.Serialization.Core.JsonConversionDiagnostic>();

        var converted = executor.Convert(JsonSource(json), JsonPlan(new ValueTypeDescriptor(alias)), diagnostics.Add);

        Assert.Equal(json, converted.InlineValue!.Value.GetRawText());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Json_profile_reports_default_exact_match_warnings_once_and_preserves_unknown_members()
    {
        const string json = "{ \"CustomerName\":\"Ada\", \"n\":1.0 }";
        var diagnostics = new List<Elsa.Serialization.Core.JsonConversionDiagnostic>();

        var converted = ContractExecutor().Convert(JsonSource(json), JsonPlan(new ValueTypeDescriptor("default")), diagnostics.Add);

        Assert.Equal(json, converted.InlineValue!.Value.GetRawText());
        Assert.Equal(new[] { "$[\"CustomerName\"]", "$[\"n\"]" }, diagnostics.Select(diagnostic => diagnostic.Path));
        Assert.All(diagnostics, diagnostic => Assert.Equal("json-unmatched-member", diagnostic.Code));
        Assert.All(diagnostics, diagnostic => Assert.Equal(typeof(DefaultJsonContract), diagnostic.TargetType));
    }

    [Fact]
    public void Json_profile_respects_strict_unmatched_members_and_only_emits_warnings_after_success()
    {
        var executor = ContractExecutor();
        var diagnostics = new List<Elsa.Serialization.Core.JsonConversionDiagnostic>();
        var strict = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            JsonSource("{\"CustomerName\":\"Ada\"}"), JsonPlan(new ValueTypeDescriptor("strict")), diagnostics.Add));
        Assert.Contains("json-unmatched-member-refused", strict.Reason, StringComparison.Ordinal);
        Assert.Empty(diagnostics);

        var invalid = Assert.Throws<RuntimeValueConversionException>(() => TypedExecutor().Convert(
            JsonSource("{\"unknown\":true,\"name\":\"Ada\",\"loyaltyPoints\":\"5\"}"),
            JsonPlan(new ValueTypeDescriptor("Acme.Customer")), diagnostics.Add));
        Assert.Contains("$.loyaltyPoints", invalid.Reason, StringComparison.Ordinal);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Diagnostic_overload_keeps_existing_conversion_providers_callable()
    {
        var provider = new ExistingConversionProvider();
        Elsa.Workflows.Runtime.Core.Contracts.IRuntimeValueConversionExecutor executor = provider;
        var type = new ValueTypeDescriptor("String");
        var plan = ValueConversionPlan.Identity(type, ValueRepresentation.TypedValue);
        var diagnostics = new List<JsonConversionDiagnostic>();
        var source = JsonSource("retained text");

        var result = executor.Convert(source, plan, diagnostics.Add);

        Assert.Same(source, result);
        Assert.Equal(1, provider.Calls);
        Assert.Empty(diagnostics);
        Assert.Same(source, executor.Convert(source, plan, JsonConversionContract.For(typeof(string)), diagnostics.Add));
        Assert.Equal(2, provider.Calls);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Xml_profile_converts_formatted_content_to_registered_typed_alias()
    {
        var plan = XmlPlan(new ValueTypeDescriptor("Acme.Customer"));

        var converted = TypedExecutor().Convert(
            XmlSource("""
            <customer loyaltyPoints="42">
              <name>Ada</name>
              <address>
                <city>Brussels</city>
              </address>
              <orders>
                <order>
                  <id>A-1</id>
                  <total>19.5</total>
                </order>
              </orders>
            </customer>
            """),
            plan);

        Assert.Equal("Ada", converted.InlineValue!.Value.GetProperty("name").GetString());
        Assert.Equal(42, converted.InlineValue.Value.GetProperty("loyaltyPoints").GetInt64());
        Assert.Equal("Brussels", converted.InlineValue.Value.GetProperty("address").GetProperty("city").GetString());
        Assert.Equal("A-1", converted.InlineValue.Value.GetProperty("orders")[0].GetProperty("id").GetString());
        Assert.Equal(19.5m, converted.InlineValue.Value.GetProperty("orders")[0].GetProperty("total").GetDecimal());
    }

    [Theory]
    [InlineData("snake", "<root><CUSTOMERNAME>Ada</CUSTOMERNAME></root>", "customer_name")]
    [InlineData("mapping", "<root><customerName>Ada</customerName></root>", "customer_id")]
    [InlineData("insensitive", "<root><CUSTOMERNAME>Ada</CUSTOMERNAME></root>", "customerName")]
    [InlineData("strict", "<root><customerName>Ada</customerName></root>", "customerName")]
    [InlineData("custom", "<root><wireName>Ada</wireName></root>", "wireName")]
    public void Xml_source_names_remain_compatible_while_output_and_hydration_use_the_type_contract(string alias, string xml, string name)
    {
        var diagnostics = new List<JsonConversionDiagnostic>();
        var result = ContractExecutor().Convert(XmlSource(xml), XmlPlan(new(alias)), diagnostics.Add).InlineValue!.Value;
        Assert.Equal("Ada", result.GetProperty(name).GetString());
        Assert.Single(result.EnumerateObject());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Xml_JsonPropertyName_source_and_explicit_JSON_mapping_have_distinct_roles()
    {
        var type = new ValueTypeDescriptor("named");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = typeof(NamedXmlContract) }));
        var result = executor.Convert(XmlSource("<root><wire_name>Ada</wire_name></root>"), XmlPlan(type)).InlineValue!.Value;
        Assert.Equal("Ada", result.GetProperty("customer_id").GetString());
        Assert.Equal("Ada", JsonConversionContract.Deserialize<NamedXmlContract>(result)!.CustomerName);
        Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(XmlSource("<root><customerName>Ada</customerName></root>"), XmlPlan(type)));
    }

    [Fact]
    public void Xml_member_scope_and_recursive_type_scopes_round_trip_without_adding_absent_members()
    {
        var type = new ValueTypeDescriptor("recursive");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = typeof(RecursiveXmlContract) }));
        var contract = JsonConversionContract.For(typeof(RecursiveXmlContract), typeof(XmlInputs).GetProperty(nameof(XmlInputs.Root))!);
        var result = executor.Convert(XmlSource("""
            <root><customerName>Root</customerName>
              <child><customerName>Child</customerName><child><customerName>Grandchild</customerName></child></child>
              <children><item><customerName>Array</customerName></item></children>
              <named><KeepKey><customerName>Dictionary</customerName></KeepKey></named>
            </root>
            """), XmlPlan(type), contract, null).InlineValue!.Value;
        Assert.Equal("Root", result.GetProperty("CustomerName").GetString());
        Assert.Equal("Child", result.GetProperty("Child").GetProperty("customer_name").GetString());
        Assert.Equal("Grandchild", result.GetProperty("Child").GetProperty("child").GetProperty("customer_name").GetString());
        Assert.Equal("Array", result.GetProperty("Children")[0].GetProperty("customer_name").GetString());
        Assert.Equal("Dictionary", result.GetProperty("Named").GetProperty("KeepKey").GetProperty("customer_name").GetString());
        Assert.False(result.TryGetProperty("Optional", out _));
        Assert.False(result.GetProperty("Child").TryGetProperty("optional", out _));
        var typed = (RecursiveXmlContract)contract.Read(result, typeof(RecursiveXmlContract))!;
        Assert.Equal("Array", typed.Children![0].CustomerName);
        Assert.Equal("Dictionary", typed.Named!["KeepKey"].CustomerName);
    }

    [Fact]
    public void Xml_temporal_and_scalar_values_use_the_contract_in_both_directions()
    {
        var type = new ValueTypeDescriptor("temporal");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = typeof(TemporalXmlContract) }));
        var result = executor.Convert(XmlSource("""
            <root><at>2026-10-28T09:15:00+05:00</at><offset>2026-10-28T09:15:00+05:00</offset>
              <day>2026-10-28T23:30:00-02:00</day><duration>01:30:00</duration><id>AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE</id>
            </root>
            """), XmlPlan(type)).InlineValue!.Value;
        Assert.Equal("2026-10-28T04:15:00Z", result.GetProperty("at").GetString());
        Assert.Equal("2026-10-28T09:15:00+05:00", result.GetProperty("offset").GetString());
        Assert.Equal("2026-10-29", result.GetProperty("day").GetString());
        Assert.Equal("PT1H30M", result.GetProperty("duration").GetString());
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", result.GetProperty("id").GetString());
        var refused = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(XmlSource("<root><at>2026-10-28T09:15:00</at></root>"), XmlPlan(type)));
        Assert.Contains("json-datetime-offset-missing", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("at", refused.Reason, StringComparison.Ordinal);
        var absentInstant = executor.Convert(XmlSource("<root><duration>01:30:00</duration></root>"), XmlPlan(type)).InlineValue!.Value;
        Assert.Equal("PT1H30M", absentInstant.GetProperty("duration").GetString());
        Assert.False(absentInstant.TryGetProperty("at", out _));
    }

    [Fact]
    public void Xml_does_not_infer_the_CLR_shape_of_an_opaque_user_converter()
    {
        var type = new ValueTypeDescriptor("opaque");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = typeof(OpaqueXmlContract) }));
        var refused = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(XmlSource("<root><customerName>Ada</customerName></root>"), XmlPlan(type)));
        Assert.Contains("no XML member-matching contract", refused.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Xml_property_converters_can_delegate_to_normal_type_metadata_without_becoming_global(bool nullableFactory)
    {
        const string dateText = "2026-10-28T09:15:00";
        var date = JsonSerializer.SerializeToElement(dateText, JsonBaseline.Options);
        var at = new DateTimeOffset(2026, 10, 28, 9, 15, 0, TimeSpan.FromHours(5));
        object holder = nullableFactory
            ? new NullableDelegatingXmlHolder { Value = new() { CustomerName = "Ada", RawDate = date, At = at } }
            : new DelegatingXmlHolder { Value = new() { CustomerName = "Ada", RawDate = date, At = at } };
        var holderType = holder.GetType();
        var options = JsonConversionContract.For(holderType).Options;
        // This is legitimate property-converter delegation before the XML path is involved.
        var direct = JsonSerializer.SerializeToElement(holder, holderType, options);
        Assert.Equal("Ada", direct.GetProperty("value").GetProperty("customerName").GetString());
        Assert.Equal(dateText, direct.GetProperty("value").GetProperty("rawDate").GetString());

        var type = new ValueTypeDescriptor("delegating-member");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = holderType }));
        var result = executor.Convert(XmlSource("""
            <root><value><customerName>Ada</customerName><rawDate>2026-10-28T09:15:00</rawDate><at>2026-10-28T09:15:00+05:00</at></value></root>
            """), XmlPlan(type)).InlineValue!.Value;
        Assert.Equal("Ada", result.GetProperty("value").GetProperty("customerName").GetString());
        Assert.Equal(dateText, result.GetProperty("value").GetProperty("rawDate").GetString());
        Assert.Equal("2026-10-28T09:15:00+05:00", result.GetProperty("value").GetProperty("at").GetString());
        Assert.True(JsonCanonicalForm.Equals(direct, result));
    }

    [Theory]
    [InlineData("<root><customerName>Ada</customerName><CUSTOMERNAME>Lin</CUSTOMERNAME></root>")]
    [InlineData("<root customerName=\"Ada\" CUSTOMERNAME=\"Lin\" />")]
    [InlineData("<root><named><KeepKey><customerName>Ada</customerName></KeepKey><KeepKey><customerName>Lin</customerName></KeepKey></named></root>")]
    public void Xml_refuses_ambiguous_source_members_instead_of_losing_one_value(string xml)
    {
        var type = new ValueTypeDescriptor("recursive");
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type> { [type.Alias] = typeof(RecursiveXmlContract) }));
        var refused = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(XmlSource(xml), XmlPlan(type)));
        Assert.Contains("ambiguous", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_projection_uses_the_relaxed_format_for_keys_and_nested_values()
    {
        var targetType = new ValueTypeDescriptor("Acme.XmlFormatting");
        var plan = XmlPlan(targetType);
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(
            new Dictionary<string, Type> { [targetType.Alias] = typeof(XmlFormattingContract) }));
        var converted = executor.Convert(XmlSource("""
            <root>
              <éLabel>Top é&lt;+&gt; &#92;u00e9</éLabel>
              <nested>
                <names><café>Ren&#xE9;&lt;+&gt;</café></names>
                <number>1234567890.123456789012345</number>
                <whole>9007199254740993</whole>
              </nested>
            </root>
            """), plan);

        Assert.Equal("""{"nested":{"names":{"café":"René<+>"},"number":1234567890.123456789012345,"whole":9007199254740993},"éLabel":"Top é<+> \\u00e9"}""",
            converted.InlineValue!.Value.GetRawText());
        Assert.False(converted.InlineValue.Value.TryGetProperty("optional", out _));

        // XML has no JSON-null token. Its existing explicit-null envelope bypass remains unchanged.
        var nullValue = executor.Convert(ValueEnvelope.Null(plan.SourceType, ValueProtectionPolicy.InstanceInline), plan);
        Assert.Equal(ValuePresence.ExplicitNull, nullValue.Presence);
        Assert.Null(nullValue.InlineValue);
        Assert.Equal(targetType, nullValue.Type);
    }

    [Fact]
    public void Xml_profile_reports_unknown_ambiguous_and_missing_members()
    {
        var executor = TypedExecutor();
        var plan = XmlPlan(new ValueTypeDescriptor("Acme.Customer"));

        var unknown = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            XmlSource("<customer loyaltyPoints=\"42\"><name>Ada</name><legacy>true</legacy></customer>"),
            plan));
        var ambiguous = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            XmlSource("<customer loyaltyPoints=\"42\"><name>Ada</name><loyaltyPoints>43</loyaltyPoints></customer>"),
            plan));
        var missing = Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(
            XmlSource("<customer loyaltyPoints=\"42\" />"),
            plan));

        Assert.Contains("XML element 'legacy' at '$'", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous XML mappings for member 'loyaltyPoints' at '$'", ambiguous.Message, StringComparison.Ordinal);
        Assert.Contains("missing required XML member 'name' at '$'", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_profile_disables_dtd_entities_and_external_resources()
    {
        var plan = XmlPlan(new ValueTypeDescriptor("Acme.Customer"));

        var exception = Assert.Throws<RuntimeValueConversionException>(() => TypedExecutor().Convert(
            XmlSource("""
            <!DOCTYPE customer [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <customer loyaltyPoints="42">
              <name>&xxe;</name>
            </customer>
            """),
            plan));

        Assert.Contains("malformed or unsafe XML content", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_profile_rejects_any_targets_without_a_named_xml_to_json_profile()
    {
        var plan = XmlPlan(new ValueTypeDescriptor("Any"));

        var exception = Assert.Throws<RuntimeValueConversionException>(() => new RuntimeValueConversionExecutor().Convert(
            XmlSource("<customer><name>Ada</name></customer>"),
            plan));

        Assert.Contains("XML has no universal canonical Any projection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_conversion_preserves_an_external_reference_without_materializing_it()
    {
        var type = new ValueTypeDescriptor("String");
        var reference = new DurableValueExternalReference("blob", "customer/42", new Dictionary<string, string>());

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.External(type, reference, ValueProtectionPolicy.InstanceInline),
            ValueConversionPlan.Identity(type, ValueRepresentation.DurableReference));

        Assert.Same(reference, converted.ExternalReference);
        Assert.Null(converted.InlineValue);
    }

    [Fact]
    public void Transient_resource_null_is_retyped_without_materializing_or_persisting_a_resource()
    {
        var sourceType = new ValueTypeDescriptor("Stream");
        var targetType = new ValueTypeDescriptor("Stream");
        var plan = Plan(sourceType, targetType, ValueRepresentation.TransientResource, ValueConversionOperation.Identity);

        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Null(sourceType, ValueProtectionPolicy.Transient),
            plan);

        Assert.Equal(ValuePresence.ExplicitNull, converted.Presence);
        Assert.Equal(targetType, converted.Type);
        Assert.Null(converted.InlineValue);
        Assert.Null(converted.ExternalReference);
        Assert.Equal(ValueProtectionPolicy.Transient, converted.Policy);
    }

    [Fact]
    public async Task Materializer_applies_an_explicit_pinned_plan_once_before_creating_the_input_snapshot()
    {
        var sourceType = new ValueTypeDescriptor("Int32");
        var targetType = new ValueTypeDescriptor("Int64");
        var plan = Plan(sourceType, targetType, ValueRepresentation.TypedValue, ValueConversionOperation.NumericWidening);
        var binding = new RuntimeInputBinding(
            "count",
            targetType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement(42), ValueProtectionPolicy.InstanceInline),
            conversionPlan: plan);
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test" });
        var contract = new ActivityContract(
            "test/activity",
            "1",
            "test",
            descriptor,
            [new ActivityInputContract("count", "Count", targetType, true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/activity"));
        var node = new ExecutableNode(
            "node",
            "node",
            "test/activity",
            "1",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["count"] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);

        var snapshot = await new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver())
            .MaterializeSnapshotAsync(
                node,
                "invocation",
                new RuntimeInputBindingResolutionContext("workflow", "invocation"),
                DateTimeOffset.UnixEpoch);

        Assert.Equal(targetType, snapshot.Values["count"].Type);
        Assert.Equal(42, snapshot.Values["count"].InlineValue!.Value.GetInt32());
    }

    [Theory]
    [InlineData("public")]
    [InlineData("sensitive")]
    [InlineData("redacted")]
    [InlineData("retained")]
    public async Task Materializer_records_one_typed_profile_warning_with_the_source_protection(string protection)
    {
        const string json = "{ \"customerName\":\"Ada\", \"SecretBranch\":{\"n\":1.0} }";
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            isSensitive: protection == "sensitive", redactionMode: protection == "redacted" ? "mask" : null,
            retentionPolicy: protection == "retained" ? "retain" : null);
        var type = new ValueTypeDescriptor("default");
        var binding = new RuntimeInputBinding("customer", type, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement(json), policy),
            conversionPlan: JsonPlan(type));
        var descriptor = JsonSerializer.SerializeToElement(new { type = "test" });
        var contract = new ActivityContract("test/activity", "1", "test", descriptor,
            [new ActivityInputContract("customer", "Customer", type, true, false, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"], new ActivityActivationRequirement("test", "test/activity"));
        var node = new ExecutableNode("node", "node", "test/activity", "1", "test", descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["customer"] = binding }, new Dictionary<string, string>(), activityContract: contract);
        var warnings = new ExecutionWarningCollector();
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation") { Warnings = warnings };
        var materializer = new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), [], valueConversionExecutor: ContractExecutor());

        var snapshot = await materializer.MaterializeSnapshotAsync(node, "invocation", context, DateTimeOffset.UnixEpoch);

        Assert.Equal(json, snapshot.Values["customer"].InlineValue!.Value.GetRawText());
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal("json-unmatched-member", warning.Code);
        Assert.Equal(protection == "public" ? "args.customer[\"SecretBranch\"]" : "args.customer", warning.Details!["path"]);
        if (protection != "public") Assert.DoesNotContain("SecretBranch", warning.Message, StringComparison.Ordinal);
    }

    private static ValueConversionPlan Plan(
        ValueTypeDescriptor sourceType,
        ValueTypeDescriptor targetType,
        ValueRepresentation representation,
        ValueConversionOperation operation) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            representation,
            sourceType,
            targetType,
            ValueConversionMode.Auto,
            operation,
            profile: null,
            limits: ValueConversionLimits.Default,
            options: null);

    private static ValueConversionPlan JsonPlan(
        ValueTypeDescriptor targetType,
        ValueConversionMode mode = ValueConversionMode.Auto,
        ValueConversionLimits? limits = null,
        ValueRepresentation sourceRepresentation = ValueRepresentation.FormattedContent,
        ValueTypeDescriptor? sourceType = null) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            sourceRepresentation,
            sourceType ?? new ValueTypeDescriptor("String"),
            targetType,
            mode,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: limits ?? ValueConversionLimits.Default,
            options: null);

    private static ValueConversionPlan XmlPlan(ValueTypeDescriptor targetType, ValueConversionMode mode = ValueConversionMode.Xml) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"),
            targetType,
            mode,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.xml", "1"),
            limits: ValueConversionLimits.Default,
            options: null);

    private static ValueConversionPlan JsonStringPlan(
        ValueTypeDescriptor sourceType,
        ValueTypeDescriptor targetType,
        ValueRepresentation representation,
        ValueConversionLimits? limits = null) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            representation,
            sourceType,
            targetType,
            ValueConversionMode.JsonString,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json-string", "1"),
            limits ?? ValueConversionLimits.Default,
            options: null);

    private static ValueEnvelope JsonSource(string json) =>
        ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement(json),
            ValueProtectionPolicy.InstanceInline);

    private static ValueEnvelope XmlSource(string xml) =>
        ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement(xml),
            ValueProtectionPolicy.InstanceInline);

    [Theory]
    [InlineData("Boolean", "true", true)]
    [InlineData("Boolean", "false", true)]
    [InlineData("Boolean", "\"true\"", false)]
    [InlineData("Boolean", "1", false)]
    [InlineData("String", "\"order-1\"", true)]
    [InlineData("String", "true", false)]
    [InlineData("String", "42", false)]
    [InlineData("Int32", "42", true)]
    [InlineData("Int32", "42.5", false)]
    [InlineData("Int32", "\"42\"", false)]
    [InlineData("Int32", "true", false)]
    [InlineData("Double", "42.5", true)]
    [InlineData("Double", "\"42.5\"", false)]
    public void A_dynamic_value_converts_into_a_scalar_only_from_the_matching_json_kind(string target, string json, bool converts)
    {
        // Spec 1228 FR-025 (D-12): a JSON value whose kind matches the scalar target converts; text is never parsed into a
        // boolean or a number, and a number that does not fit an integral target is refused.
        var source = new ValueTypeDescriptor("Any");
        var plan = new ValueConversionPlan(1, ValueRepresentation.StructuredValue, source, new ValueTypeDescriptor(target),
            ValueConversionMode.Auto, ValueConversionOperation.Profile, new ValueConversionProfileReference("elsa.json", "1"), null, null);
        var executor = new RuntimeValueConversionExecutor(new TestTypeRegistry(new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Boolean"] = typeof(bool), ["String"] = typeof(string), ["Int32"] = typeof(int), ["Double"] = typeof(double)
        }));
        var value = ValueEnvelope.Inline(source, JsonSerializer.Deserialize<JsonElement>(json), ValueProtectionPolicy.InstanceInline);

        if (converts)
            Assert.Equal(json, executor.Convert(value, plan).InlineValue!.Value.GetRawText());
        else
            Assert.Throws<RuntimeValueConversionException>(() => executor.Convert(value, plan));
    }

    private static RuntimeValueConversionExecutor TypedExecutor() => new(new TestTypeRegistry(
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Acme.Customer"] = typeof(CustomerContract)
        }));

    private static RuntimeValueConversionExecutor ContractExecutor() => new(new TestTypeRegistry(
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = typeof(DefaultJsonContract),
            ["snake"] = typeof(SnakeJsonContract),
            ["mapping"] = typeof(MappedJsonContract),
            ["insensitive"] = typeof(InsensitiveJsonContract),
            ["nested"] = typeof(NestedJsonContract),
            ["strict"] = typeof(StrictJsonContract),
            ["custom"] = typeof(AlternateNameJsonContract)
        }));

    private sealed record DefaultJsonContract
    {
        public string? CustomerName { get; init; }
    }

    [Elsa.Serialization.Core.JsonConversion(NamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
    private sealed record SnakeJsonContract
    {
        public string? CustomerName { get; init; }
    }

    [Elsa.Serialization.Core.JsonConversion(MemberMappings = ["customer_id=CustomerName"])]
    private sealed record MappedJsonContract
    {
        public string? CustomerName { get; init; }
    }

    [Elsa.Serialization.Core.JsonConversion(CaseInsensitive = true)]
    private sealed record InsensitiveJsonContract
    {
        public string? CustomerName { get; init; }
    }

    private sealed record NestedJsonContract
    {
        public SnakeJsonContract? Customer { get; init; }
    }

    [Elsa.Serialization.Core.JsonConversion(RefuseUnmatchedMembers = true)]
    private sealed record StrictJsonContract
    {
        public string? CustomerName { get; init; }
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(AlternateNameJsonConverter))]
    private sealed record AlternateNameJsonContract(string CustomerName);

    private sealed class AlternateNameJsonConverter : System.Text.Json.Serialization.JsonConverter<AlternateNameJsonContract>, IJsonConversionMemberMatcher
    {
        public AlternateNameJsonConverter() { }
        public bool TryGetMemberType(string name, out Type memberType)
        {
            memberType = typeof(string);
            return name == "wireName";
        }
        public override AlternateNameJsonContract Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return new(document.RootElement.GetProperty("wireName").GetString()!);
        }
        public override void Write(Utf8JsonWriter writer, AlternateNameJsonContract value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteString("wireName", value.CustomerName); writer.WriteEndObject();
        }
    }

    private sealed class ExistingConversionProvider : Elsa.Workflows.Runtime.Core.Contracts.IRuntimeValueConversionExecutor
    {
        public int Calls { get; private set; }
        public ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan) { Calls++; return source; }
    }

    private sealed class TestTypeRegistry(IReadOnlyDictionary<string, Type> aliases) : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            var match = aliases.SingleOrDefault(item => item.Value == type);
            alias = match.Key!;
            return match.Value is not null;
        }

        public bool TryGetType(string alias, out Type type) => aliases.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => aliases.Values;
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetType(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }

    private sealed record XmlFormattingContract
    {
        [System.Text.Json.Serialization.JsonPropertyName("éLabel")]
        public required string Label { get; init; }
        public XmlFormattingNestedContract? Nested { get; init; }
        public string? Optional { get; init; }
    }

    [JsonConversion(MemberMappings = ["customer_id=CustomerName"])]
    private sealed record NamedXmlContract
    {
        [System.Text.Json.Serialization.JsonPropertyName("wire_name")]
        public required string CustomerName { get; init; }
    }

    private sealed class XmlInputs
    {
        [JsonConversion(NamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.Unspecified)]
        public RecursiveXmlContract? Root { get; set; }
    }

    [JsonConversion(NamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower)]
    private sealed record RecursiveXmlContract
    {
        public string? CustomerName { get; init; }
        public RecursiveXmlContract? Child { get; init; }
        public RecursiveXmlContract[]? Children { get; init; }
        public Dictionary<string, RecursiveXmlContract>? Named { get; init; }
        public string? Optional { get; init; }
    }

    private sealed record TemporalXmlContract
    {
        public DateTime At { get; init; }
        public DateTimeOffset Offset { get; init; }
        public DateOnly Day { get; init; }
        public TimeSpan Duration { get; init; }
        public Guid Id { get; init; }
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(OpaqueXmlConverter))]
    private sealed record OpaqueXmlContract(string CustomerName);

    private sealed class OpaqueXmlConverter : System.Text.Json.Serialization.JsonConverter<OpaqueXmlContract>
    {
        public OpaqueXmlConverter() { }
        public override OpaqueXmlContract Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new InvalidOperationException("An opaque converter must not be given an inferred XML object.");
        public override void Write(Utf8JsonWriter writer, OpaqueXmlContract value, JsonSerializerOptions options) => throw new InvalidOperationException("An opaque converter must not be given an inferred XML object.");
    }

    private sealed class DelegatingXmlHolder
    {
        [JsonConverter(typeof(DelegatingXmlMemberConverter))] public DelegatingXmlValue? Value { get; init; }
    }

    private sealed class NullableDelegatingXmlHolder
    {
        [JsonConverter(typeof(NullableDelegatingXmlMemberFactory))] public NullableDelegatingXmlValue? Value { get; init; }
    }

    private sealed record DelegatingXmlValue
    {
        public string? CustomerName { get; init; }
        public JsonElement RawDate { get; init; }
        public DateTimeOffset At { get; init; }
    }

    private readonly record struct NullableDelegatingXmlValue
    {
        public string? CustomerName { get; init; }
        public JsonElement RawDate { get; init; }
        public DateTimeOffset At { get; init; }
    }

    private static bool MatchDelegatingXmlMember(string name, out Type memberType)
    {
        memberType = name == "at" ? typeof(DateTimeOffset) : typeof(string);
        return name is "customerName" or "rawDate" or "at";
    }

    private sealed class DelegatingXmlMemberConverter : JsonConverter<DelegatingXmlValue>, IJsonConversionMemberMatcher
    {
        private int writeDepth;
        public DelegatingXmlMemberConverter() { }
        public bool TryGetMemberType(string name, out Type memberType) => MatchDelegatingXmlMember(name, out memberType);
        public override DelegatingXmlValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<DelegatingXmlValue>(ref reader, options);
        public override void Write(Utf8JsonWriter writer, DelegatingXmlValue value, JsonSerializerOptions options)
        {
            if (writeDepth > 0) throw new JsonException("xml-property-converter-recursion: property delegation selected itself.");
            writeDepth++;
            try { JsonSerializer.Serialize(writer, value, options); }
            finally { writeDepth--; }
        }
    }

    private sealed class NullableDelegatingXmlMemberFactory : JsonConverterFactory
    {
        public NullableDelegatingXmlMemberFactory() { }
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(NullableDelegatingXmlValue?);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => new NullableDelegatingXmlMemberConverter();
    }

    private sealed class NullableDelegatingXmlMemberConverter : JsonConverter<NullableDelegatingXmlValue?>, IJsonConversionMemberMatcher
    {
        private int writeDepth;
        public bool TryGetMemberType(string name, out Type memberType) => MatchDelegatingXmlMember(name, out memberType);
        public override NullableDelegatingXmlValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<NullableDelegatingXmlValue?>(ref reader, options);
        public override void Write(Utf8JsonWriter writer, NullableDelegatingXmlValue? value, JsonSerializerOptions options)
        {
            if (writeDepth > 0) throw new JsonException("xml-property-converter-recursion: nullable property delegation selected itself.");
            writeDepth++;
            try { JsonSerializer.Serialize(writer, value, options); }
            finally { writeDepth--; }
        }
    }

    private sealed record XmlFormattingNestedContract
    {
        public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();
        public decimal Number { get; init; }
        public long Whole { get; init; }
    }

    private sealed record CustomerContract
    {
        public required string Name { get; init; }
        public long LoyaltyPoints { get; init; }
        public AddressContract? Address { get; init; }
        public IReadOnlyList<OrderContract> Orders { get; init; } = [];
        public int? Age { get; init; }
    }

    private sealed record AddressContract
    {
        public required string City { get; init; }
    }

    private sealed record OrderContract
    {
        public required string Id { get; init; }
        public decimal Total { get; init; }
    }
}
