using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Agent.Review.Contracts;
using Elsa.Agent.Review.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Agent.Tests.Composition;

/// <summary>
/// The editorial BPMN process leaves the host with its exact task bindings (T063): each agent and review task carries its
/// activity version and literal inputs in <c>elsa:taskBinding</c>, because the visible catalog declares every bound input a
/// portable literal. Import proposes each retained binding and refuses to execute until the author confirms them; the
/// confirmed import binds every task to its exact version and inputs and verifies them against the host catalog (asserted
/// on the imported structure). Its graph compiles and remaps through the shipped BPMN structure handler onto this host's
/// placed children and runs the same revision loop, admitting one human decision after a restart; the imported inputs
/// reaching execution through publication is proved with T036. Omissions (sensitive, credential, non-literal and unresolvable inputs) are proved per
/// element in the Interchange tests. Publishing the confirmed process as a workflow definition is proved in M5 (T036).
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
    public async Task An_exported_process_keeps_its_exact_bindings_and_runs_once_the_author_confirms_them()
    {
        await using var host = await EditorialHost.StartAsync("bpmn-interchange", new(
            [EditorialCompositionTests.Article("Moss gardens grow slowly."), EditorialCompositionTests.Article("Moss gardens reward patience.")],
            [EditorialCompositionTests.Review("changes_requested", "Lead with the reward."), EditorialCompositionTests.Review("approved", "Ready.")]));
        host.Subjects.Grant(Reviewer);
        BpmnImportResult? proposed = null;
        BpmnImportResult? imported = null;
        BpmnExportResult? exported = null;
        Dictionary<string, string> versions = [];

        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true, reauthor: async (graph, copywriter, proofreader) =>
        {
            versions = Versions(copywriter, proofreader);
            await using var services = Interchange(new Catalog(versions.Values.Distinct().ToArray(), new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [copywriter] = "brief-key", [ReviewVersionId] = "Title"
            }));
            exported = await services.GetRequiredService<IBpmnDocumentExporter>().ExportAsync(Authored(graph, versions));
            var importer = services.GetRequiredService<IBpmnDocumentImporter>();
            proposed = await importer.ImportAsync(exported.Xml);
            imported = await importer.ImportAsync(exported.Xml, new BpmnImportOptions { ConfirmedBindings = BoundTasks });

            // Publication places each confirmed child under its executable identity, and the handler remaps the graph to match.
            var placed = graph.Elements.Where(x => x.ChildNodeId is not null).ToDictionary(x => x.ElementId, x => x.ChildNodeId!, StringComparer.Ordinal);
            var confirmed = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Elements
                .Where(x => placed.ContainsKey(x.ElementId)).ToDictionary(x => x.ChildNodeId!, x => placed[x.ElementId], StringComparer.Ordinal);
            var handler = services.GetServices<IActivityStructureHandler>().Single(x => x.Kind == BpmnProcessActivity.StructureKind);
            var compiled = handler.RemapExecutableStructure(handler.CompileExecutableStructure(imported.ProcessNode), confirmed);
            return new ExecutableActivityStructure(compiled.Kind, compiled.SchemaVersion, compiled.Payload);
        });

        Assert.NotNull(exported);
        Assert.NotNull(proposed);
        Assert.NotNull(imported);
        Assert.Empty(exported.Omissions);
        foreach (var authored in versions.Values.Concat([Brief, ReviewTitle]))
            Assert.Contains(authored, exported.Xml, StringComparison.Ordinal);

        // Until the author confirms them, every retained binding is only a proposal and nothing is bound.
        Assert.False(proposed.Analysis.CanExecute);
        foreach (var task in BoundTasks)
        {
            var proposal = Assert.Single(proposed.Analysis.Capabilities, x => x.ElementId == task);
            Assert.Equal(BpmnImportCapabilityAvailability.RequiresBinding, proposal.Availability);
            Assert.Equal(versions[task], proposal.RetainedBinding!.ActivityVersionId);
        }
        Assert.Empty(proposed.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!.Activities);

        // Confirmed, each task is bound to its exact version and literal inputs, verified against the host catalog.
        Assert.True(imported.Analysis.CanExecute);
        var bound = imported.ProcessNode.Structure!.Payload.Deserialize<BpmnAuthoredStructure>(Web)!;
        foreach (var task in BoundTasks)
        {
            var child = Assert.Single(bound.Activities, x => x.NodeId == Assert.Single(bound.Elements, e => e.ElementId == task).ChildNodeId);
            Assert.Equal(versions[task], child.ActivityVersionId);
        }
        Assert.Equal(Brief, ((JsonElement)Assert.Single(Assert.Single(bound.Activities, x => x.ActivityVersionId == versions["write"]).Inputs).Value.Value!).GetString());

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

    /// <summary>A visible host catalog that knows exactly the listed versions and the BPMN process; each version declares its one listed input.</summary>
    private sealed class Catalog(IReadOnlyCollection<string> versions, IReadOnlyDictionary<string, string> inputs) : IActivityDefinitionLookup
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
            Task.FromResult<IActivityDefinitionVersion?>(versions.Contains(versionId) ? new CatalogVersion(versionId, new Definition("definition:" + versionId, versionId),
                inputs.TryGetValue(versionId, out var input) ? [new InputDefinition(input, input, new("String"), "Json", input, null, IsNullable: true)] : []) : null);
    }

    private sealed record Definition(string Id, string ActivityTypeKey) : IActivityDefinition
    {
        public string Category => "Test";
        public string? DisplayName => ActivityTypeKey;
        public string? Description => null;
    }

    private sealed record CatalogVersion(string Id, IActivityDefinition Definition, IEnumerable<InputDefinition> Inputs) : IActivityDefinitionVersion
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
        public IEnumerable<OutputDefinition> Outputs => [];
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;
    }
}
