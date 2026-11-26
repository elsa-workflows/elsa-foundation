using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    private static readonly JsonSerializerOptions CandidateOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    // Replays the production planner's reviewed promotion candidates (the documents the held native-host Fact
    // submits) through the reusable template compiler with an accept-all admission policy and a test-built publication
    // record, then publishes a workflow with two literal call sites through the real publish handler and placement.
    // It does not use the planner's call-site or replacement documents, executes no workflow and is not native-host
    // or ordinary activity-publication evidence.
    [Theory]
    [InlineData("sequence-direct")]
    [InlineData("sequence-parameter")]
    [InlineData("flowchart-direct")]
    [InlineData("flowchart-parameter")]
    [InlineData("bpmn-direct")]
    [InlineData("bpmn-parameter")]
    public async Task A_reviewed_promotion_candidate_publishes_and_places_two_calls_with_their_own_native_local(string name)
    {
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "native-blocks", "block-promotions.json")))!;
        Assert.Equal("pure-planner-golden-documents-not-runtime-evidence", fixture["kind"]!.GetValue<string>());
        var entry = Assert.Single(fixture["cases"]!.AsArray(), item => item!["name"]!.GetValue<string>() == name)!;

        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(BpmnProcess), TypeAliasConvention.CanonicalAlias(typeof(BpmnProcess)));
        registry.RegisterType(typeof(Elsa.Activities.Primitives.Activities.WriteLine), TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Primitives.Activities.WriteLine)));
        var write = SecretBindingCompilerFixture.ClrActivityVersion(typeof(Elsa.Activities.Primitives.Activities.WriteLine));
        var bpmn = SecretBindingCompilerFixture.ClrActivityVersion(typeof(BpmnProcess));
        bpmn.DesignFacets = [StructureFacet(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion)];
        var versions = new List<ActivityDefinitionVersion> { _writeLineActivity, write, _sequenceActivity, _flowchartActivity, bpmn };
        var dependencyVersions = new FakeActivityVersionStore(versions);
        var publications = new DefaultPublications();
        var templates = new DefaultTemplates();
        var sources = new List<WorkflowExecutableSourceReference>();
        var bpmnRoot = JsonSerializer.Deserialize<ActivityNode>("""
            {"nodeId":"process","activityVersionId":"BPMN","inputs":[],"outputs":[],"structure":{"kind":"elsa.bpmn.structure","schemaVersion":"1.0.0","payload":{
              "activities":[],"elements":[{"elementId":"start","elementType":"startEvent"},{"elementId":"end","elementType":"endEvent"}],
              "sequenceFlows":[{"flowId":"start-end","sourceRef":"start","targetRef":"end"}]}}}
            """.Replace("BPMN", bpmn.Id), CandidateOptions)!;
        foreach (var (dependency, root) in new[]
                 {
                     (_writeLineActivity, Node("write", Text("body"))),
                     (write, new ActivityNode("write", write.Id, [new WorkflowArgumentState("text", new ArgumentValue("body", "Literal"), null, null, null, null)], [])),
                     (_sequenceActivity, SequenceNode("steps", [Node("write", Text("body"))])),
                     (_flowchartActivity, FlowchartNode("chart", [Node("write", Text("body"))], [], "write")),
                     (bpmn, bpmnRoot)
                 })
            await PublishAuthorableAsync(dependency, root, dependencyVersions, registry, publications, templates, sources, emitsDone: true);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sequence"] = _sequenceActivity.Id, ["flowchart"] = _flowchartActivity.Id, ["bpmn"] = bpmn.Id, ["write"] = write.Id,
            ["set"] = "elsa.intrinsic.set@1", ["set-output"] = "elsa.intrinsic.set-output@1"
        };
        void Remap(JsonNode? value)
        {
            if (value is JsonArray array) { foreach (var item in array) Remap(item); return; }
            if (value is not JsonObject record) return;
            if (record["activityVersionId"] is JsonValue id && id.TryGetValue<string>(out var key) && placeholders.TryGetValue(key, out var actual))
                record["activityVersionId"] = actual;
            foreach (var item in record.ToArray()) Remap(item.Value);
        }
        var candidate = entry["candidate"]!.DeepClone();
        Remap(candidate);
        var contract = candidate["contract"].Deserialize<DesignContract>(CandidateOptions)!;
        var manifest = candidate["provider"].Deserialize<ActivityProviderManifest>(CandidateOptions)!;

        using var services = new ServiceCollection().AddScoped<IActivityTemplateInputBindingCompiler>(_ => new RuntimeInputBindingCompiler(registry)).BuildServiceProvider();
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()),
            services.GetRequiredService<IServiceScopeFactory>());
        var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
            new ActivityTemplateDependencyDiscovererRegistry([graph]), publications, dependencyVersions, templates,
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
            TestCompiler.SourceAuthority(dependencyVersions, _activityStructureService), _activityStructureService);
        var compiled = await coordinator.CompileAsync(new(new ActivityDefinition { Id = "promoted-definition", ActivityTypeKey = "promoted", Category = "Test" },
            new ActivityTemplateCandidate("ActivityDraft", "promoted-draft", 1, contract, manifest), "promoted-version", "1.0.0", 0));
        Assert.True(compiled.IsSuccessful, string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var template = compiled.Template!;
        var publication = DefaultPublication("promoted-version", "promoted-definition", contract, template, manifest,
            ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary);
        publications.Add(publication);
        templates.Add(template);
        sources.Add(SourceOf(publication, template));
        versions.Add(new ActivityDefinitionVersion("1.0.0", publication.DefinitionId)
        {
            Id = publication.DefinitionVersionId,
            Definition = new ActivityDefinition { Id = publication.DefinitionId, ActivityTypeKey = publication.ActivityTypeKey, Category = "Test" },
            ProviderKey = manifest.ProviderKey, ProviderSchemaVersion = manifest.SchemaVersion,
            ConsumerKey = template.Root.Descriptor.ConsumerKey, ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
            DescriptorPayload = template.Root.Descriptor.Payload, Inputs = []
        });

        var inputKey = Assert.Single(contract.Inputs).ReferenceKey;
        var calls = new[] { ("call-one", "one"), ("call-two", "two") }.Select(call => new ActivityNode(call.Item1, publication.DefinitionVersionId,
            [new WorkflowArgumentState(inputKey, new ArgumentValue(call.Item2, "Literal"), null, null, null, null)], [])).ToArray();
        var workflow = WorkflowVersion(SequenceNode("workflow", calls));
        var versionStore = new FakeVersionStore(workflow);
        var compiler = TestCompiler.Create(versionStore, dependencyVersions, _activityStructureService, registry, publications, templates,
            new DefaultSourceReferences([.. sources]), dataSelectionAnalyzer: TestCompiler.SourceAuthority(dependencyVersions, _activityStructureService));
        var published = await Handler(null, compiler, versionStore, triggerProviders: [new Elsa.Activities.Bpmn.Internal.BpmnProcessTriggerStimulusProvider()]).Handle(new PublishWorkflow(workflow.Id), CancellationToken.None);
        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(published.ArtifactId));

        var nodes = executable.RootActivity.DescendantsAndSelf().ToArray();
        var setters = nodes.Where(node => node.IntrinsicKind == WorkflowIntrinsicKind.Set).ToArray();
        var outputs = nodes.Where(node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput).ToArray();
        Assert.Equal(2, setters.Length);
        Assert.Equal(2, outputs.Length);
        var scopes = new List<string>();
        foreach (var setter in setters)
        {
            var scope = setter.IntrinsicVariable!.DeclaringScopeId;
            scopes.Add(scope);
            var owner = Assert.Single(nodes, node => node.ExecutableNodeId == scope);
            var declaration = Assert.Single(owner.Structure!.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(PublishedDeclarationOptions)!);
            Assert.Equal(("local", "String"), (declaration.VariableKey, declaration.Type.Alias));
            Assert.NotNull(declaration.InitialBinding);
            Assert.Equal(DurableValueStorage.Inline, declaration.Policy.Storage);
            Assert.Equal(DurableValueStorage.Inline, setter.InputBindings["value"].EffectivePolicy.Storage);
            // The generated Set Output reads this same placed local.
            Assert.Single(outputs, output => output.InputBindings["value"].Variable is { VariableKey: "local" } read && read.DeclaringScopeId == scope);
        }
        Assert.Equal(2, scopes.Distinct(StringComparer.Ordinal).Count());
    }
}
