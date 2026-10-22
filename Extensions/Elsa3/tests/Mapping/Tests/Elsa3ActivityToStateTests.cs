using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Regression coverage for binding Elsa 3 properties to an Elsa 4 activity's declared inputs and outputs. A type that
/// the built-in map does not cover (<c>Acme.Leaf</c>) resolves verbatim to its catalog key and binds by name
/// (spec FR-010). Prior to the #378 fix an inverted De Morgan guard made the population loop unreachable, so every
/// imported activity silently dropped all argument bindings.
/// </summary>
public sealed class Elsa3ActivityToStateTests
{
    private const string ActivityType = "Acme.Leaf";
    private const string DefinitionId = "def-acme-leaf";
    private const string VersionId = "ver-acme-leaf-1";

    /// <summary>A catalog activity declaring the input <c>Message</c>, under the key <c>key:message</c>.</summary>
    private readonly IActivityDefinitionLookup _messageCatalog = new FakeActivityDefinitionLookup(inputNames: ["Message"], outputNames: []);

    /// <summary>A catalog activity declaring two inputs whose names differ only in case, under distinct keys.</summary>
    private readonly IActivityDefinitionLookup _urlCatalog = new ReusableActivityImportFixtures.BuiltInActivityLookup(Input("Url", "url-key"), Input("URL", "upper-url-key"));

    [Fact]
    public async Task Map_InputArgument_PopulatesInputsOnActivityNode()
    {
        var node = await MapAsync(_messageCatalog, Leaf(("Message", Argument("Literal", "Hello world"))));

        var input = Assert.Single(node.Inputs);
        Assert.Equal("key:message", input.ReferenceKey);
        Assert.Equal("Hello world", input.Value.Value);
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Empty(node.Outputs);
    }

    [Fact]
    public async Task Map_InputArgument_whose_name_differs_in_case_from_the_declared_input_is_stored_under_the_declared_key()
    {
        var node = await MapAsync(_messageCatalog, Leaf(("message", Argument("Literal", "Hello world"))));

        var input = Assert.Single(node.Inputs);
        Assert.Equal("key:message", input.ReferenceKey);
        Assert.Equal("Hello world", input.Value.Value);
    }

    [Fact]
    public async Task Map_two_properties_that_bind_the_same_declared_input_is_refused()
    {
        var result = await ConversionServices.ConvertAsync(_messageCatalog, ConversionServices.Definition(
            Leaf(("message", Argument("Literal", "first")), ("Message", Argument("Literal", "second")))));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.DuplicateInputBinding, refusal.Code);
        Assert.Equal("node-1", refusal.Metadata["ActivityNodeId"]);
        Assert.Contains("'message' and 'Message'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'key:message'", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_property_that_matches_several_declared_inputs_ignoring_case_binds_the_exact_name()
    {
        var node = await MapAsync(_urlCatalog, Leaf(("URL", Argument("Literal", "value"))));

        Assert.Equal("upper-url-key", Assert.Single(node.Inputs).ReferenceKey);
    }

    [Fact]
    public async Task Map_property_that_matches_several_declared_inputs_only_ignoring_case_is_refused()
    {
        var result = await ConversionServices.ConvertAsync(_urlCatalog, ConversionServices.Definition(Leaf(("url", Argument("Literal", "value")))));

        var refusal = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.DuplicateInputBinding, refusal.Code);
        Assert.Contains("'url' matches more than one declared input: 'Url', 'URL'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_OutputArgument_PopulatesOutputsOnActivityNode()
    {
        var catalog = new FakeActivityDefinitionLookup(inputNames: [], outputNames: ["Result"]);

        var node = await MapAsync(catalog, Leaf(("Result", Argument("Literal", "result"))));

        var output = Assert.Single(node.Outputs);
        Assert.Equal("key:result", output.ReferenceKey);
        Assert.Equal("result", output.Value.Value);
        Assert.Equal("Literal", output.Value.ExpressionType);
        Assert.Empty(node.Inputs);
    }

    [Fact]
    public async Task Collect_OutputOnlyMemoryReference_PreservesProducerOccurrence()
    {
        var source = Leaf(("Result", MemoryReference("result-1")));
        var contract = new Elsa3NodeContract([], [Output("Result")]);

        var inventory = await new Elsa3MemoryReferenceGraph().CollectAsync(source, _ => contract);

        var occurrence = Assert.Single(inventory.Occurrences);
        Assert.Equal("result-1", occurrence.MemoryReferenceId);
        Assert.Equal("$.root.Result.memoryReference", occurrence.JsonPath);
        Assert.Equal("node-1", occurrence.ActivityNodeId);
        Assert.Equal("/node-1", occurrence.ActivityPath);
        Assert.Equal("key:result", occurrence.StablePropertyKey);
        Assert.Equal(Elsa3PropertyDirection.Output, occurrence.Direction);
        Assert.Equal("node-1", occurrence.StructuralFrameId);
        Assert.Null(occurrence.Expression);
    }

    [Fact]
    public async Task Collect_ExpressionAndMemoryReference_PreservesBothDeclaredDirectionsAndStructuralFrame()
    {
        var leaf = new Elsa3Activity
        {
            Id = "leaf",
            NodeId = "node-leaf",
            Name = "Transform",
            Type = ActivityType,
            Version = 1,
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["Value"] = ArgumentWithMemoryReference("JavaScript", "input + 1", "value-1"),
            },
        };
        var nestedContainer = new Elsa3Activity
        {
            Id = "nested",
            NodeId = "node-nested",
            Name = "Nested",
            Type = ActivityType,
            Version = 1,
            Activities = [leaf],
        };
        var root = new Elsa3Activity
        {
            Id = "root",
            NodeId = "node-root",
            Name = "Root",
            Type = ActivityType,
            Version = 1,
            Activities = [nestedContainer],
        };
        var contract = new Elsa3NodeContract([Input("Value", "key:value")], [Output("Value")]);

        var inventory = await new Elsa3MemoryReferenceGraph().CollectAsync(root, _ => contract);

        Assert.Collection(
            inventory.Occurrences,
            input => Assert.Equal(Elsa3PropertyDirection.Input, input.Direction),
            output => Assert.Equal(Elsa3PropertyDirection.Output, output.Direction));

        foreach (var occurrence in inventory.Occurrences)
        {
            Assert.Equal("value-1", occurrence.MemoryReferenceId);
            Assert.Equal("$.root.activities[0].activities[0].Value.memoryReference", occurrence.JsonPath);
            Assert.Equal("node-leaf", occurrence.ActivityNodeId);
            Assert.Equal("/node-root/node-nested/node-leaf", occurrence.ActivityPath);
            Assert.Equal("key:value", occurrence.StablePropertyKey);
            Assert.Equal("node-nested", occurrence.StructuralFrameId);
            Assert.Equal("JavaScript", occurrence.Expression?.Type);
            Assert.Equal("input + 1", occurrence.Expression?.Value);
        }

        Assert.Equal(["node-root", "node-nested"], inventory.StructuralFrames.Select(x => x.Id));
        Assert.Equal("node-nested", Assert.Single(inventory.Nodes, x => x.ActivityNodeId == "node-leaf").StructuralFrameId);
    }

    private static async Task<ActivityNode> MapAsync(IActivityDefinitionLookup catalog, Elsa3Activity leaf)
    {
        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(leaf));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        return result.Value!.State.RootActivity!;
    }

