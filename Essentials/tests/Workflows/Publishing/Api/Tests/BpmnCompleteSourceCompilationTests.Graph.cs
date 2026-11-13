using Elsa.Expressions.Core.Models;
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
using Elsa.Workflows.Runtime.Services.Values;
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
        Assert.NotNull(typeof(ActivityTemplateCompiler).GetConstructor([
            typeof(IActivityTemplateProviderCompilerRegistry), typeof(IActivityTemplateDependencyDiscovererRegistry),
            typeof(IActivityDefinitionVersionPublicationStore), typeof(IActivityDefinitionVersionStore),
            typeof(IExecutableActivityTemplateReader), typeof(IActivityTemplateAdmissionPolicy), typeof(TimeProvider),
            typeof(IEnumerable<Elsa.Workflows.Design.Validations.Core.Contracts.IActivityDataSelectionDocumentProjector>),
            typeof(Elsa.Workflows.Design.Validations.Core.Contracts.IActivityDataSelectionAnalyzer), typeof(IActivityStructureService)
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
        var owner = Process(canStart: false, taskAttributes: " implementation=\"urn:unqualified\"");
        var source = owner.Structure!.Payload.GetRawText();
        var result = await fixture.Compile(owner);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        AssertSanitizedStructureRefusal(result);
        Assert.Equal(source, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_template_coordinator_refuses_complete_source_disguised_as_legacy_for_any_start_flag(bool canStart)
    {
        using var fixture = new GraphFixture();
        var owner = Process(canStart: canStart, taskAttributes: " implementation=\"urn:unqualified\"");
        var source = owner.Structure!.Payload.GetRawText();
        owner = owner with { Structure = new(owner.Structure.Kind, "1.0.0", owner.Structure.Payload) };
        var result = await fixture.Compile(owner);
        Assert.False(result.IsSuccessful); Assert.Null(result.Template);
        var failure = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "activity.provider.failure");
        Assert.Equal(GraphActivityProvider.Key, Assert.IsType<ActivityDiagnosticLocation>(failure.Location).ProviderKey);
        Assert.Equal("discover-dependencies", failure.Metadata["operation"]);
        Assert.Equal("1", failure.Metadata["schemaVersion"]);
        Assert.DoesNotContain("documentXml", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_template_coordinator_compiles_native_body_variable_frames_inside_complete_source(bool standardScope)
    {
        using var fixture = new GraphFixture();
        var owner = Process(canStart: true, nestedStandardScope: standardScope);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(owner.Structure!.Payload.GetRawText())!.AsObject();
        var native = payload["activities"]!.AsArray()[0]!.AsObject();
        native["structure"]!["payload"]!["variables"] = JsonSerializer.SerializeToNode(new[]
        {
            new VariableDefinition("native-local", "Native local", new("Int32"), null, new ArgumentValue("42", "Literal"))
        }, Web);
        owner = owner with { Structure = new(owner.Structure.Kind, owner.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Web)) };
        var result = await fixture.Compile(owner);
        Assert.True(result.IsSuccessful, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var dependency = Assert.Single(result.Template!.DirectDependencies, dependency => dependency.OccurrenceId == "work");
        var overlay = Assert.IsType<ExecutableActivityTemplateOccurrenceOverlay>(dependency.OccurrenceOverlay);
        var structure = Assert.IsType<ExecutableActivityStructure>(overlay.Structure);
        var variable = Assert.Single(structure.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(Web)!);
        Assert.Equal("native-local", variable.VariableKey);
        Assert.Equal(42, variable.InitialBinding?.Literal?.InlineValue?.GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Actual_placement_retains_normalized_process_scope_and_native_body_variables_with_authored_children(bool standardScope, bool flowchartBody)
    {
        using var fixture = new GraphFixture();
        var owner = Process(canStart: true, nestedStandardScope: standardScope);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(owner.Structure!.Payload.GetRawText())!.AsObject();
        payload["variables"] = JsonSerializer.SerializeToNode(new[]
        { new VariableDefinition("process-local", "Process local", new("Int32"), null, new ArgumentValue("7", "Literal")) }, Web);
        var native = payload["activities"]![0]!["structure"]!["payload"]!;
        native["activities"] = JsonSerializer.SerializeToNode(new[] { Sequence("nested-work") }, Web);
        if (flowchartBody)
        {
            payload["activities"]![0]!["activityVersionId"] = "flowchart-version";
            payload["activities"]![0]!["structure"]!["kind"] = Elsa.Activities.Flowchart.Activities.Flowchart.StructureKind;
            native["connections"] = new System.Text.Json.Nodes.JsonArray();
            native["startNodeId"] = "nested-work";
        }
        native["variables"] = JsonSerializer.SerializeToNode(new[]
        { new VariableDefinition("native-local", "Native local", new("Int32"), null, new ArgumentValue("42", "Literal")) }, Web);
        owner = owner with { Structure = new(owner.Structure.Kind, owner.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Web)) };
        var before = owner.Structure.Payload.Clone();
        var result = await fixture.Compile(owner);
        Assert.True(result.IsSuccessful, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var placed = await fixture.Place(result.Template!);
        var process = PlacedOccurrence(placed, "process");
        var work = PlacedOccurrence(placed, "work");
        var child = PlacedOccurrence(placed, "nested-work");
        AssertCanonical(process, "process", "process-local", 7);
        AssertCanonical(work, "work", "native-local", 42);
        Assert.Equal(child.ExecutableNodeId, flowchartBody ? work.Structure!.Payload.GetProperty("startNodeId").GetString()
            : Assert.Single(work.Structure!.Payload.GetProperty("activities").EnumerateArray()).GetString());
        var taskScope = process;
        if (standardScope)
        {
            taskScope = PlacedOccurrence(placed, "scope-sub");
            AssertCanonical(taskScope, "scope-sub", "header-value", 42);
            Assert.Equal(taskScope.ExecutableNodeId, ElementChild(process, "Sub"));
        }
        Assert.Equal(work.ExecutableNodeId, ElementChild(taskScope, "T"));
        Assert.True(JsonElement.DeepEquals(before, owner.Structure.Payload));

        void AssertCanonical(ExecutableNode node, string occurrence, string key, int expected)
        {
            var overlay = Assert.Single(result.Template!.DirectDependencies, dependency => dependency.OccurrenceId == occurrence).OccurrenceOverlay!;
            Assert.True(JsonElement.DeepEquals(overlay.Structure!.Payload.GetProperty("variables"), node.Structure!.Payload.GetProperty("variables")));
            var projector = new RuntimeVariableDeclarationProjector();
            var declaration = Assert.Single(projector.ProjectDeclarations(node)).Value;
            Assert.Equal(key, declaration.VariableKey);
            Assert.Equal("Int32", declaration.Type.Alias);
            Assert.NotNull(declaration.Policy);
            Assert.Equal(expected, projector.ProjectInitialValues(node)[key].InlineValue!.Value.GetInt32());
        }

        static string? ElementChild(ExecutableNode node, string id) => node.Structure!.Payload.GetProperty("elements").EnumerateArray()
            .Single(element => element.GetProperty("elementId").GetString() == id).GetProperty("childNodeId").GetString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_template_coordinator_preserves_authored_variable_overlays_outside_translated_subtrees(bool translatedSibling)
    {
        using var fixture = new GraphFixture();
        var owner = Sequence("legacy", Sequence("legacy-leaf"));
        var payload = System.Text.Json.Nodes.JsonNode.Parse(owner.Structure!.Payload.GetRawText())!.AsObject();
        payload["variables"] = JsonSerializer.SerializeToNode(new[]
        {
            new VariableDefinition("legacy-local", "Legacy local", new("Int32"), null, new ArgumentValue("42", "Literal"))
        }, Web);
        owner = owner with { Structure = new(owner.Structure.Kind, owner.Structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, Web)) };

        var result = await fixture.Compile(translatedSibling ? Sequence("outer", Process(), owner) : owner);

        Assert.True(result.IsSuccessful, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var dependency = Assert.Single(result.Template!.DirectDependencies, dependency => dependency.OccurrenceId == "legacy");
        var overlay = Assert.IsType<ExecutableActivityTemplateOccurrenceOverlay>(dependency.OccurrenceOverlay);
        var structure = Assert.IsType<ExecutableActivityStructure>(overlay.Structure);
        var variables = structure.Payload.GetProperty("variables");
        Assert.True(JsonElement.DeepEquals(owner.Structure.Payload.GetProperty("variables"), variables));
        var variable = Assert.Single(variables.EnumerateArray());
        Assert.Equal("legacy-local", variable.GetProperty("referenceKey").GetString());
        Assert.False(variable.TryGetProperty("variableKey", out _));
        Assert.False(variable.TryGetProperty("initialBinding", out _));
        var placed = await fixture.Place(result.Template);
        var legacy = PlacedOccurrence(placed, "legacy");
        var legacyChild = PlacedOccurrence(placed, "legacy-leaf");
        Assert.True(JsonElement.DeepEquals(variables, legacy.Structure!.Payload.GetProperty("variables")));
        Assert.Equal(legacyChild.ExecutableNodeId, Assert.Single(legacy.Structure.Payload.GetProperty("activities").EnumerateArray()).GetString());
        if (translatedSibling)
        {
            var process = Assert.Single(result.Template.DirectDependencies, dependency => dependency.OccurrenceId == "process");
            Assert.Equal("1.0.0", Assert.IsType<ExecutableActivityStructure>(process.OccurrenceOverlay!.Structure).SchemaVersion);
        }
    }

    [Fact]
    public async Task Actual_loaded_Graph_template_cannot_gain_BpmnProcess_authority_from_its_publication_type_label()
    {
        using var fixture = new GraphFixture(graphClaimingProcess: true);
        var owner = Process();
        var source = owner.Structure!.Payload.GetRawText();
        var result = await fixture.Compile(owner);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        AssertSanitizedStructureRefusal(result);
        Assert.Equal(source, owner.Structure.Payload.GetRawText());
    }

    [Fact]
    public async Task Actual_native_template_descriptor_schema_must_match_the_qualified_execution_contract()
    {
        using var fixture = new GraphFixture(badDescriptorSchema: true);
        var owner = Process();
        var source = owner.Structure!.Payload.GetRawText();
        var result = await fixture.Compile(owner);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Template);
        AssertSanitizedStructureRefusal(result);
        Assert.Equal(source, owner.Structure.Payload.GetRawText());
    }

    private static void AssertSanitizedStructureRefusal(ActivityTemplateCompilerResult result)
    {
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "activity.graph.structure-compilation-refused");
        var location = Assert.IsType<ActivityDiagnosticLocation>(diagnostic.Location);
        Assert.Equal(ActivityDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(GraphActivityProvider.Key, location.ProviderKey);
        Assert.Null(location.JsonPointer);
        Assert.Equal("ActivityDefinition", diagnostic.Subject.Kind);
        Assert.Equal(GraphActivityProvider.Key, diagnostic.Subject.Id);
        Assert.Equal("The activity provider reported an issue.", diagnostic.Message);
        Assert.Empty(diagnostic.Metadata);
    }

    private static ExecutableNode PlacedOccurrence(ActivityTemplatePlacement placed, string id) =>
        Assert.Single(placed.Root.DescendantsAndSelf(), node => JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(
            node.Metadata["activity.invocationOrigin"], Web)!.LastOrDefault(segment => segment.Kind == ActivityInvocationOriginSegmentKind.NestedPlacement)?.Id == id);

    private sealed class GraphFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ActivityTemplateCompiler _compiler;
        private readonly ActivityTemplatePlacer _placer;
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
            new Elsa.Activities.Flowchart.ActivitiesFlowchartFeature().ConfigureServices(services);
            new Elsa.Workflows.Publishing.WorkflowsPublishingFeature().ConfigureServices(services);
            _services = services.BuildServiceProvider();
            var structures = new DefaultActivityStructureService(_services.GetServices<IActivityStructureHandler>());
            var inputs = new RuntimeInputBindingCompiler(registry);
            var outputs = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
            var nodeCompiler = new ExecutableNodeCompiler(structures, registry, inputs, outputs);
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
            var actualProcess = Assert.Single(templates, template => template.TemplateId == "template:bpmn-version").Root;
            Assert.Equal(graphClaimingProcess ? "Test.ActualGraphRoot" : typeof(BpmnProcess).FullName, actualProcess.ActivityType);
            Assert.Equal(graphClaimingProcess ? GraphActivityProvider.RuntimeConsumerKey : WellKnownRuntimeActivityConsumers.ClrActivity, actualProcess.Descriptor.ConsumerKey);
            Assert.Equal(badDescriptorSchema ? "99" : RuntimeActivityDescriptor.InitialSchemaVersion, actualProcess.Descriptor.SchemaVersion);
            var publications = rows.Select((row, index) => new ActivityDefinitionVersionPublication
            {
                DefinitionId = row.DefinitionId, DefinitionVersionId = row.Id, ActivityTypeKey = row.Definition!.ActivityTypeKey,
                Version = "1.0.0", TemplateId = templates[index].TemplateId, TemplateHash = templates[index].TemplateHash,
                SourceReferenceId = $"source:{row.Id}", Provider = new(row.ProviderKey, row.ProviderSchemaVersion, JsonSerializer.SerializeToElement(new { }, Web)),
                Contract = NativeContract(row.Id), ProviderFingerprint = "native-fixture", RuntimeRequirements = [], PublishedAt = DateTimeOffset.UnixEpoch,
                ResolutionKind = ActivityDefinitionVersionResolutionKind.AuthorableActivity, DirectDependencyCount = 0, ClosedTemplateCount = 0
            }).ToArray();
            var provider = new GraphActivityProvider(structures, new ActivityContractAuthoringValidator(new Capabilities()));
            var versions = new FakeActivityVersionStore(rows.ToList());
            var publicationStore = new Publications(publications);
            var templateReader = new Templates(templates);
            _compiler = new ActivityTemplateCompiler(
                new ActivityTemplateProviderCompilerRegistry([provider]), new ActivityTemplateDependencyDiscovererRegistry([provider]),
                publicationStore, versions, templateReader,
                new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [provider],
                TestCompiler.SourceAuthority(versions, structures), structures,
                _services.GetRequiredService<IExecutableStructureNormalizer>());
            _placer = new(publicationStore, templateReader, new SourceReferences(publications.Select(Reference).ToArray()),
                new Sha256ActivityPlacementHasher(), structures, inputs, outputs);
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

        internal ValueTask<ActivityTemplatePlacement> Place(ExecutableActivityTemplate template)
        {
            var publication = new ActivityDefinitionVersionPublication
            {
                DefinitionId = _definition.Id, DefinitionVersionId = "graph-version", Version = "1.0.0", ActivityTypeKey = _definition.ActivityTypeKey,
                TemplateId = template.TemplateId, TemplateHash = template.TemplateHash, SourceReferenceId = "source:graph-version",
                Contract = new("1", [], [], [new("done", "Done", true)]),
                Provider = new(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new { }, Web)),
                ProviderFingerprint = "graph-fixture", RuntimeRequirements = [], PublishedAt = DateTimeOffset.UnixEpoch,
                Lifecycle = ActivityDefinitionVersionLifecycle.Active
            };
            return _placer.PlaceAsync(new(publication, template, Reference(publication),
                new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-version")]), publication.ActivityTypeKey,
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>()));
        }
        public void Dispose() => _services.Dispose();
    }

    private static WorkflowExecutableSourceReference Reference(ActivityDefinitionVersionPublication publication) => new(
        publication.SourceReferenceId, publication.TemplateId, "ActivityDefinitionVersion", publication.DefinitionVersionId,
        publication.Version, publication.DefinitionId, publication.DefinitionVersionId, publication.Version,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WorkflowExecutableReferenceScope.Published, LayoutSidecar: ExecutableLayoutSidecar.Empty);

    private sealed class SourceReferences(WorkflowExecutableSourceReference[] references) : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(references.SingleOrDefault(reference => reference.SourceReferenceId == id));
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, references.Where(reference => reference.ArtifactId == query.ArtifactId), reference => reference.SourceReferenceId, $"source-reference:artifact:{query.ArtifactId}"));
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(query, references.Where(reference => query.Scope is null || reference.Scope == query.Scope)
                .Where(reference => !query.LiveOnly || reference.IsLive(query.Now!.Value)), reference => reference.SourceReferenceId, TestRuntimeStorePages.SourceReferenceQueryBinding(query)));
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.UnreferencedArtifactIds(candidates, references, now));
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
