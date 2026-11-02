using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

/// <summary>
/// Ordinary bound work, such as a user-defined agent or a human review, survives export and import with its exact
/// activity version and inputs, or is refused against the visible catalog without inventing a version.
/// </summary>
public sealed class BpmnExactBindingInterchangeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string AgentVersionId = "acme.copywriter@1.0.0";
    private const string ReviewVersionId = "elsa.human-review@1.0.0";

    [Fact]
    public void Bound_agent_and_review_tasks_round_trip_their_exact_versions_and_inputs()
    {
        var xml = new BpmnDocumentExporter().Export(Process());
        var imported = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(xml);
        var structure = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;

        var write = Assert.Single(structure.Elements, x => x.ElementId == "write");
        var agent = Assert.Single(structure.Activities, x => x.NodeId == write.ChildNodeId);
        Assert.Equal(AgentVersionId, agent.ActivityVersionId);
        var brief = Assert.Single(agent.Inputs);
        Assert.Equal("brief-key", brief.ReferenceKey);
        Assert.Equal("moss gardens", ((JsonElement)brief.Value!.Value!).GetString());
        var review = Assert.Single(structure.Activities, x => x.NodeId == Assert.Single(structure.Elements, e => e.ElementId == "human-review").ChildNodeId);
        Assert.Equal(ReviewVersionId, review.ActivityVersionId);
        // Without a verified host catalog the restored binding is proposed, not yet executable.
        Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == "write" && x.Availability == BpmnImportCapabilityAvailability.Unknown);
        Assert.DoesNotContain(imported.Analysis.Issues, x => x.ElementId == "write" && x.Severity != BpmnImportIssueSeverity.Info);
        Assert.Contains("elsa:activity", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task The_visible_catalog_decides_whether_an_exact_binding_can_execute(bool published, bool canExecute)
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityDefinitionLookup>(_ => new Catalog(published));
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>().ImportAsync(new BpmnDocumentExporter().Export(Process()));

        Assert.Equal(canExecute, result.Analysis.CanExecute);
        var structure = result.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;
        // Refusal keeps the authored binding; it never substitutes another version.
        Assert.Contains(structure.Activities, x => x.ActivityVersionId == AgentVersionId);
        if (!published)
            Assert.Contains(result.Analysis.Capabilities, x => x.Availability == BpmnImportCapabilityAvailability.Unsupported && x.Reason.Contains(AgentVersionId, StringComparison.Ordinal));
    }

    [Fact]
    public void A_tampered_exact_binding_is_refused_with_an_actionable_reason()
    {
        var xml = new BpmnDocumentExporter().Export(Process()).Replace("acme.copywriter@1.0.0", "\" broken", StringComparison.Ordinal);
        var tampered = System.Text.RegularExpressions.Regex.Replace(new BpmnDocumentExporter().Export(Process()),
            "elsa:activity=\"[^\"]*copywriter[^\"]*\"", "elsa:activity=\"{not json\"");

        var imported = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(tampered);

        Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == "write" && x.Availability == BpmnImportCapabilityAvailability.Unsupported &&
            x.Reason.Contains("Bind the task's activity explicitly", StringComparison.Ordinal));
        Assert.False(imported.Analysis.CanExecute);
        Assert.NotNull(xml);
    }

    private static ActivityNode Process()
    {
        ArgumentState Literal(string key, object value) => new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);
        var structure = new BpmnAuthoredStructure(
            activities:
            [
                new ActivityNode("agent-node", AgentVersionId, [Literal("brief-key", "moss gardens")], []),
                new ActivityNode("review-node", ReviewVersionId, [Literal("Title", "Launch post")], [])
            ],
            elements:
            [
                new BpmnElement("start", BpmnElementTypes.StartEvent),
                new BpmnElement("write", BpmnElementTypes.ServiceTask, name: "Copywriter", childNodeId: "agent-node"),
                new BpmnElement("human-review", BpmnElementTypes.UserTask, name: "Review", childNodeId: "review-node"),
                new BpmnElement("end", BpmnElementTypes.EndEvent)
            ],
            sequenceFlows:
            [
                new BpmnSequenceFlow("f1", "start", "write"),
                new BpmnSequenceFlow("f2", "write", "human-review"),
                new BpmnSequenceFlow("f3", "human-review", "end", conditionOutcome: "Approved")
            ]);
        return new("node-bpmn", "Elsa.BpmnProcess", [], [],
            new ActivityNodeStructure(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));
    }

    private sealed class Catalog(bool published) : IActivityDefinitionLookup
    {
        private static readonly Definition Process = new("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess");
        private static readonly Definition Agent = new("catalog-agent", "acme.copywriter");
        private static readonly Definition Review = new("catalog-review", "Elsa.Agent.Specialists.Review.Activities.HumanReview");

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>(published ? [Process, Agent, Review] : [Process, Review]);

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Exact bindings never consult version ordering.");

        public Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionSummary?>(new(definitionId + "-v1", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action));

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) => Task.FromResult<IActivityDefinitionVersion?>(versionId switch
        {
            AgentVersionId when published => new CatalogVersion(AgentVersionId, Agent),
            ReviewVersionId => new CatalogVersion(ReviewVersionId, Review),
            _ => null
        });
    }

    private sealed record Definition(string Id, string ActivityTypeKey) : IActivityDefinition
    {
        public string Category => "Test";
        public string? DisplayName => ActivityTypeKey;
        public string? Description => null;
    }

    private sealed record CatalogVersion(string Id, IActivityDefinition Definition) : IActivityDefinitionVersion
    {
        public string Version => "1.0.0";
        public string DefinitionId => Definition.Id;
        public string ProviderKey => "test";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test";
        public string ConsumerSchemaVersion => "1";
        public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
        public string SourceKind => "Test";
        public string SourceId => Id;
        public IEnumerable<InputDefinition> Inputs => [];
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;
    }
}
