using Elsa.Workflows.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
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
        Assert.Equal(ValueConversionOperation.NumericWidening, resolver.Resolve(Type("System.UInt32"), ValueRepresentation.TypedValue, Type("System.Int64")).Operation);
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
    public void Explicit_json_profile_requires_declared_formatted_content_and_pins_the_builtin_version()
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
        var explicitJson = resolver.Resolve(Type("String"), ValueRepresentation.FormattedContent, Type("Elsa.Any"), ValueConversionMode.Json);
        var autoJson = resolver.Resolve(Type("String"), ValueRepresentation.FormattedContent, Type("JsonObject"));

        Assert.Contains("not format-sniffed", rawText.Message, StringComparison.Ordinal);
        Assert.Contains("not available", unknownProfile.Message, StringComparison.Ordinal);
        Assert.Equal(ValueConversionOperation.Profile, explicitJson.Operation);
        Assert.Equal("elsa.json", explicitJson.Profile!.Id);
        Assert.Equal(ValueConversionMode.Json, explicitJson.Mode);
        Assert.Equal(ValueConversionOperation.Profile, autoJson.Operation);
        Assert.Equal("JsonObject", autoJson.TargetType.Alias);
    }

    [Fact]
    public void Built_in_profile_registry_lists_profiles_for_authoring_surfaces()
    {
        var profiles = BuiltInValueConversionProfileRegistry.Instance.List();

        Assert.Equal(["elsa.json", "elsa.xml"], profiles.Select(profile => profile.Profile.Id));
        Assert.All(profiles, profile => Assert.Equal("1", profile.Profile.Version));
    }

    [Fact]
    public void Json_profile_pins_registered_typed_aliases_from_formatted_and_structured_sources()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerContract), "Acme.Customer");
        var typedResolver = new ValueConversionPlanResolver(wellKnownTypeRegistry: registry);

        var formatted = typedResolver.Resolve(Type("String"), ValueRepresentation.FormattedContent, Type("Acme.Customer"));
        var structured = typedResolver.Resolve(
            Type("JsonNode"),
            ValueRepresentation.StructuredValue,
            Type("Acme.Customer", CollectionKind.List));
        var unregistered = Assert.Throws<ValueConversionPublicationException>(() =>
            typedResolver.Resolve(Type("String"), ValueRepresentation.FormattedContent, Type("Acme.Unknown")));

        Assert.Equal(ValueConversionOperation.Profile, formatted.Operation);
        Assert.Equal("elsa.json", formatted.Profile!.Id);
        Assert.Equal(ValueConversionOperation.Profile, structured.Operation);
        Assert.Equal(CollectionKind.List, structured.TargetType.CollectionKind);
        Assert.Contains("registered typed target alias", unregistered.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_profile_pins_registered_typed_aliases_and_rejects_any_projection()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerContract), "Acme.Customer");
        var typedResolver = new ValueConversionPlanResolver(wellKnownTypeRegistry: registry);

        var explicitXml = typedResolver.Resolve(
            Type("String"),
            ValueRepresentation.FormattedContent,
            Type("Acme.Customer"),
            ValueConversionMode.Xml);
        var namedProfile = typedResolver.Resolve(
            Type("String"),
            ValueRepresentation.FormattedContent,
            Type("Acme.Customer"),
            ValueConversionMode.Profile,
            new ValueConversionProfileReference("elsa.xml", "1"));
        var anyProjection = Assert.Throws<ValueConversionPublicationException>(() =>
            typedResolver.Resolve(
                Type("String"),
                ValueRepresentation.FormattedContent,
                Type("Elsa.Any"),
                ValueConversionMode.Xml));

        Assert.Equal(ValueConversionOperation.Profile, explicitXml.Operation);
        Assert.Equal("elsa.xml", explicitXml.Profile!.Id);
        Assert.Equal("1", explicitXml.Profile.Version);
        Assert.Equal(ValueConversionMode.Xml, explicitXml.Mode);
        Assert.Equal("elsa.xml", namedProfile.Profile!.Id);
        Assert.Equal(ValueConversionMode.Profile, namedProfile.Mode);
        Assert.Contains("XML has no universal canonical Any projection", anyProjection.Message, StringComparison.Ordinal);
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
            [new ActivityOutputContract(
                "result",
                "Result",
                new TypeReference("UInt32"),
                IsRequired: true,
                IsNullable: false,
                StorageDriverKey: "elsa.json")],
            [new ArgumentState(
                "result",
                new ArgumentValue("total", "Variable"),
                null,
                null,
                null,
                null,
                new AuthoredValueConversionRequest())],
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
    public void Authored_literal_json_conversion_pins_formatted_content_source_plan()
    {
        var input = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create(), resolver).Compile(
            "reader",
            Input("Elsa.Any"),
            new ArgumentState(
                "value",
                new ArgumentValue("""{"name":"Grace"}""", "Literal"),
                null,
                null,
                null,
                null,
                new AuthoredValueConversionRequest(AuthoredValueConversionMode.Json)));

        Assert.Equal(RuntimeInputBindingSource.Literal, input.Source);
        Assert.Equal("String", input.ConversionPlan!.SourceType.Alias);
        Assert.Equal(ValueRepresentation.FormattedContent, input.ConversionPlan.SourceRepresentation);
        Assert.Equal(ValueConversionMode.Json, input.ConversionPlan.Mode);
        Assert.Equal("elsa.json", input.ConversionPlan.Profile!.Id);
        Assert.Equal("""{"name":"Grace"}""", input.Literal!.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Output_capture_honors_explicit_authored_conversion_mode_and_profile()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerContract), "Acme.Customer");
        var outputCompiler = new RuntimeOutputCaptureCompiler(
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]),
            new ValueConversionPlanResolver(wellKnownTypeRegistry: registry));

        var captures = outputCompiler.CompileBoundaryOutputs(
            "writer",
            [new ActivityOutputContract(
                "result",
                "Result",
                new TypeReference("String"),
                IsRequired: true,
                IsNullable: false,
                StorageDriverKey: "elsa.json",
                SourceRepresentation: ValueRepresentation.FormattedContent)],
            [new ArgumentState(
                "result",
                new ArgumentValue("customer", "Variable"),
                null,
                null,
                null,
                null,
                new AuthoredValueConversionRequest(AuthoredValueConversionMode.Xml))],
            [new VariableDefinition("customer", "Customer", new TypeReference("Acme.Customer"), null, null)]);

        var capture = Assert.Single(captures).Value;
        Assert.Equal(ValueConversionMode.Xml, capture.ConversionPlan!.Mode);
        Assert.Equal("elsa.xml", capture.ConversionPlan.Profile!.Id);
        Assert.Equal("Acme.Customer", capture.ConversionPlan.TargetType.Alias);
    }

    [Fact]
    public void Output_capture_rejects_transient_resources_before_durable_variable_publication()
    {
        var outputCompiler = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]), resolver);

        var exception = Assert.Throws<ArgumentException>(() => outputCompiler.CompileBoundaryOutputs(
            "reader",
            [new ActivityOutputContract(
                "stream",
                "Stream",
                new TypeReference("Stream"),
                IsRequired: true,
                IsNullable: false,
                StorageDriverKey: "elsa.json")],
            [new ArgumentState("stream", new ArgumentValue("payload", "Variable"), null, null, null, null)],
            [new VariableDefinition("payload", "Payload", new TypeReference("Stream"), null, null)]));

        Assert.Contains("VF-ACT-005", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TransientResource", exception.Message, StringComparison.Ordinal);
        Assert.Contains("destination storage policy 'Custom'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("execution-local activity-result binding", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DurableReference/resource-handle", exception.Message, StringComparison.Ordinal);
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
        "value",
        "Value",
        new TypeReference(alias),
        StorageDriverType: "elsa.json",
        DisplayName: "Value",
        Category: null,
        IsNullable: false);

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

    private sealed record CustomerContract(string Name);
}
