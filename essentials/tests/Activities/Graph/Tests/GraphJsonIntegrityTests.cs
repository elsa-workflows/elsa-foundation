using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Activities.Graph.Tests;

public sealed partial class GraphActivityProviderTests
{
    private const string CarriedGraphJson = """{ "escaped" : "\u0061\/\u003c", "number" : 12345678901234567890123456789012345, "fraction" : 1.0, "nested" : [ null, { "CaseKey" : "v" } ] }""";

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Compilation_preserves_raw_nested_literals_and_defaults_in_stored_payloads(string schema)
    {
        var contract = RawGraphContract();
        var manifest = RawGraphManifest(schema);
        var provider = new GraphActivityProvider(new TestActivityStructureService(),
            new ActivityContractAuthoringValidator(new RawGraphCapabilityCatalog()));

        var compilation = await provider.CompileAsync(new("definition", "type", "draft", 1, "1.0.0", contract,
            manifest, [CreateDependency("graph-root", "activity-ver-root", "graph-root", contract)], "fingerprint"));

        Assert.Empty(compilation.Diagnostics);
        var payload = Assert.IsType<ExecutableNode>(compilation.ExecutableRoot).Descriptor.Payload;
        Assert.Equal(CarriedGraphJson, payload.GetProperty("occurrences")[0].GetProperty("inputBindings")[0].GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("variables")[0].GetProperty("initialValue").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("outputMappings")[0].GetProperty("source").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("inputs")[0].GetProperty("default").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, compilation.ExecutableRoot!.ActivityContract!.Inputs["payload"].DefaultValue!.Value.GetRawText());
        Assert.Equal(CarriedGraphJson, Assert.Single(compilation.Occurrences).InputBindings[0].GetProperty("value").GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task Compiling_scoped_variables_preserves_the_compilers_raw_initial_binding_and_structure_siblings()
    {
        var variables = JsonSerializer.SerializeToElement(new[] { RawGraphDeclaration() }, JsonBaseline.Options);
        using var services = new ServiceCollection()
            .AddSingleton<IActivityTemplateInputBindingCompiler>(new RawGraphVariableCompiler(variables))
            .BuildServiceProvider();
        var structures = new TestActivityStructureService
        {
            ScopedVariables = new Dictionary<string, IReadOnlyCollection<VariableDefinition>>
            {
                ["graph-root"] = [new("local", "Local", new("Object"), null, new ArgumentValue(ParseJson(CarriedGraphJson), "Literal"))]
            }
        };
        var provider = new GraphActivityProvider(structures,
            new ActivityContractAuthoringValidator(new RawGraphCapabilityCatalog()), services.GetRequiredService<IServiceScopeFactory>());
        var manifest = RawGraphManifest("1", $$$"""{"kind":"opaque","schemaVersion":"1","payload":{"carried":{{{CarriedGraphJson}}}}}""");

        var compilation = await provider.CompileAsync(new("definition", "type", "draft", 1, "1.0.0", RawGraphContract(),
            manifest, [CreateDependency("graph-root", "activity-ver-root", "graph-root", RawGraphContract())], "fingerprint"));

        Assert.Empty(compilation.Diagnostics);
        var payload = Assert.Single(compilation.Occurrences).Structure!.Payload;
        Assert.Equal(CarriedGraphJson, payload.GetProperty("carried").GetRawText());
        Assert.Equal(variables.GetRawText(), payload.GetProperty("variables").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("variables")[0].GetProperty("initialBinding").GetProperty("literal").GetProperty("inlineValue").GetRawText());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Manifest_migration_preserves_carried_json_without_canonicalizing_stored_data(string targetSchema)
    {
        var source = RawGraphManifest("1");

        var migrated = await _provider.MigrateAsync(new(source, targetSchema));

        Assert.Empty(migrated.Diagnostics);
        AssertRawGraphManifest(Assert.IsType<ActivityProviderManifest>(migrated.Manifest).Payload);
    }

    [Fact]
    public async Task Exact_reference_rewrite_preserves_other_literal_and_default_payloads()
    {
        var source = RawGraphManifest("1");

        var rewritten = await new GraphActivityProviderReferenceRewriter(new TestActivityStructureService())
            .RewriteReferencesAsync(source, [new("graph-root", "activity-ver-root", "new-version")]);

        Assert.Equal("new-version", rewritten.Payload.GetProperty("rootActivity").GetProperty("activityVersionId").GetString());
        AssertRawGraphManifest(rewritten.Payload);
    }

    [Fact]
    public void Explicit_manifest_canonical_output_uses_the_exact_shared_number_contract()
    {
        var source = RawGraphManifest("1");
        Assert.True(ActivityGraphManifest.TryParse(source.Payload, out var manifest, out var errors));
        Assert.Empty(errors);

        var canonical = manifest!.ToCanonicalJsonElement();

        Assert.Equal(JsonHashInput.Hash(source.Payload), JsonHashInput.Hash(canonical));
        Assert.Equal(JsonHashInput.CanonicalBytes(source.Payload), manifest.ToCanonicalUtf8Bytes());
        Assert.Equal("12345678901234567890123456789012345e0", canonical.GetProperty("rootActivity").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetProperty("number").GetRawText());
        Assert.NotEqual(JsonHashInput.Hash(canonical), JsonHashInput.HashJson(source.Payload.GetRawText()
            .Replace("12345678901234567890123456789012345", "12345678901234567890123456789012346", StringComparison.Ordinal)));
    }

    private static ActivityProviderManifest RawGraphManifest(string schema, string structure = "null") => new(
        GraphActivityProvider.Key, schema, ParseJson($$$"""
        {"rootActivity":{"nodeId":"graph-root","activityVersionId":"activity-ver-root","inputs":[{"referenceKey":"payload","value":{"expressionType":"Literal","value":{{{CarriedGraphJson}}}}}],"outputs":[],"structure":{{{structure}}}},
         "variables":[{"referenceKey":"state","name":"State","type":{"alias":"Object","collectionKind":"Single"},"storageDriverKey":"elsa.json","initialValue":{"syntax":"Literal","value":{{{CarriedGraphJson}}}}}],
         "outputMappings":[{"outputReferenceKey":"result","source":{"syntax":"Literal","value":{{{CarriedGraphJson}}}}}]{{{(schema == "2" ? ",\"outcomeMappings\":[{\"sourceOutcomeReferenceKey\":\"done\",\"boundaryOutcomeReferenceKey\":\"done\"}]" : "")}}}}
        """));

    private static DesignActivityContract RawGraphContract() => new("1",
        [new("payload", "Payload", new("Object"), false, true, new("Literal", ParseJson(CarriedGraphJson)), "elsa.json")],
        [new("result", "Result", new("Object"), true, true, "elsa.json")], [new("done", "Done", true)]);

    private static void AssertRawGraphManifest(JsonElement payload)
    {
        Assert.Equal(CarriedGraphJson, payload.GetProperty("rootActivity").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("variables")[0].GetProperty("initialValue").GetProperty("value").GetRawText());
        Assert.Equal(CarriedGraphJson, payload.GetProperty("outputMappings")[0].GetProperty("source").GetProperty("value").GetRawText());
    }

    private static RuntimeVariableDeclaration RawGraphDeclaration()
    {
        var type = new ValueTypeDescriptor("Object");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new("local", "Local", type, policy, new("local", type, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, ParseJson(CarriedGraphJson), policy)));
    }

    private sealed class RawGraphCapabilityCatalog : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; } =
            [new("Object", "Object", "Test", "Test", Enum.GetValues<CollectionKind>().ToHashSet(), true, true, new HashSet<string> { "elsa.json" })];
    }

    private sealed class RawGraphVariableCompiler(JsonElement variables) : IActivityTemplateInputBindingCompiler
    {
        public JsonElement CompileGraphVariables(string nodeId, IEnumerable<VariableDefinition> declarations) => variables;
        public IReadOnlyDictionary<string, RuntimeInputBinding> CompileGraphInputs(string nodeId, IEnumerable<InputDefinition> definitions, IEnumerable<ArgumentState> states) => throw new NotSupportedException();
        public IReadOnlyDictionary<string, RuntimeInputBinding> CompileGraphIntrinsicInputs(string nodeId, IEnumerable<InputDefinition> definitions, IEnumerable<ArgumentState> states) => throw new NotSupportedException();
        public RuntimeInputBinding PinAssignmentSourceConversion(string nodeId, RuntimeInputBinding binding, TypeReference? sourceType) => throw new NotSupportedException();
    }
}
