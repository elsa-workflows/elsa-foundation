using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Regression coverage for the Elsa-3 → Elsa-4 activity mapper. The mapper resolves each
/// <see cref="Elsa3Activity.AdditionalProperties"/> entry against the target activity version's
/// declared inputs/outputs and projects it onto <see cref="ActivityNode.Inputs"/> /
/// <see cref="ActivityNode.Outputs"/>. Prior to the #378 fix an inverted De Morgan guard
/// (<c>||</c> where <c>&amp;&amp;</c> was intended) made the population loop body unreachable, so
/// every imported activity silently dropped all argument bindings.
/// </summary>
public sealed class Elsa3ActivityToStateTests
{
    private const string ActivityType = "Elsa.WriteLine";
    private const string DefinitionId = "def-writeline";
    private const string VersionId = "ver-writeline-1";

    [Fact]
    public async Task Map_InputArgument_PopulatesInputsOnActivityNode()
    {
        // A leaf Elsa-3 activity whose "Message" property carries a literal-expression argument, stored under the declared
        // input's reference key (the fake catalog declares "key:message").
        var source = new Elsa3Activity
        {
            Id = "a1",
            NodeId = "node-1",
            Name = "WriteLine1",
            Type = ActivityType,
            Version = 1,
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["Message"] = Argument("Literal", "Hello world"),
            },
        };

        var lookup = new FakeActivityDefinitionLookup(
            inputNames: ["Message"],
            outputNames: []);

        var mapper = new Elsa3ActivityToState(lookup);

        var node = await mapper.Map(source, CancellationToken.None);

        var input = Assert.Single(node.Inputs);
        Assert.Equal("key:message", input.ReferenceKey);
        Assert.Equal("Hello world", input.Value.Value);
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Empty(node.Outputs);
    }

    [Fact]
    public async Task Map_InputArgument_whose_name_differs_in_case_from_the_declared_input_is_stored_under_the_declared_key()
    {
        var mapper = new Elsa3ActivityToState(new FakeActivityDefinitionLookup(inputNames: ["Message"], outputNames: []));

        var node = await mapper.Map(Leaf(("message", Argument("Literal", "Hello world"))), CancellationToken.None);

        var input = Assert.Single(node.Inputs);
        Assert.Equal("key:message", input.ReferenceKey);
        Assert.Equal("Hello world", input.Value.Value);
    }

    [Fact]
    public async Task Map_two_properties_that_bind_the_same_declared_input_is_refused()
    {
        var mapper = new Elsa3ActivityToState(new FakeActivityDefinitionLookup(inputNames: ["Message"], outputNames: []));

        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            mapper.Map(Leaf(("message", Argument("Literal", "first")), ("Message", Argument("Literal", "second"))), CancellationToken.None).AsTask());

        Assert.Contains("'node-1'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'message' and 'Message'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'key:message'", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("first", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_property_that_matches_several_declared_inputs_ignoring_case_binds_the_exact_name()
    {
        var mapper = new Elsa3ActivityToState(new ReusableActivityImportFixtures.BuiltInActivityLookup(Input("Url", "url-key"), Input("URL", "upper-url-key")));

        var node = await mapper.Map(Leaf(("URL", Argument("Literal", "value"))), CancellationToken.None);

        Assert.Equal("upper-url-key", Assert.Single(node.Inputs).ReferenceKey);
    }

    [Fact]
    public async Task Map_property_that_matches_several_declared_inputs_only_ignoring_case_is_refused()
    {
        var mapper = new Elsa3ActivityToState(new ReusableActivityImportFixtures.BuiltInActivityLookup(Input("Url", "url-key"), Input("URL", "upper-url-key")));

        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            mapper.Map(Leaf(("url", Argument("Literal", "value"))), CancellationToken.None).AsTask());

        Assert.Contains("'url' matches more than one declared input: 'Url', 'URL'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_OutputArgument_PopulatesOutputsOnActivityNode()
    {
        var source = new Elsa3Activity
        {
            Id = "a1",
            NodeId = "node-1",
            Name = "WriteLine1",
            Type = ActivityType,
            Version = 1,
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["Result"] = Argument("JavaScript", "getResult()"),
            },
        };

        var lookup = new FakeActivityDefinitionLookup(
            inputNames: [],
            outputNames: ["Result"]);

        var mapper = new Elsa3ActivityToState(lookup);

        var node = await mapper.Map(source, CancellationToken.None);

        var output = Assert.Single(node.Outputs);
        Assert.Equal("Result", output.ReferenceKey);
        Assert.Equal("getResult()", output.Value.Value);
        Assert.Equal("JavaScript", output.Value.ExpressionType);
        Assert.Empty(node.Inputs);
    }

    [Fact]
    public async Task Collect_OutputOnlyMemoryReference_PreservesProducerOccurrence()
    {
        var source = new Elsa3Activity
        {
            Id = "a1",
            NodeId = "node-1",
            Name = "Producer",
            Type = ActivityType,
            Version = 1,
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["Result"] = MemoryReference("result-1"),
            },
        };

        var lookup = new FakeActivityDefinitionLookup(
            inputNames: [],
            outputNames: ["Result"]);

        var inventory = await new Elsa3MemoryReferenceGraph(lookup).CollectAsync(source, CancellationToken.None);

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

        var lookup = new FakeActivityDefinitionLookup(
            inputNames: ["Value"],
            outputNames: ["Value"]);

        var inventory = await new Elsa3MemoryReferenceGraph(lookup).CollectAsync(root, CancellationToken.None);

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
    /// Hand-rolled lookup (the test project pulls in no mocking library) returning a single
    /// activity definition + version whose declared input/output names drive the mapper's
    /// property-to-argument matching.
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
        public string Category => "Console";
        public string? DisplayName => "Write Line";
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
