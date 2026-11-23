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
            "Elsa.Etl.Design.Api.EtlDesignJsonContext|Elsa.Etl.Core.EtlPipelineV2|Elsa.Etl.Design.Api.EtlPipelineV2JsonConverter"
        }, snapshot.OpaqueConverters);
    }

    [Theory]
    [InlineData("Elsa.Expressions.Core.Models.ArgumentValue", "Value")]
    [InlineData("Elsa.Activities.Design.Core.Models.ActivityArgumentValue", "Value")]
    [InlineData("Elsa.Workflows.Runtime.Core.Models.ValueEnvelope", "InlineValue")]
    [InlineData("Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommand", "Payload")]
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
