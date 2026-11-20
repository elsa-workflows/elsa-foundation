using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class BindingConversionPlanContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ValueTypeDescriptor StringType = new("System.String");
    private static readonly ValueTypeDescriptor AnyType = new("Elsa.Any");

    [Fact]
    public void Auto_mode_pins_an_identity_plan()
    {
        var plan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue);

        Assert.Equal(ValueConversionMode.Auto, plan.Mode);
        Assert.Equal(ValueConversionOperation.Identity, plan.Operation);
        Assert.Null(plan.Profile);
    }

    [Fact]
    public void None_mode_pins_an_identity_plan()
    {
        var plan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue, ValueConversionMode.None);

        Assert.Equal(ValueConversionMode.None, plan.Mode);
        Assert.Equal(ValueConversionOperation.Identity, plan.Operation);
        Assert.Null(plan.Profile);
    }

    [Fact]
    public void Json_mode_pins_a_versioned_profile()
    {
        var plan = NewProfilePlan(ValueConversionMode.Json, "elsa.json", "1");

        Assert.Equal(ValueConversionMode.Json, plan.Mode);
        Assert.Equal(ValueConversionOperation.Profile, plan.Operation);
        Assert.Equal("elsa.json", plan.Profile!.Id);
    }

    [Fact]
    public void Xml_mode_pins_a_versioned_profile()
    {
        var plan = NewProfilePlan(ValueConversionMode.Xml, "elsa.xml", "1");

        Assert.Equal(ValueConversionMode.Xml, plan.Mode);
        Assert.Equal(ValueConversionOperation.Profile, plan.Operation);
        Assert.Equal("elsa.xml", plan.Profile!.Id);
    }

    [Fact]
    public void Named_profile_mode_pins_the_requested_profile()
    {
        var plan = NewProfilePlan(ValueConversionMode.Profile, "contoso.customer", "2026-07");

        Assert.Equal(ValueConversionMode.Profile, plan.Mode);
        Assert.Equal(ValueConversionOperation.Profile, plan.Operation);
        Assert.Equal("contoso.customer", plan.Profile!.Id);
        Assert.Equal("2026-07", plan.Profile.Version);
    }

    [Fact]
    public void Fingerprint_is_deterministic_for_semantically_equivalent_json()
    {
        var first = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            TypeWithSchema("""{"b":2,"a":{"z":true,"y":false}}"""),
            AnyType,
            ValueConversionMode.Json,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            ValueConversionLimits.Default,
            JsonDocument.Parse("""{"beta":[{"b":2,"a":1}],"alpha":"value"}""").RootElement);
        var second = new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            TypeWithSchema("""{"a":{"y":false,"z":true},"b":2}"""),
            AnyType,
            ValueConversionMode.Json,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference("elsa.json", "1"),
            ValueConversionLimits.Default,
            JsonDocument.Parse("""{"alpha":"value","beta":[{"a":1,"b":2}]}""").RootElement);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Invalid_plan_combinations_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.TextValue,
            StringType,
            AnyType,
            ValueConversionMode.None,
            ValueConversionOperation.CanonicalAny,
            profile: null,
            limits: null,
            options: null));
        Assert.Throws<ArgumentException>(() => new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            StringType,
            AnyType,
            ValueConversionMode.Json,
            ValueConversionOperation.Identity,
            profile: null,
            limits: null,
            options: null));
        Assert.Throws<ArgumentException>(() => new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            StringType,
            AnyType,
            ValueConversionMode.Profile,
            ValueConversionOperation.Profile,
            profile: null,
            limits: null,
            options: null));
        Assert.Throws<ArgumentException>(() => new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.TextValue,
            StringType,
            StringType,
            ValueConversionMode.Auto,
            ValueConversionOperation.Identity,
            new ValueConversionProfileReference("elsa.json", "1"),
            limits: null,
            options: null));
        Assert.Throws<ArgumentException>(() => new ValueConversionPlan(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.TextValue,
            StringType,
            StringType,
            ValueConversionMode.Auto,
            ValueConversionOperation.Identity,
            profile: null,
            limits: null,
            JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void Plan_round_trips_through_json_with_its_pinned_fingerprint()
    {
        var plan = NewProfilePlan(ValueConversionMode.Json, "elsa.json", "1");

        var restored = JsonSerializer.Deserialize<ValueConversionPlan>(JsonSerializer.Serialize(plan, SerializerOptions), SerializerOptions)!;

        Assert.Equal(plan.Fingerprint, restored.Fingerprint);
        Assert.Equal(plan.SourceType, restored.SourceType);
        Assert.Equal(plan.TargetType, restored.TargetType);
        Assert.Equal(plan.Profile, restored.Profile);
        Assert.Equal(plan.Options!.Value.GetRawText(), restored.Options!.Value.GetRawText());
    }

    [Fact]
    public void Legacy_binding_json_without_a_plan_preserves_the_legacy_path()
    {
        var input = new RuntimeInputBinding(
            "message",
            StringType,
            ValueProtectionPolicy.Transient,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.Transient));
        var capture = new RuntimeOutputCapture(
            "result",
            "result",
            new RuntimeValueTypeDescriptor("String", null, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            captureOnSuccessfulCompletion: true);

        var legacyInputJson = JsonNode.Parse(JsonSerializer.Serialize(input, SerializerOptions))!.AsObject();
        var legacyCaptureJson = JsonNode.Parse(JsonSerializer.Serialize(capture, SerializerOptions))!.AsObject();
        legacyInputJson.Remove("conversionPlan");
        legacyCaptureJson.Remove("conversionPlan");

        var restoredInput = JsonSerializer.Deserialize<RuntimeInputBinding>(legacyInputJson, SerializerOptions)!;
        var restoredCapture = JsonSerializer.Deserialize<RuntimeOutputCapture>(legacyCaptureJson, SerializerOptions)!;

        Assert.Null(restoredInput.ConversionPlan);
        Assert.Null(restoredCapture.ConversionPlan);
    }

    private static ValueConversionPlan NewProfilePlan(ValueConversionMode mode, string id, string version) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            ValueRepresentation.FormattedContent,
            StringType,
            AnyType,
            mode,
            ValueConversionOperation.Profile,
            new ValueConversionProfileReference(id, version),
            ValueConversionLimits.Default,
            JsonSerializer.SerializeToElement(new { strict = true }));

    private static ValueTypeDescriptor TypeWithSchema(string schema) =>
        new("Contoso.Customer", schema: JsonDocument.Parse(schema).RootElement, schemaVersion: 1);
}
