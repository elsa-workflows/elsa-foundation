using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ValueConversionPlanTests
{
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
    public void Canonical_any_projection_returns_json_not_an_arbitrary_clr_identity()
    {
        var sourceType = new ValueTypeDescriptor("Acme.Customer");
        var targetType = new ValueTypeDescriptor("Elsa.Any");
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
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"));
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
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"));
        var converted = new RuntimeValueConversionExecutor().Convert(
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("[{\"id\":2},{\"id\":3}]"),
                ValueProtectionPolicy.InstanceInline),
            plan);

        Assert.Equal(2, converted.InlineValue!.Value.EnumerateArray().First().GetProperty("id").GetInt32());
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
        var plan = JsonPlan(new ValueTypeDescriptor("Elsa.Any"), ValueConversionMode.Json);

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
        var targetType = new ValueTypeDescriptor("Elsa.Any");
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
        var smallPayload = JsonPlan(new ValueTypeDescriptor("Elsa.Any"), limits: new ValueConversionLimits(8, 10, 4));
        var shallow = JsonPlan(new ValueTypeDescriptor("Elsa.Any"), limits: new ValueConversionLimits(1, 10, 1_024));
        var fewNodes = JsonPlan(new ValueTypeDescriptor("Elsa.Any"), limits: new ValueConversionLimits(8, 2, 1_024));
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
            [new ActivityInputContract("count", "Count", targetType, true, false, null, ActivityValuePolicy.Default)],
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
        ValueConversionLimits? limits = null) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("String"),
            targetType,
            mode,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: limits ?? ValueConversionLimits.Default,
            options: null);
}
