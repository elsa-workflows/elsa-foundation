using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed partial class GraphActivityProviderTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Data_analysis_projects_the_actual_provider_envelope_and_public_contract(string schema)
    {
        var payload = ParseJson("""
            {"rootActivity":{"nodeId":"body","activityVersionId":"exact-version","inputs":[],"outputs":[],"structure":null},
             "variables":[{"referenceKey":"local","name":"Local","type":{"alias":"String","collectionKind":"List"},"storageDriverKey":"json","initialValue":{"syntax":"Literal","value":[]}}],
             "outputMappings":[],"outcomeMappings":[],"inputs":[{"referenceKey":"forged"}]}
            """);
        var original = payload.GetRawText();
        var contract = new ActivityContract("1",
            [new("customer-id", "CustomerId", new("String"), true, true, new("Literal", ParseJson("\"default\"")), "json")],
            [new("reply", "Reply", new("String"), false, true, "json", SourceRepresentation: ValueRepresentation.TextValue)], []);

        var document = await _provider.TryProjectAsync(new(GraphActivityProvider.Key, schema, payload), contract, "actual-draft", "canonical-revision");

        Assert.NotNull(document);
        Assert.Equal("ActivityDefinition", document.ResourceKind);
        Assert.Equal("actual-draft", document.ResourceId);
        Assert.Equal("canonical-revision", document.ContractRevision);
        Assert.Equal("body", document.RootActivity!.NodeId);
        Assert.Equal("exact-version", document.RootActivity.ActivityVersionId);
        var input = Assert.Single(document.Inputs);
        Assert.Equal("customer-id", input.ReferenceKey);
        Assert.True(input.IsNullable);
        Assert.True(input.IsRequired);
        Assert.Equal("Literal", input.DefaultSyntax);
        Assert.Equal("default", input.DefaultValue!.Value.GetString());
        var variable = Assert.Single(document.Variables);
        Assert.Equal("local", variable.ReferenceKey);
        Assert.Equal(CollectionKind.List, variable.Type.CollectionKind);
        Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(variable.Default!.Value).ValueKind);
        Assert.Equal("reply", Assert.Single(document.Outputs).ReferenceKey);
        Assert.Equal(original, payload.GetRawText());
    }

    [Fact]
    public async Task Data_analysis_returns_null_only_for_a_different_provider()
    {
        var contract = new ActivityContract("1", [], [], []);
        Assert.Null(await _provider.TryProjectAsync(new("other-provider", "1", ParseJson("null")), contract, "draft", "revision"));
        var unsupported = await Assert.ThrowsAsync<ActivityDataSelectionRejectedException>(() =>
            _provider.TryProjectAsync(new(GraphActivityProvider.Key, "future", ParseJson("{}")), contract, "draft", "revision").AsTask());
        Assert.Contains(unsupported.Findings, x => x.Type.EndsWith("activity.graph.schema-unsupported", StringComparison.Ordinal));
        var malformed = await Assert.ThrowsAsync<ActivityDataSelectionRejectedException>(() =>
            _provider.TryProjectAsync(new(GraphActivityProvider.Key, "1", ParseJson("{}")), contract, "draft", "revision").AsTask());
        Assert.Contains(malformed.Findings, x => x.Path == "/rootActivity");
    }

    [Fact]
    public async Task Data_analysis_refuses_an_owned_malformed_argument_instead_of_returning_an_empty_graph()
    {
        var manifest = CreateManifest("""
            {"rootActivity":{"nodeId":"body","activityVersionId":"exact-version","inputs":[{"referenceKey":"value","value":"not-an-argument"}],"outputs":[]},"variables":[],"outputMappings":[]}
            """);
        var rejected = await Assert.ThrowsAsync<ActivityDataSelectionRejectedException>(() =>
            _provider.TryProjectAsync(manifest, new("1", [], [], []), "draft", "revision").AsTask());
        Assert.Contains(rejected.Findings, x => x.Path == "/rootActivity/inputs/0");
    }

    [Fact]
    public void Graph_projection_reuses_the_singleton_provider_without_capturing_an_analyzer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityStructureService>(new TestActivityStructureService());
        services.AddSingleton(new ActivityContractAuthoringValidator(new TestContractCapabilityCatalog()));
        new GraphActivitiesDesignFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Same(provider.GetRequiredService<GraphActivityProvider>(), provider.GetRequiredService<IActivityDataSelectionDocumentProjector>());
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IActivityDataSelectionAnalyzer));
    }

    [Theory]
    [InlineData("public-ref", false)]
    [InlineData("PublicName", true)]
    public async Task Legacy_boundary_Input_reads_use_the_declared_reference_key(string read, bool rejected)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            rootActivity = ParseJson("""{"nodeId":"body","activityVersionId":"exact-version","inputs":[],"outputs":[]}"""),
            variables = Array.Empty<object>(),
            outputMappings = new[] { new { outputReferenceKey = "reply", source = new { syntax = "Input", value = new { referenceKey = read } } } }
        });
        var contract = new ActivityContract("1", [new("public-ref", "PublicName", new("String"), false, true, null, "elsa.json")],
            [new("reply", "Reply", new("String"), false, true, "elsa.json")], [new("done", "Done", true)]);
        var diagnostics = await _provider.ValidateAsync(new(GraphActivityProvider.Key, "1", payload), contract);
        Assert.Equal(rejected, diagnostics.Any(x => x.Code == "activity.graph.boundary-input-unknown"));
        if (rejected)
            Assert.Contains(diagnostics, x => x.Code == "activity.graph.boundary-input-unknown" && x.Location?.JsonPointer == "/outputMappings/0/source");
    }
}
