using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Mappings;
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
        // A leaf Elsa-3 activity whose "Message" property carries a literal-expression argument.
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
        Assert.Equal("Message", input.ReferenceKey);
        Assert.Equal("Hello world", input.Value.Value);
        Assert.Equal("Literal", input.Value.ExpressionType);
        Assert.Empty(node.Outputs);
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

    private static JsonElement Argument(string expressionType, string expressionValue) =>
        JsonSerializer.SerializeToElement(new
        {
            expression = new { type = expressionType, value = expressionValue },
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
            .Select(name => new InputDefinition(name, name, new TypeReference("String"), null, name, null))
            .ToArray();

        public IEnumerable<OutputDefinition> Outputs { get; } = outputNames
            .Select(name => new OutputDefinition(name, name, new TypeReference("String"), null, name, null))
            .ToArray();

        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;
    }
}
