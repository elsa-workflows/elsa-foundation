using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ValueConversionPlanResolverTests
{
    private readonly ValueConversionPlanResolver resolver = new();

    [Fact]
    public void Default_representations_distinguish_text_binary_structured_and_typed_values()
    {
        Assert.Equal(ValueRepresentation.TextValue, ValueRepresentationDefaults.Infer(Type("String")));
        Assert.Equal(ValueRepresentation.BinaryContent, ValueRepresentationDefaults.Infer(Type("Byte", CollectionKind.Array)));
        Assert.Equal(ValueRepresentation.StructuredValue, ValueRepresentationDefaults.Infer(Type("Customer", CollectionKind.List)));
        Assert.Equal(ValueRepresentation.TypedValue, ValueRepresentationDefaults.Infer(Type("Customer")));
    }

    [Fact]
    public void Auto_pins_identity_nullable_numeric_collection_and_canonical_any_operations()
    {
        Assert.Equal(ValueConversionOperation.Identity, resolver.Resolve(Type("String"), ValueRepresentation.TextValue, Type("String")).Operation);
        Assert.Equal(ValueConversionOperation.NullableCompatibility, resolver.Resolve(Type("Int32"), ValueRepresentation.TypedValue, Type("Int32?")).Operation);
        Assert.Equal(ValueConversionOperation.NumericWidening, resolver.Resolve(Type("UInt32"), ValueRepresentation.TypedValue, Type("Int64")).Operation);
        Assert.Equal(ValueConversionOperation.RecursiveCollection, resolver.Resolve(Type("UInt16", CollectionKind.List), ValueRepresentation.TypedValue, Type("Int32", CollectionKind.List)).Operation);
        Assert.Equal(ValueConversionOperation.RecursiveCollection, resolver.Resolve(Type("UInt16", CollectionKind.Array), ValueRepresentation.TypedValue, Type("Int32", CollectionKind.List)).Operation);
        Assert.Equal(ValueConversionOperation.CanonicalAny, resolver.Resolve(Type("Customer"), ValueRepresentation.TypedValue, Type("Elsa.Any")).Operation);
    }

    [Theory]
    [InlineData("Int64", "Int32")]
    [InlineData("Double", "Decimal")]
    [InlineData("UInt64", "Int64")]
    [InlineData("Int32?", "Int64")]
    public void Auto_rejects_lossy_numeric_conversions_with_complete_contract_diagnostics(string sourceAlias, string targetAlias)
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            resolver.Resolve(Type(sourceAlias), ValueRepresentation.TypedValue, Type(targetAlias)));

        Assert.Equal("VF-COER-001", exception.Message[..11]);
        Assert.Contains(sourceAlias, exception.Message, StringComparison.Ordinal);
        Assert.Contains(targetAlias, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ValueRepresentation.TypedValue), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ValueConversionMode.Auto), exception.Message, StringComparison.Ordinal);
        Assert.Contains("lossy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_profiles_require_a_declared_formatted_source_and_an_available_pinned_version()
    {
        var rawText = Assert.Throws<ValueConversionPublicationException>(() =>
            resolver.Resolve(Type("String"), ValueRepresentation.TextValue, Type("Customer"), ValueConversionMode.Json));
        var unknownProfile = Assert.Throws<ValueConversionPublicationException>(() =>
            resolver.Resolve(
                Type("String"),
                ValueRepresentation.FormattedContent,
                Type("Customer"),
                ValueConversionMode.Profile,
                new ValueConversionProfileReference("partner.json", "8")));

        Assert.Contains("not format-sniffed", rawText.Message, StringComparison.Ordinal);
        Assert.Contains("not available", unknownProfile.Message, StringComparison.Ordinal);
        Assert.Equal("elsa.json", resolver.Resolve(
            Type("String"), ValueRepresentation.FormattedContent, Type("Customer"), ValueConversionMode.Json).Profile!.Id);
    }

    [Fact]
    public void Transient_values_cannot_be_pinned_across_a_durable_binding()
    {
        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            resolver.Resolve(Type("Stream"), ValueRepresentation.TransientResource, Type("Elsa.Any")));

        Assert.Contains("transient resources", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_capture_resolves_against_the_declared_variable_target_and_input_compiler_leaves_unknown_sources_unplanned()
    {
        var outputCompiler = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]), resolver);
        var captures = outputCompiler.CompileBoundaryOutputs(
            "writer",
            [new ActivityOutputContract("result", "Result", new TypeReference("UInt32"), true, "elsa.json")],
            [new ArgumentState("result", new ArgumentValue("total", "Variable"), null, null, null, null)],
            [new VariableDefinition("total", "Total", new TypeReference("Int64"), null, null)]);

        var capture = Assert.Single(captures).Value;
        Assert.Equal("Int64", capture.Type.Kind);
        Assert.Equal("elsa.json", capture.Type.Id);
        Assert.Equal(ValueConversionOperation.NumericWidening, capture.ConversionPlan!.Operation);

        var input = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create(), resolver).Compile(
            "reader",
            Input("String"),
            new ArgumentValue("total", "Variable"));

        Assert.Null(input.ConversionPlan);
    }

    [Fact]
    public void Conversion_fingerprints_are_behavioral_but_missing_legacy_plans_keep_the_legacy_shape()
    {
        var plan = resolver.Resolve(Type("Int32"), ValueRepresentation.TypedValue, Type("Int64"));
        var withPlan = Binding(plan);
        var differentPlan = Binding(resolver.Resolve(Type("Int16"), ValueRepresentation.TypedValue, Type("Int64")));
        var legacy = Binding(null);
        var alsoLegacy = Binding(null);
        var hasher = new WorkflowExecutableHasher();

        Assert.NotEqual(hasher.ComputeHash(Node(withPlan)), hasher.ComputeHash(Node(differentPlan)));
        Assert.Equal(hasher.ComputeHash(Node(legacy)), hasher.ComputeHash(Node(alsoLegacy)));
    }

    private static ValueTypeDescriptor Type(string alias, CollectionKind kind = CollectionKind.Single) => new(alias, kind);

    private static InputDefinition Input(string alias) => new(
        "value", "Value", new TypeReference(alias), "elsa.json", "Value", null);

    private static RuntimeInputBinding Binding(ValueConversionPlan? plan) => new(
        "value",
        Type("Int64"),
        ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.Literal,
        ValueEnvelope.Inline(Type("Int64"), System.Text.Json.JsonSerializer.SerializeToElement(1), ValueProtectionPolicy.InstanceInline),
        conversionPlan: plan);

    private static ExecutableNode Node(RuntimeInputBinding binding) => new(
        "node", "node", "test", "1",
        new RuntimeActivityDescriptor("test", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding> { ["value"] = binding },
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());
}
