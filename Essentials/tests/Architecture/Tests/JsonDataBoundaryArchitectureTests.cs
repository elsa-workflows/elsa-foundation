using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonDataBoundaryArchitectureTests
{
    [Fact]
    public void Every_shipping_generated_context_is_loaded_and_every_declared_wire_root_can_be_described()
    {
        var contexts = JsonBoundaryContractGraph.Contexts(RuntimeLibraries.Assemblies);
        var sourcePaths = ModuleRoots.Resolve(RepoPaths.RepoRoot, [.. ModuleRoots.Production, "Studio/src", "Extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(path) && ModuleRoots.IsNotTestFile(path)).Distinct().ToArray();
        var declared = JsonBoundaryContractGraph.DeclaredContexts(sourcePaths.Select(File.ReadAllText));
        var loaded = contexts.Select(context => context.GetType().FullName!).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(declared);
        Assert.Equal(declared.Order(StringComparer.Ordinal), loaded.Order(StringComparer.Ordinal));
        var snapshot = JsonBoundaryContractGraph.Inspect(contexts);
        Assert.True(snapshot.Errors.Count == 0, "Actual wire contract metadata is incomplete:\n" + string.Join('\n', snapshot.Errors));
        Assert.True(snapshot.Pins.Keys.Count(key => key.StartsWith("root|", StringComparison.Ordinal)) >= 600,
            "The complete declared boundary inventory must be inspected.");
    }

    [Fact]
    public void Reviewed_wire_shapes_do_not_silently_gain_a_member_root_converter_or_polymorphic_alternative()
    {
        var snapshot = JsonBoundaryContractGraph.Inspect(JsonBoundaryContractGraph.Contexts(RuntimeLibraries.Assemblies));
        Assert.Empty(snapshot.Errors);
        var path = Path.Join(RepoPaths.RepoRoot, "Essentials/tests/Architecture/Tests/Baselines/json-wire-contracts.json");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))!;
        var changes = snapshot.Pins.Keys.Union(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(key => !expected.TryGetValue(key, out var before) || !snapshot.Pins.TryGetValue(key, out var after) || before != after)
            .Select(key => key + " | actual=" + snapshot.Pins.GetValueOrDefault(key, "removed") + "\n" + snapshot.Shapes.GetValueOrDefault(key, "Root added or removed.")).ToArray();
        if (changes.Length != 0)
        {
            var evidence = Path.Join(Path.GetTempPath(), "elsa-json-wire-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Join(evidence, "actual-contracts.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            Assert.Fail("Review changed wire members against the data roles in docs/serialization.md before updating pins. Full actual graph: " + evidence + "\n" + string.Join('\n', changes));
        }
    }

    [Fact]
    public void Loose_members_and_opaque_converter_contracts_need_an_exact_member_level_review()
    {
        var snapshot = JsonBoundaryContractGraph.Inspect(JsonBoundaryContractGraph.Contexts(RuntimeLibraries.Assemblies));
        Assert.Empty(snapshot.Errors);
        Assert.Equal(new[]
        {
            "Elsa.Weaver.Api.WeaverSseJsonContext|Elsa.Weaver.Core.Models.WeaverStreamEvent.payload",
            "Elsa.Weaver.Api.WeaverSseJsonContext|Elsa.Weaver.Core.Models.WeaverToolCallOutcome.result",
            "Elsa.Workflows.Publishing.Api.WorkflowsPublishingJsonContext|Elsa.Workflows.Publishing.Api.Models.RuntimePreflightProblemDetails.diagnostics"
        }, snapshot.LooseMembers);
        Assert.Equal(new[]
        {
            "Elsa.Activities.Design.Api.ActivitiesDesignJsonContext|Elsa.Activities.Design.Api.Commands.ActivityLayoutRecordsPayload|Elsa.Activities.Design.Api.Commands.ActivityLayoutRecordsPayloadJsonConverter",
            "Elsa.Activities.Design.Api.ActivitiesDesignJsonContext|Elsa.Activities.Design.Api.Commands.ActivityNodePresentationPayload|Elsa.Activities.Design.Api.Commands.ActivityPresentationPayloadConverter",
            "Elsa.Etl.Design.Api.EtlDesignJsonContext|Elsa.Etl.Core.EtlPipelineV2|Elsa.Etl.Design.Api.EtlPipelineV2JsonConverter",
            "Elsa.Workflows.Runtime.Api.WorkflowsRuntimeJsonContext|Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceDetailsView.outputs|System.Collections.Generic.IReadOnlyDictionary`2[System.String,Elsa.Workflows.Runtime.Api.Models.WorkflowOutputView]|Elsa.Workflows.Runtime.Api.Models.WorkflowOutputsJsonConverter"
        }, snapshot.OpaqueConverters);
    }

    [Theory]
    [InlineData("Elsa.Expressions.Core.Models.ArgumentValue", "Value")]
    [InlineData("Elsa.Activities.Design.Core.Models.ActivityArgumentValue", "Value")]
    [InlineData("Elsa.Workflows.Runtime.Core.Models.ValueEnvelope", "InlineValue")]
    [InlineData("Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommand", "Payload")]
    [InlineData("Elsa.Workflows.Runtime.Api.Models.WorkflowOutputView", "Value")]
    [InlineData("Elsa.Weaver.Core.Models.WeaverContextAttachment", "Content")]
    public void Reviewed_data_members_remain_JSON_values(string owner, string member)
    {
        var type = Assert.Single(RuntimeLibraries.Assemblies.Select(assembly => assembly.GetType(owner)).OfType<Type>());
        var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance)!;
        Assert.NotNull(property);
        Assert.True(JsonBoundaryContractGraph.IsJsonData(property.PropertyType), owner + "." + member + " carries data and must remain JSON.");
    }

    [Fact]
    public void Proposal_operations_remain_a_sequence_of_JSON_values()
    {
        var property = typeof(Elsa.Weaver.Core.Models.WeaverActionProposal).GetProperty("Operations")!;
        Assert.Equal(typeof(IReadOnlyCollection<JsonElement>), property.PropertyType);
    }

    [Theory]
    [InlineData(typeof(object))]
    [InlineData(typeof(Dictionary<string, object>))]
    [InlineData(typeof(Dictionary<string, JsonElement>))]
    [InlineData(typeof(List<JsonElement>))]
    [InlineData(typeof(OwnedDocument))]
    public void A_data_member_cannot_be_waived_as_an_object_collection_dictionary_or_POCO(Type replacement) =>
        Assert.False(JsonBoundaryContractGraph.IsJsonData(replacement));

    [Theory]
    [InlineData("object")]
    [InlineData("nested-object")]
    [InlineData("POCO")]
    [InlineData("new-member")]
    [InlineData("removed-ignore")]
    [InlineData("converter")]
    [InlineData("derived-type")]
    [InlineData("new-root")]
    public void Wire_shape_drift_is_detected_even_when_the_boundary_root_name_does_not_change(string mutation)
    {
        var before = InspectFixture(JsonBaseline.Options, [typeof(OwnedDocument)]);
        var options = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(OwnedDocument))
            {
                if (mutation is "object" or "nested-object" or "POCO")
                {
                    info.Properties.Remove(info.Properties.Single(property => property.Name == "data"));
                    var replacement = mutation switch { "object" => typeof(object), "nested-object" => typeof(IReadOnlyList<object>), _ => typeof(OtherDocument) };
                    info.Properties.Add(info.CreateJsonPropertyInfo(replacement, "data"));
                }
                if (mutation == "new-member") info.Properties.Add(info.CreateJsonPropertyInfo(typeof(JsonElement), "newData"));
                if (mutation == "removed-ignore") info.Properties.Single(property => property.Name == "inProcess").Get = _ => new object();
            }
            if (info.Type == typeof(OwnedUnion) && mutation == "derived-type")
                info.PolymorphismOptions!.DerivedTypes.Add(new(typeof(SecondAlternative), "second"));
        });
        options.TypeInfoResolver = resolver;
        if (mutation == "converter") options.Converters.Add(new OtherDocumentConverter());
        options.MakeReadOnly();
        var after = InspectFixture(options, mutation == "new-root" ? [typeof(OwnedDocument), typeof(OtherDocument)] : [typeof(OwnedDocument)]);
        Assert.Empty(before.Errors);
        Assert.Empty(after.Errors);
        Assert.False(before.Pins.SequenceEqual(after.Pins), mutation + " must change the reviewed boundary graph.");
    }

    [Fact]
    public void Source_discovery_includes_aliases_qualified_attributes_nested_contexts_and_global_aliases()
    {
        var found = JsonBoundaryContractGraph.DeclaredContexts([
            "global using JS = System.Text.Json.Serialization.JsonSerializableAttribute;",
            "using Meta = System.Text.Json.Serialization; namespace A; [Meta.JsonSerializable(typeof(string))] partial class Context {}",
            "namespace B { class Outer { [JS(typeof(string))] partial class Context {} } }",
            "[global::System.Text.Json.Serialization.JsonSerializable(typeof(string))] partial class Context {}"
        ]);
        Assert.Equal(new[] { "A.Context", "B.Outer+Context", "Context" }, found.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(typeof(Rows))]
    [InlineData(typeof(RowsById))]
    public void A_changed_element_beneath_an_unchanged_non_generic_collection_changes_its_own_pin(Type collection)
    {
        var before = InspectFixture(JsonBaseline.Options, [collection]);
        var options = WithAddedMember(typeof(OtherDocument));
        var after = InspectFixture(options, [collection]);
        Assert.Empty(before.Errors);
        Assert.Empty(after.Errors);
        var root = "shape|fixture|" + JsonBoundaryContractGraph.Name(collection);
        var element = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(OtherDocument));
        Assert.Equal(before.Pins[root], after.Pins[root]);
        Assert.NotEqual(before.Pins[element], after.Pins[element]);
        Assert.True(JsonBoundaryContractGraph.ContainsObject(typeof(ObjectRows)));
    }

    [Fact]
    public void Both_nested_siblings_inside_a_generic_owner_have_distinct_pins()
    {
        var roots = new[] { typeof(GenericOwner<string>.First), typeof(GenericOwner<string>.Second) };
        var before = InspectFixture(JsonBaseline.Options, roots);
        var after = InspectFixture(WithAddedMember(roots[1]), roots);
        Assert.Empty(before.Errors);
        Assert.Empty(after.Errors);
        var first = "shape|fixture|" + JsonBoundaryContractGraph.Name(roots[0]);
        var second = "shape|fixture|" + JsonBoundaryContractGraph.Name(roots[1]);
        Assert.NotEqual(first, second);
        Assert.Equal(before.Pins[first], after.Pins[first]);
        Assert.NotEqual(before.Pins[second], after.Pins[second]);
    }

    [Fact]
    public void Array_rank_and_jagged_nesting_have_distinct_contract_identities()
    {
        Assert.Equal(3, new[] { typeof(int[]), typeof(int[,]), typeof(int[][]) }.Select(JsonBoundaryContractGraph.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Effective_enum_wire_names_are_pinned_even_with_the_same_CLR_enum_and_converter_type()
    {
        JsonSerializerOptions Options(JsonNamingPolicy naming)
        {
            var options = JsonBaseline.CreateCopy();
            options.Converters.Add(new JsonStringEnumConverter<WireState>(naming));
            options.MakeReadOnly();
            return options;
        }
        var left = Options(JsonNamingPolicy.CamelCase);
        var right = Options(JsonNamingPolicy.SnakeCaseLower);
        Assert.Equal(left.GetTypeInfo(typeof(WireState)).Converter.GetType(), right.GetTypeInfo(typeof(WireState)).Converter.GetType());
        var before = InspectFixture(left, [typeof(WireState)]);
        var after = InspectFixture(right, [typeof(WireState)]);
        var key = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(WireState));
        Assert.NotEqual(before.Pins[key], after.Pins[key]);
        using var shape = JsonDocument.Parse(before.Shapes[key]);
        Assert.Contains(shape.RootElement.GetProperty("EnumWire").EnumerateArray(), token => token.GetString() == "\"ready\"");
    }

    [Theory]
    [InlineData(typeof(System.Collections.ArrayList))]
    [InlineData(typeof(System.Collections.Hashtable))]
    public void Untyped_collections_expose_their_implicit_object_contract(Type collection)
    {
        Assert.True(JsonBoundaryContractGraph.ContainsObject(collection));
        var snapshot = InspectFixture(JsonBaseline.Options, [collection]);
        Assert.Empty(snapshot.Errors);
        Assert.Contains("shape|fixture|System.Object", snapshot.Pins.Keys);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Member_enum_converter_names_are_pinned_including_factories_and_nullable_members(bool expanded, bool nullable)
    {
        JsonSerializerOptions Options(JsonNamingPolicy naming)
        {
            var options = JsonBaseline.CreateCopy();
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                if (info.Type != typeof(EnumDocument)) return;
                var property = info.Properties.Single(property => property.Name == (nullable ? "optional" : "state"));
                JsonConverterFactory factory = nullable ? new NullableWireStateConverterFactory(naming) : new JsonStringEnumConverter<WireState>(naming);
                property.CustomConverter = expanded ? factory.CreateConverter(property.PropertyType, options) : factory;
            });
            options.TypeInfoResolver = resolver;
            options.MakeReadOnly();
            return options;
        }
        var left = Options(JsonNamingPolicy.CamelCase);
        var right = Options(JsonNamingPolicy.SnakeCaseLower);
        var before = InspectFixture(left, [typeof(EnumDocument)]);
        var after = InspectFixture(right, [typeof(EnumDocument)]);
        Assert.True(before.Errors.Count == 0, string.Join('\n', before.Errors));
        Assert.True(after.Errors.Count == 0, string.Join('\n', after.Errors));
        var key = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(EnumDocument));
        Assert.NotEqual(before.Pins[key], after.Pins[key]);
        var unchangedEnum = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(WireState));
        Assert.Equal(before.Pins[unchangedEnum], after.Pins[unchangedEnum]);
        var value = new EnumDocument { State = WireState.FirstChoice, Optional = WireState.FirstChoice };
        var member = nullable ? "optional" : "state";
        Assert.Equal("firstChoice", JsonSerializer.SerializeToElement(value, left).GetProperty(member).GetString());
        Assert.Equal("first_choice", JsonSerializer.SerializeToElement(value, right).GetProperty(member).GetString());
    }

    [Fact]
    public void An_owned_scalar_converter_does_not_expose_the_underlying_untyped_collection_as_a_wire_object_slot()
    {
        var snapshot = InspectFixture(JsonBaseline.Options, [typeof(ScalarDocument)]);
        Assert.Empty(snapshot.Errors);
        Assert.Empty(snapshot.LooseMembers);
        Assert.DoesNotContain("shape|fixture|System.Object", snapshot.Pins.Keys);
        Assert.Single(snapshot.OpaqueConverters);
    }

    [Fact]
    public void A_generic_scalar_converter_does_not_require_metadata_for_its_hidden_argument()
    {
        var options = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = new RefusingHiddenResolver(resolver);
        options.MakeReadOnly();
        var snapshot = InspectFixture(options, [typeof(ScalarBox<HiddenDocument>)]);
        Assert.Empty(snapshot.Errors);
        Assert.DoesNotContain("shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(HiddenDocument)), snapshot.Pins.Keys);
        Assert.Single(snapshot.OpaqueConverters);
    }

    [Fact]
    public void A_member_scalar_converter_is_an_exact_opaque_contract_and_hides_the_default_collection_graph()
    {
        var options = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(MemberScalarDocument)) info.Properties.Single(property => property.Name == "scalar").CustomConverter = new UntypedScalarConverter();
        });
        options.TypeInfoResolver = resolver;
        options.MakeReadOnly();
        var snapshot = InspectFixture(options, [typeof(MemberScalarDocument)]);
        Assert.Empty(snapshot.Errors);
        Assert.Empty(snapshot.LooseMembers);
        Assert.DoesNotContain("shape|fixture|System.Object", snapshot.Pins.Keys);
        var contract = Assert.Single(snapshot.OpaqueConverters);
        Assert.Contains("MemberScalarDocument.scalar|", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_enum_member_writer_observes_the_actual_owner_encoder_and_writer_settings()
    {
        var options = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        var converter = new UnicodeEnumConverter();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(EnumDocument)) info.Properties.Single(property => property.Name == "state").CustomConverter = converter;
        });
        options.TypeInfoResolver = resolver;
        options.MakeReadOnly();
        var snapshot = InspectFixture(options, [typeof(EnumDocument)]);
        Assert.Empty(snapshot.Errors);
        var key = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(EnumDocument));
        using var shape = JsonDocument.Parse(snapshot.Shapes[key]);
        var member = shape.RootElement.GetProperty("Properties").EnumerateArray().Single(property => property.GetProperty("Name").GetString() == "state");
        var actual = JsonSerializer.SerializeToElement(new EnumDocument { State = WireState.FirstChoice }, options).GetProperty("state").GetRawText();
        Assert.Equal("\"René <+>\"", actual);
        Assert.All(member.GetProperty("EnumWire").EnumerateArray(), token => Assert.Equal(actual, token.GetString()));
        var actualWriter = converter.Observed[^1];
        Assert.All(converter.Observed.SkipLast(1), sampled =>
        {
            Assert.Same(actualWriter.Encoder, sampled.Encoder);
            Assert.Equal(actualWriter.Indented, sampled.Indented);
            Assert.Equal(actualWriter.SkipValidation, sampled.SkipValidation);
            Assert.Equal(actualWriter.MaxDepth, sampled.MaxDepth);
            Assert.Equal(actualWriter.IndentCharacter, sampled.IndentCharacter);
            Assert.Equal(actualWriter.IndentSize, sampled.IndentSize);
            Assert.Equal(actualWriter.NewLine, sampled.NewLine);
        });
    }

    [Fact]
    public void An_ignored_enum_does_not_need_wire_metadata_but_removing_ignore_changes_the_graph()
    {
        var beforeOptions = JsonBaseline.CreateCopy();
        beforeOptions.TypeInfoResolver = new RefusingEnumResolver(new DefaultJsonTypeInfoResolver());
        beforeOptions.MakeReadOnly();
        var before = InspectFixture(beforeOptions, [typeof(IgnoredEnumDocument)]);
        Assert.Empty(before.Errors);
        var hidden = "shape|fixture|" + JsonBoundaryContractGraph.Name(typeof(HiddenEnum));
        Assert.DoesNotContain(hidden, before.Pins.Keys);
        var afterOptions = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(IgnoredEnumDocument)) return;
            var property = info.Properties.Single(property => property.Name == "hidden");
            property.Get = _ => HiddenEnum.Choice;
            property.ShouldSerialize = null;
        });
        afterOptions.TypeInfoResolver = resolver;
        afterOptions.MakeReadOnly();
        var after = InspectFixture(afterOptions, [typeof(IgnoredEnumDocument)]);
        Assert.Empty(after.Errors);
        Assert.Contains(hidden, after.Pins.Keys);
        Assert.False(before.Pins.SequenceEqual(after.Pins));
    }

    private static JsonSerializerOptions WithAddedMember(Type target)
    {
        var options = JsonBaseline.CreateCopy();
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != target) return;
            var property = info.CreateJsonPropertyInfo(typeof(string), "newMember");
            property.Get = _ => "added";
            info.Properties.Add(property);
        });
        options.TypeInfoResolver = resolver;
        options.MakeReadOnly();
        return options;
    }

    private static JsonBoundaryContractGraph.Snapshot InspectFixture(JsonSerializerOptions options, Type[] roots) =>
        JsonBoundaryContractGraph.Inspect(new[] { new JsonBoundaryContractGraph.Contracts("fixture", roots, options) });

    public sealed class OwnedDocument
    {
        public JsonElement Data { get; init; }
        public OwnedUnion? Alternative { get; init; }
        public OtherDocument? Metadata { get; init; }
        [JsonIgnore] public object? InProcess { get; init; }
    }
    public sealed class OtherDocument { public string Name { get; init; } = string.Empty; }
    public sealed class Rows : List<OtherDocument>;
    public sealed class RowsById : Dictionary<string, OtherDocument>;
    public sealed class ObjectRows : List<object>;
    public sealed class GenericOwner<T>
    {
        public sealed class First { public JsonElement Data { get; init; } }
        public sealed class Second { public JsonElement Data { get; init; } }
    }
    public enum WireState { FirstChoice, [JsonStringEnumMemberName("ready")] ReadyChoice }
    public sealed class EnumDocument { public WireState State { get; init; } public WireState? Optional { get; init; } }
    private sealed class NullableWireStateConverterFactory(JsonNamingPolicy naming) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(WireState?);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new NullableWireStateConverter((JsonConverter<WireState>)new JsonStringEnumConverter<WireState>(naming).CreateConverter(typeof(WireState), options));
    }
    private sealed class NullableWireStateConverter(JsonConverter<WireState> inner) : JsonConverter<WireState?>
    {
        public override WireState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : inner.Read(ref reader, typeof(WireState), options);
        public override void Write(Utf8JsonWriter writer, WireState? value, JsonSerializerOptions options)
        {
            if (value.HasValue) inner.Write(writer, value.Value, options);
            else writer.WriteNullValue();
        }
    }
    public sealed class ScalarDocument { public ScalarRows? Scalar { get; init; } }
    [JsonConverter(typeof(ScalarRowsConverter))]
    public sealed class ScalarRows : System.Collections.ArrayList;
    public sealed class ScalarRowsConverter : JsonConverter<ScalarRows>
    {
        public override ScalarRows Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new();
        public override void Write(Utf8JsonWriter writer, ScalarRows value, JsonSerializerOptions options) => writer.WriteStringValue("owned-scalar");
    }
    public sealed class HiddenDocument { public object? InProcess { get; init; } }
    public enum HiddenEnum { Choice }
    public sealed class IgnoredEnumDocument { [JsonIgnore] public HiddenEnum Hidden { get; init; } }
    private sealed class RefusingEnumResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(HiddenEnum)
            ? throw new NotSupportedException("The ignored enum has no wire metadata.") : inner.GetTypeInfo(type, options);
    }
    [JsonConverter(typeof(ScalarBoxConverterFactory))]
    public sealed class ScalarBox<T>;
    public sealed class ScalarBoxConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ScalarBox<>);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ScalarBoxConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }
    public sealed class ScalarBoxConverter<T> : JsonConverter<ScalarBox<T>>
    {
        public override ScalarBox<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new();
        public override void Write(Utf8JsonWriter writer, ScalarBox<T> value, JsonSerializerOptions options) => writer.WriteStringValue("owned-scalar");
    }
    private sealed class RefusingHiddenResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => type == typeof(HiddenDocument)
            ? throw new NotSupportedException("The hidden CLR argument has no wire metadata.") : inner.GetTypeInfo(type, options);
    }
    public sealed class MemberScalarDocument { public System.Collections.ArrayList? Scalar { get; init; } }
    public sealed class UntypedScalarConverter : JsonConverter<System.Collections.ArrayList>
    {
        public override System.Collections.ArrayList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new();
        public override void Write(Utf8JsonWriter writer, System.Collections.ArrayList value, JsonSerializerOptions options) => writer.WriteStringValue("owned-scalar");
    }
    public sealed class UnicodeEnumConverter : JsonConverter<WireState>
    {
        public List<JsonWriterOptions> Observed { get; } = [];
        public override WireState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => WireState.FirstChoice;
        public override void Write(Utf8JsonWriter writer, WireState value, JsonSerializerOptions options)
        { Observed.Add(writer.Options); writer.WriteStringValue("René <+>"); }
    }
    [JsonPolymorphic, JsonDerivedType(typeof(FirstAlternative), "first")]
    public abstract class OwnedUnion;
    public sealed class FirstAlternative : OwnedUnion { public JsonElement Data { get; init; } }
    public sealed class SecondAlternative : OwnedUnion { public JsonElement Data { get; init; } }
    private sealed class OtherDocumentConverter : JsonConverter<OtherDocument>
    {
        public override OtherDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, OtherDocument value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
