using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>
/// The editorial BPMN process leaves the host as a portable document and comes back through the visible catalog:
/// the imported authored process compiles, with the shipped BPMN structure handler, into the graph that runs the
/// same revision loop and admits one human decision after a restart. A missing agent version is refused.
/// </summary>
public sealed class EditorialInterchangeTests
{
    private const string Reviewer = "reviewer-ada";
    private const string GateVersionId = "sample.editorial-gate@1.0.0";
    private const string ReviewVersionId = "elsa.human-review@1.0.0";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_exported_and_reimported_process_keeps_exact_agent_versions_and_runs_the_revision_loop()
    {
        await using var host = await EditorialHost.StartAsync("bpmn-interchange", new(
            [EditorialCompositionTests.Article("Moss gardens grow slowly."), EditorialCompositionTests.Article("Moss gardens reward patience.")],
            [EditorialCompositionTests.Review("changes_requested", "Lead with the reward."), EditorialCompositionTests.Review("approved", "Ready.")]));
        host.Subjects.Grant(Reviewer);
        BpmnImportResult? imported = null;
        string[] bound = [];

        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true, reauthor: async (graph, copywriter, proofreader) =>
        {
            var authored = Authored(graph, copywriter, proofreader);
            bound = [copywriter, proofreader];
            await using var services = Interchange(new Catalog([GateVersionId, ReviewVersionId, copywriter, proofreader]));
            imported = await services.GetRequiredService<IBpmnDocumentImporter>().ImportAsync(new BpmnDocumentExporter().Export(authored));
            var handler = services.GetServices<IActivityStructureHandler>().Single(x => x.Kind == BpmnProcessActivity.StructureKind);
            // Publication places each authored child under its executable identity; the handler remaps the graph to match.
            var placed = graph.Elements.Where(x => x.ChildNodeId is not null).ToDictionary(x => x.ElementId, x => x.ChildNodeId!, StringComparer.Ordinal);
            var authoredToExecutable = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Elements
                .Where(x => x.ChildNodeId is not null).ToDictionary(x => x.ChildNodeId!, x => placed[x.ElementId], StringComparer.Ordinal);
            var compiled = handler.RemapExecutableStructure(handler.CompileExecutableStructure(imported.ProcessNode), authoredToExecutable);
            return new ExecutableActivityStructure(compiled.Kind, compiled.SchemaVersion, compiled.Payload);
        });

        Assert.NotNull(imported);
        Assert.True(imported.Analysis.CanExecute);
        var activities = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Activities;
        Assert.Equal(bound.Append(GateVersionId).Append(ReviewVersionId).Order(StringComparer.Ordinal).ToArray(), activities.Select(x => x.ActivityVersionId).Distinct().Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        await host.StartRunAsync(executable);
        await host.PumpAsync();
        Assert.Equal(["copywriter", "proofreader", "copywriter", "proofreader"], host.Model.Calls.Select(x => x.Role).ToArray());
        var pending = await host.PendingReviewAsync();
        var view = Assert.IsType<HumanReviewView>(await host.ReadReviewAsync(pending.InvocationId));

        await host.RestartAsync();
        var command = new ReviewDecisionCommand(host.WorkflowExecutionId, pending.InvocationId, view.ContentHash, ReviewDecision.Approve, null, "request-interchange");
        await host.DecideAsync(command, Reviewer);
        await host.PumpAsync();

        Assert.Equal(ReviewDecisionReceiptStatus.Accepted, (await host.DecideAsync(command, Reviewer)).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", review.Completion!.OutcomeKey);
        Assert.Equal(4, host.Model.Calls.Count);
    }

    [Fact]
    public async Task A_document_naming_an_agent_version_the_host_cannot_see_is_refused_without_substitution()
    {
        var (_, copywriter, proofreader) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true);
        var graph = await GraphAsync();
        await using var services = Interchange(new Catalog([GateVersionId, ReviewVersionId, copywriter]));

        var imported = await services.GetRequiredService<IBpmnDocumentImporter>().ImportAsync(new BpmnDocumentExporter().Export(Authored(graph, copywriter, proofreader)));

        Assert.False(imported.Analysis.CanExecute);
        Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == "proofread" && x.Availability == BpmnImportCapabilityAvailability.Unsupported);
        var activities = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Activities;
        Assert.Contains(activities, x => x.ActivityVersionId == proofreader);
    }

    private static async Task<BpmnStructure> GraphAsync()
    {
        BpmnStructure? captured = null;
        await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true, reauthor: (graph, _, _) =>
        {
            captured = graph;
            return Task.FromResult(new ExecutableActivityStructure(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(graph)));
        });
        return captured!;
    }

    /// <summary>The authored process an author would save: each task bound to its exact activity version.</summary>
    private static ActivityNode Authored(BpmnStructure graph, string copywriter, string proofreader)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gate"] = GateVersionId, ["check"] = GateVersionId, ["write"] = copywriter, ["proofread"] = proofreader, ["human-review"] = ReviewVersionId
        };
        var activities = graph.Elements.Where(x => x.ChildNodeId is not null)
            .Select(x => new ActivityNode(x.ChildNodeId!, versions[x.ElementId], [], []))
            .ToArray();
        var structure = new BpmnAuthoredStructure(activities, graph.Elements, graph.SequenceFlows);
        return new ActivityNode("editorial-flowchart", BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId, [], [],
            new ActivityNodeStructure(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));
    }

    private static ServiceProvider Interchange(Catalog catalog)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDefinitionLookup>(catalog);
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A visible host catalog that knows exactly the listed versions and the BPMN process.</summary>
    private sealed class Catalog(IReadOnlyCollection<string> versions) : IActivityDefinitionLookup
    {
        private static readonly Definition Process = new("catalog-process", "Elsa.Activities.Bpmn.Activities.BpmnProcess");

        private IEnumerable<IActivityDefinition> Definitions => versions.Select(x => new Definition("definition:" + x, x)).Append<IActivityDefinition>(Process);

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null,
            string? displayName = null, string? description = null, bool? tenantAgnostic = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Definitions);

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Exact bindings never consult version ordering.");

        public Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinitionVersionSummary?>(new(definitionId + "-v1", "1.0.0", DateTimeOffset.UnixEpoch, ActivityExecutionType.Action));

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(versions.Contains(versionId) ? new CatalogVersion(versionId, new Definition("definition:" + versionId, versionId)) : null);
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