    /// <summary>A leaf Elsa 3 activity, node <c>node-1</c>, carrying <paramref name="properties"/>.</summary>
    private static Elsa3Activity Leaf(params (string Name, JsonElement Value)[] properties) => new()
    {
        Id = "a1",
        NodeId = "node-1",
        Name = "Leaf",
        Type = ActivityType,
        Version = 1,
        AdditionalProperties = properties.ToDictionary(property => property.Name, property => property.Value),
    };

    private static InputDefinition Input(string name, string referenceKey) =>
        new(referenceKey, name, new TypeReference("String"), null, name, null, IsNullable: true);

    private static OutputDefinition Output(string name) =>
        new($"key:{name.ToLowerInvariant()}", name, new TypeReference("String"), null, name, null, true);

    private static JsonElement Argument(string expressionType, string expressionValue) =>
        JsonSerializer.SerializeToElement(new
        {
            expression = new { type = expressionType, value = expressionValue },
        });

    private static JsonElement MemoryReference(string id) =>
        JsonSerializer.SerializeToElement(new
        {
            memoryReference = new { id },
        });

    private static JsonElement ArgumentWithMemoryReference(string expressionType, string expressionValue, string id) =>
        JsonSerializer.SerializeToElement(new
        {
            expression = new { type = expressionType, value = expressionValue },
            memoryReference = new { id },
        });

    /// <summary>
    /// Hand-rolled lookup (the test project pulls in no mocking library) returning a single activity definition and
    /// version, whatever the key, whose declared input/output names drive property-to-argument matching.
    /// </summary>
    private sealed class FakeActivityDefinitionLookup(string[] inputNames, string[] outputNames) : IActivityDefinitionLookup
    {
        private readonly IActivityDefinition _definition = new FakeActivityDefinition();
        private readonly IActivityDefinitionVersion _version = new FakeActivityDefinitionVersion(inputNames, outputNames);

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_definition);

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null,
            bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>([_definition]);

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_version);

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(_version);

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>(
            [
                new ActivityDefinitionVersionSummary(VersionId, "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action),
            ]);
    }

    private sealed class FakeActivityDefinition : IActivityDefinition
    {
        public string Id => DefinitionId;
        public string ActivityTypeKey => ActivityType;
        public string Category => "Test";
        public string? DisplayName => "Acme leaf";
        public string? Description => null;
    }

    private sealed class FakeActivityDefinitionVersion(string[] inputNames, string[] outputNames) : IActivityDefinitionVersion
    {
        public string Id => VersionId;
        public string Version => "1.0.0";
        public string DefinitionId => Elsa3ActivityToStateTests.DefinitionId;
        public string ProviderKey => "elsa.clr";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "elsa.clr";
        public string ConsumerSchemaVersion => "1";
        public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
        public string SourceKind => "CLR";
        public string SourceId => ActivityType;
        public IActivityDefinition Definition => new FakeActivityDefinition();

        public IEnumerable<InputDefinition> Inputs { get; } = inputNames
            .Select(name => new InputDefinition($"key:{name.ToLowerInvariant()}", name, new TypeReference("String"), null, name, null, false))
            .ToArray();

        public IEnumerable<OutputDefinition> Outputs { get; } = outputNames
            .Select(name => new OutputDefinition($"key:{name.ToLowerInvariant()}", name, new TypeReference("String"), null, name, null, false))
            .ToArray();

        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;
    }
}
