using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Design;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class BpmnCompleteSourceCompilationTests
{
    [Fact]
    public void Template_compiler_preserves_its_existing_constructor_for_precompiled_callers()
    {
        Assert.NotNull(typeof(ActivityTemplateCompiler).GetConstructor([
            typeof(IActivityTemplateProviderCompilerRegistry), typeof(IActivityTemplateDependencyDiscovererRegistry),
            typeof(IActivityDefinitionVersionPublicationStore), typeof(IActivityDefinitionVersionStore),
            typeof(IExecutableActivityTemplateReader), typeof(IActivityTemplateAdmissionPolicy), typeof(TimeProvider)
        ]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_template_coordinator_compiles_complete_source_with_exact_native_dependency_templates(bool standardScope)
    {
        using var fixture = new GraphFixture();
        var result = await fixture.Compile(Process(canStart: true, nestedStandardScope: standardScope));
        Assert.True(result.IsSuccessful, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var dependencies = result.Template!.DirectDependencies;
        var process = Assert.Single(dependencies, dependency => dependency.OccurrenceId == "process");
        var overlay = Assert.IsType<ExecutableActivityTemplateOccurrenceOverlay>(process.OccurrenceOverlay);
        var processStructure = Assert.IsType<ExecutableActivityStructure>(overlay.Structure);
        Assert.Equal("1.0.0", processStructure.SchemaVersion);
        Assert.Equal("elsa.bpmn.structure", processStructure.Kind);
        Assert.True(Assert.Single(overlay.InputBindings.EnumerateArray()).GetProperty("value").GetProperty("value").GetBoolean());
        Assert.Contains(dependencies, dependency => dependency.OccurrenceId == "work" && dependency.ParentOccurrenceId == (standardScope ? "scope-sub" : "process"));
        if (standardScope)
        {
            var header = Assert.Single(dependencies, dependency => dependency.OccurrenceId == "scope-sub");
            var headerOverlay = Assert.IsType<ExecutableActivityTemplateOccurrenceOverlay>(header.OccurrenceOverlay);
            var argument = Assert.Single(headerOverlay.InputBindings.EnumerateArray());
            Assert.Equal("CanStartWorkflow", argument.GetProperty("referenceKey").GetString());
            Assert.False(argument.GetProperty("value").GetProperty("value").GetBoolean());
            var headerStructure = Assert.IsType<ExecutableActivityStructure>(headerOverlay.Structure);
            var variable = Assert.Single(headerStructure.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(Web)!);
            Assert.Equal(42, variable.InitialBinding?.Literal?.InlineValue?.GetInt32());
        }
    }

    [Fact]
    public async Task Actual_template_coordinator_refuses_unsupported_source_before_any_template_is_returned()
    {
        using var fixture = new GraphFixture();
        var result = await fixture.Compile(Process(canStart: false, taskAttributes: " implementation=\"urn:unqualified\""));
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("implementation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Actual_loaded_Graph_template_cannot_gain_BpmnProcess_authority_from_its_publication_type_label()
    {
        using var fixture = new GraphFixture(graphClaimingProcess: true);
        var result = await fixture.Compile(Process());
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("BpmnProcess", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Actual_native_template_descriptor_schema_must_match_the_qualified_execution_contract()
    {
        using var fixture = new GraphFixture(badDescriptorSchema: true);
        var result = await fixture.Compile(Process());
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("BpmnProcess", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class GraphFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ActivityTemplateCompiler _compiler;
        private readonly ActivityDefinition _definition = new() { Id = "graph-definition", ActivityTypeKey = "Test.CompleteSource", Category = "BPMN" };

        internal GraphFixture(bool graphClaimingProcess = false, bool badDescriptorSchema = false)
        {
            var registry = TestWellKnownTypeRegistry.Create();
            registry.RegisterType(typeof(BpmnProcess), TypeAliasConvention.CanonicalAlias(typeof(BpmnProcess)));
            var services = new ServiceCollection();
            services.AddSingleton<IWellKnownTypeRegistry>(registry);
            services.AddSingleton(TimeProvider.System);
            new ActivitiesBpmnDesignFeature().ConfigureServices(services);
            new ActivitiesSequenceFeature().ConfigureServices(services);
            new Elsa.Workflows.Publishing.WorkflowsPublishingFeature().ConfigureServices(services);
            _services = services.BuildServiceProvider();
            var structures = new DefaultActivityStructureService(_services.GetServices<IActivityStructureHandler>());
            var nodeCompiler = new ExecutableNodeCompiler(structures, registry, new RuntimeInputBindingCompiler(registry),
                new RuntimeOutputCaptureCompiler(new Elsa.Workflows.Runtime.Services.Values.RuntimeDurableValueStorageDriverRegistry(
                    [new Elsa.Workflows.Runtime.Services.Values.JsonRuntimeDurableValueStorageDriver()])));
            var rows = Versions();
            var templates = rows.Select(row =>
            {
                var native = nodeCompiler.CompileSourceOwnedRoot(row);
                if (graphClaimingProcess && row.Id == "bpmn-version")
                    native = new("native-root", "native-root", "Test.ActualGraphRoot", "1",
                        new RuntimeActivityDescriptor(GraphActivityProvider.RuntimeConsumerKey, "1", JsonSerializer.SerializeToElement(new { entryOccurrenceId = "unrelated" }, Web)),
                        new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(), activityContract: native.ActivityContract);
                if (badDescriptorSchema && row.Id == "bpmn-version")
                    native = new(native.ExecutableNodeId, native.AuthoredActivityId, native.ActivityType, native.ActivityTypeVersion,
                        new RuntimeActivityDescriptor(native.Descriptor.ConsumerKey, "99", native.Descriptor.Payload), native.InputBindings,
                        native.OutputCaptures, native.Metadata, native.ChildSlots, native.Structure, activityContract: native.ActivityContract);
                return new ExecutableActivityTemplate($"template:{row.Id}", $"hash:{row.Id}", native,
                    new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "native-fixture", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
            }).ToArray();
            var publications = rows.Select((row, index) => new ActivityDefinitionVersionPublication
            {
                DefinitionId = row.DefinitionId, DefinitionVersionId = row.Id, ActivityTypeKey = row.Definition!.ActivityTypeKey,
                Version = "1.0.0", TemplateId = templates[index].TemplateId, TemplateHash = templates[index].TemplateHash,
                SourceReferenceId = $"source:{row.Id}", Provider = new(row.ProviderKey, row.ProviderSchemaVersion, JsonSerializer.SerializeToElement(new { }, Web)),
                Contract = NativeContract(row.Id), ProviderFingerprint = "native-fixture", RuntimeRequirements = [], PublishedAt = DateTimeOffset.UnixEpoch,
                ResolutionKind = ActivityDefinitionVersionResolutionKind.AuthorableActivity, DirectDependencyCount = 0, ClosedTemplateCount = 0
            }).ToArray();
            var provider = new GraphActivityProvider(structures, new ActivityContractAuthoringValidator(new Capabilities()));
            _compiler = ActivatorUtilities.CreateInstance<ActivityTemplateCompiler>(_services,
                new ActivityTemplateProviderCompilerRegistry([provider]), new ActivityTemplateDependencyDiscovererRegistry([provider]),
                new Publications(publications), new FakeActivityVersionStore(rows.ToList()), new Templates(templates),
                new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System);
        }

        internal ValueTask<ActivityTemplateCompilerResult> Compile(Elsa.Workflows.Design.Core.Models.ActivityNode root)
        {
            var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
            {
                rootActivity = root, variables = Array.Empty<object>(), outputMappings = Array.Empty<object>()
            }, Web));
            var candidate = new ActivityTemplateCandidate("ActivityDraft", "graph-draft", 1,
                new("1", [], [], [new("done", "Done", true)]), manifest);
            return _compiler.CompileAsync(new(_definition, candidate, "graph-version", "1.0.0", 0));
        }
        public void Dispose() => _services.Dispose();
    }

    private static DesignContract NativeContract(string version) => new("1",
        version == "bpmn-version" ? [new(nameof(BpmnProcess.CanStartWorkflow), nameof(BpmnProcess.CanStartWorkflow), new("Boolean"), false, false, null, "elsa.json")] : [],
        [], [new("done", "Done", true)]);

    private sealed class Capabilities : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; } = new[] { "Boolean", "Int32" }.Select(alias =>
            new ActivityContractTypeCapability(alias, alias, "Tests", "text", new HashSet<CollectionKind> { CollectionKind.Single }, false, true, new HashSet<string> { "elsa.json" })).ToArray();
    }

    private sealed class Publications(ActivityDefinitionVersionPublication[] publications) : IActivityDefinitionVersionPublicationStore
    {
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string version, CancellationToken cancellationToken = default) => Task.FromResult(publications.SingleOrDefault(publication => publication.DefinitionVersionId == version));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definition, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(publications.Where(publication => publication.DefinitionId == definition).ToArray());
    }

    private sealed class Templates(ExecutableActivityTemplate[] templates) : IExecutableActivityTemplateReader
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(templates.SingleOrDefault(template => template.TemplateId == id));
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) => ValueTask.FromResult(templates.SingleOrDefault(template => template.TemplateHash == hash));
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, templates, template => template.TemplateId, "activity-template"));
    }
}
