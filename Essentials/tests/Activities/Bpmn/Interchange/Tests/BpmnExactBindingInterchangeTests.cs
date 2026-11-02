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
            Assert.Contains(result.Analysis.Capabilities, x => x.ElementId == "write" && x.Availability == BpmnImportCapabilityAvailability.Unsupported);
    }

    [Fact]
    public void A_tampered_exact_binding_is_refused_with_an_actionable_reason()
    {
        var tampered = System.Text.RegularExpressions.Regex.Replace(new BpmnDocumentExporter().Export(Process()),
            "elsa:activity=\"[^\"]*copywriter[^\"]*\"", "elsa:activity=\"{not json\"");

        var imported = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(tampered);

        Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == "write" && x.Availability == BpmnImportCapabilityAvailability.Unsupported);
        Assert.False(imported.Analysis.CanExecute);
    }

    [Fact]
    public void Secret_expression_and_sensitive_bindings_never_leave_or_enter_a_document()
    {
        ArgumentState Input(string key, string expressionType, bool? sensitive = null) =>
            new(key, new ArgumentValue(JsonSerializer.SerializeToElement("x"), expressionType), null, null, null, sensitive);
        foreach (var unsafeInput in new[] { Input("header", "Secret"), Input("url", "JavaScript"), Input("token", "Literal", sensitive: true) })
        {
            var xml = new BpmnDocumentExporter().Export(Process(new ActivityNode("agent-node", AgentVersionId, [unsafeInput], [])));
            Assert.DoesNotMatch("elsa:activity=\"[^\"]*copywriter", xml);
        }

        var crafted = System.Text.RegularExpressions.Regex.Replace(new BpmnDocumentExporter().Export(Process()),
            "elsa:activity=\"[^\"]*copywriter[^\"]*\"",
            "elsa:activity=\"" + System.Security.SecurityElement.Escape(JsonSerializer.Serialize(new
            {
                nodeId = "x", activityVersionId = AgentVersionId, outputs = Array.Empty<object>(),
                inputs = new[] { new { referenceKey = "header", value = new { value = "prod-api-key", expressionType = "Secret" } } }
            }, Web)) + "\"");
        var imported = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(crafted);

        Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == "write" && x.Availability == BpmnImportCapabilityAvailability.Unsupported);
        Assert.False(imported.Analysis.CanExecute);
        var structure = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;
        Assert.DoesNotContain(structure.Activities, x => x.ActivityVersionId == AgentVersionId);
    }

    [Fact]
    public void A_boundary_on_a_task_with_a_retained_binding_survives_the_round_trip()
    {
        var xml = new BpmnDocumentExporter().Export(Process());
        var prefix = System.Text.RegularExpressions.Regex.Match(xml, "<([A-Za-z0-9]+:)?userTask").Groups[1].Value;
        var boundary = $"<{prefix}boundaryEvent id=\"timeout\" attachedToRef=\"human-review\"><{prefix}timerEventDefinition><{prefix}timeDuration>PT1H</{prefix}timeDuration></{prefix}timerEventDefinition></{prefix}boundaryEvent>" +
                       $"<{prefix}sequenceFlow id=\"f-timeout\" sourceRef=\"timeout\" targetRef=\"end\" />";
        var withBoundary = xml.Replace($"</{prefix}process>", boundary + $"</{prefix}process>", StringComparison.Ordinal);

        var imported = new BpmnDocumentImporter(new BpmnWorkBinder()).Import(withBoundary);
        var structure = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;

        Assert.Contains(structure.Elements, x => x.ElementId == "timeout" && x.AttachedToRef == "human-review");
        Assert.Contains(structure.SequenceFlows, x => x.FlowId == "f-timeout");
        Assert.Contains(structure.Activities, x => x.ActivityVersionId == ReviewVersionId);
    }

    private static ActivityNode Process(ActivityNode? agent = null) => Node(Structure(agent));

    private static ActivityNode Node(BpmnAuthoredStructure structure) => new("node-bpmn", "Elsa.BpmnProcess", [], [],
        new ActivityNodeStructure(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));

    private static BpmnAuthoredStructure Structure(ActivityNode? agent = null)
    {
        ArgumentState Literal(string key, object value) => new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);
        return new BpmnAuthoredStructure(
            activities:
            [
                agent ?? new ActivityNode("agent-node", AgentVersionId, [Literal("brief-key", "moss gardens")], []),
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
