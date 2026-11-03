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
/// The editorial BPMN process leaves the host as a portable document without its bound work: agent and review
/// versions and their authored inputs never travel in shared XML. Import reports every bound task as requiring an
/// explicit binding and refuses to execute. The imported graph itself (elements, outcome flows, the loop) then compiles
/// and remaps through the shipped BPMN structure handler and runs the same revision loop over this host's placed
/// children, admitting one human decision after a restart. Resolving the author's rebinding through publication is
/// proved with workflow-definition publishing in M5 (T036).
/// </summary>
public sealed class EditorialInterchangeTests
{
    private const string Reviewer = "reviewer-ada";
    private const string GateVersionId = "sample.editorial-gate@1.0.0";
    private const string ReviewVersionId = "elsa.human-review@1.0.0";
    private static readonly string[] BoundTasks = ["gate", "write", "check", "proofread", "human-review"];
    private const string Brief = "moss gardens";
    private const string ReviewTitle = "Editorial article";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_imported_process_refuses_unbound_work_and_its_graph_runs_over_rebound_tasks()
    {
        await using var host = await EditorialHost.StartAsync("bpmn-interchange", new(
            [EditorialCompositionTests.Article("Moss gardens grow slowly."), EditorialCompositionTests.Article("Moss gardens reward patience.")],
            [EditorialCompositionTests.Review("changes_requested", "Lead with the reward."), EditorialCompositionTests.Review("approved", "Ready.")]));
        host.Subjects.Grant(Reviewer);
        BpmnImportResult? imported = null;
        var xml = "";
        string[] bound = [];

        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true, reauthor: async (graph, copywriter, proofreader) =>
        {
            var versions = Versions(copywriter, proofreader);
            bound = [copywriter, proofreader];
            await using var services = Interchange(new Catalog([GateVersionId, ReviewVersionId, copywriter, proofreader]));
            xml = new BpmnDocumentExporter().Export(Authored(graph, versions));
            imported = await services.GetRequiredService<IBpmnDocumentImporter>().ImportAsync(xml);

            // The author binds each imported task to its exact version; publication then places each authored child
            // under its executable identity, and the handler remaps the graph to match.
            var placed = graph.Elements.Where(x => x.ChildNodeId is not null).ToDictionary(x => x.ElementId, x => x.ChildNodeId!, StringComparer.Ordinal);
            var rebound = Rebind(imported.ProcessNode, versions);
            var handler = services.GetServices<IActivityStructureHandler>().Single(x => x.Kind == BpmnProcessActivity.StructureKind);
            var compiled = handler.RemapExecutableStructure(handler.CompileExecutableStructure(rebound),
                placed.ToDictionary(x => AuthorNodeId(x.Key), x => x.Value, StringComparer.Ordinal));
            return new ExecutableActivityStructure(compiled.Kind, compiled.SchemaVersion, compiled.Payload);
        });

        Assert.NotNull(imported);
        foreach (var authored in new[] { GateVersionId, ReviewVersionId, Brief, ReviewTitle }.Concat(bound))
            Assert.DoesNotContain(authored, xml, StringComparison.Ordinal);
        Assert.False(imported.Analysis.CanExecute);
        foreach (var task in BoundTasks)
            Assert.Contains(imported.Analysis.Capabilities, x => x.ElementId == task && x.Availability == BpmnImportCapabilityAvailability.RequiresBinding);
        Assert.Empty(imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Activities);

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

    private static Dictionary<string, string> Versions(string copywriter, string proofreader) => new(StringComparer.Ordinal)
    {
        ["gate"] = GateVersionId, ["check"] = GateVersionId, ["write"] = copywriter, ["proofread"] = proofreader, ["human-review"] = ReviewVersionId
    };

    private static string AuthorNodeId(string elementId) => "author-" + elementId;

    /// <summary>The authored process an author would save: each task bound to its exact version with literal inputs.</summary>
    private static ActivityNode Authored(BpmnStructure graph, IReadOnlyDictionary<string, string> versions)
    {
        var activities = graph.Elements.Where(x => x.ChildNodeId is not null)
            .Select(x => new ActivityNode(x.ChildNodeId!, versions[x.ElementId], x.ElementId switch
            {
                "write" => [Literal("brief-key", Brief)],
                "human-review" => [Literal("Title", ReviewTitle)],
                _ => []
            }, []))
            .ToArray();
        return Process(new BpmnAuthoredStructure(activities, graph.Elements, graph.SequenceFlows));
    }

    private static ArgumentState Literal(string key, string value) =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);

    /// <summary>What the importing author does in the designer: bind each imported task to an exact version.</summary>
    private static ActivityNode Rebind(ActivityNode imported, IReadOnlyDictionary<string, string> versions)
    {
        var structure = imported.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;
        var elements = structure.Elements.Select(x => versions.ContainsKey(x.ElementId)
            ? new BpmnElement(x.ElementId, x.ElementType, x.Name, AuthorNodeId(x.ElementId), x.LaneId, x.DefaultFlowId, x.EventDefinitions, x.Properties,
                x.AttachedToRef, x.CancelActivity, x.LoopCharacteristics, x.IsForCompensation, x.CompensationHandlerElementId, x.IsTransaction,
                x.TriggeredByEvent, x.ListenerNodeId)
            : x).ToArray();
        var activities = versions.Select(x => new ActivityNode(AuthorNodeId(x.Key), x.Value, [], [])).ToArray();
        return Process(new BpmnAuthoredStructure(activities, elements, structure.SequenceFlows));
    }

    private static ActivityNode Process(BpmnAuthoredStructure structure) =>
        new("editorial-flowchart", BpmnDocumentImporter.DefaultBpmnProcessActivityVersionId, [], [],
            new ActivityNodeStructure(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));

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
