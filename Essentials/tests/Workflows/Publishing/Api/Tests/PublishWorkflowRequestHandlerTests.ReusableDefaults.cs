using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Published_reusable_defaults_cannot_read_a_future_or_missing_producer_before_workflow_writes(bool portable, bool missing)
    {
        var fixture = await ReusableDefaultAsync(ResultDefault(portable, missing ? "missing" : "later"));
        var before = await CountPublishWritesAsync();
        var authoredBefore = JsonSerializer.Serialize(fixture.Version.State);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "consumer/inputs/value" &&
            finding.Type == "ValueFlow/UnavailableProducer");
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
        Assert.Equal(authoredBefore, JsonSerializer.Serialize(fixture.Version.State));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_reusable_defaults_accept_a_causally_earlier_typed_result_without_mutating_authored_inputs(bool portable)
    {
        var fixture = await ReusableDefaultAsync(ResultDefault(portable, "earlier"));
        var authoredBefore = JsonSerializer.Serialize(fixture.Version.State);
        var publication = await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);
        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var consumer = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "consumer");
        var binding = consumer.InputBindings["value"];
        Assert.Equal(portable ? RuntimeInputBindingSource.Expression : RuntimeInputBindingSource.ActivityResult, binding.Source);
        Assert.Equal("earlier", portable
            ? Assert.IsType<ActivityResultExpressionParameterBinding>(binding.Expression!.Parameters["p"]).ProducerNodeId
            : binding.ActivityResult!.ProducerExecutableNodeId);
        Assert.Equal(authoredBefore, JsonSerializer.Serialize(fixture.Version.State));
    }

    [Theory]
    [InlineData("Literal", false)]
    [InlineData("Object", false)]
    [InlineData("Literal", true)]
    [InlineData("JavaScript", false)]
    public async Task Published_reusable_literal_object_null_and_expression_defaults_remain_supported(string syntax, bool nullValue)
    {
        var fixture = await ReusableDefaultAsync(new(syntax,
            JsonSerializer.SerializeToElement(nullValue ? null : syntax == "Object" ? JsonSerializer.Serialize("hello") : "hello")), nullable: nullValue);
        var publication = await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);
        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var consumer = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "consumer");
        var binding = consumer.InputBindings["value"];
        Assert.Equal(syntax == "JavaScript" ? RuntimeInputBindingSource.Expression : RuntimeInputBindingSource.Literal, binding.Source);
        if (syntax == "JavaScript") Assert.Equal("hello", binding.Expression!.Expression);
        else if (nullValue) Assert.Equal(JsonValueKind.Null, binding.LiteralValue!.Value.ValueKind);
        else Assert.Equal("hello", binding.LiteralValue!.Value.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_authored_result_overrides_an_unavailable_reusable_default(bool portable)
    {
        var value = ResultDefault(portable, "earlier");
        var fixture = await ReusableDefaultAsync(ResultDefault(portable, "later"),
            new("value", new(value.Value, value.Syntax), null, null, null, null));
        var publication = await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);
        Assert.NotNull(await _store.FindAsync(publication.ArtifactId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Flowchart_owner_rebuild_preserves_causal_admission_of_reusable_defaults(bool portable, bool earlier)
    {
        var fixture = await ReusableDefaultAsync(ResultDefault(portable, earlier ? "earlier" : "later"), flowchart: true);
        var before = await CountPublishWritesAsync();
        var authoredBefore = JsonSerializer.Serialize(fixture.Version.State);
        if (earlier)
        {
            var publication = await fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None);
            Assert.NotNull(await _store.FindAsync(publication.ArtifactId));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
                fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None));
            var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
            Assert.Contains(refusal.Findings, finding => finding.Path == "consumer/inputs/value" && finding.Type == "ValueFlow/UnavailableProducer");
            Assert.Equal(before, await CountPublishWritesAsync());
            Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
        }
        Assert.Equal(authoredBefore, JsonSerializer.Serialize(fixture.Version.State));
    }

    [Fact]
    public async Task Reusable_defaults_still_participate_in_whole_node_binding_admission()
    {
        var fixture = await ReusableDefaultAsync(new("Secret", JsonSerializer.SerializeToElement(new { name = "example" })));
        var before = await CountPublishWritesAsync();
        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            fixture.Handler.Handle(new PublishWorkflow(fixture.Version.Id), CancellationToken.None));
        Assert.Equal(SecretBindingDiagnostics.NonClrConsumerRefused("consumer", "value", GraphActivityProvider.RuntimeConsumerKey).Message, exception.Message);
        Assert.Equal(before, await CountPublishWritesAsync());
    }

    private static ActivityInputDefault ResultDefault(bool portable, string producer) => portable
        ? new("JavaScript", JsonSerializer.SerializeToElement(new ExpressionDefinition("JavaScript", "p", new("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["p"] = new ActivityResultExpressionParameterBinding(producer, "$result") },
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1)))
        : new("ActivityResult", JsonSerializer.SerializeToElement(new { producerNodeId = producer, projectionKey = "$result", producerScopeId = "workflow" }));

    // The public default first passes the actual Graph provider and template coordinator. Only then is
    // that exact publication placed in a native Sequence and published through the real shared analyzer.
    private async Task<(Elsa.Workflows.Publishing.Handlers.PublishWorkflowRequestHandler Handler,
        Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionVersion Version)> ReusableDefaultAsync(
        ActivityInputDefault value, WorkflowArgumentState? authored = null, bool nullable = false, bool flowchart = false)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(DefaultResultProducer), TypeAliasConvention.CanonicalAlias(typeof(DefaultResultProducer)));
        var producer = SecretBindingCompilerFixture.ClrActivityVersion(typeof(DefaultResultProducer));
        var bodyVersionStore = new FakeActivityVersionStore([_writeLineActivity]);
        var bodyExecutable = await TestCompiler.Create(new FakeVersionStore(WorkflowVersion(Node("body", Text("body")))),
            bodyVersionStore, _activityStructureService, registry).CompileAsync(new("version-1", WorkflowExecutableReferenceScope.Published,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "body-"));
        var bodyHash = ExecutableActivityTemplateBehaviorHasher.Compute(bodyExecutable.RootActivity, bodyExecutable.ResumeTargets,
            [], [], bodyExecutable.RuntimeRequirements, bodyExecutable.StorageDriverRequirements, "body", new Dictionary<string, string>());
        var bodyTemplate = new ExecutableActivityTemplate("body-template", bodyHash, bodyExecutable.RootActivity,
            bodyExecutable.ResumeTargets, [], [], bodyExecutable.RuntimeRequirements, "body", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch,
            bodyExecutable.StorageDriverRequirements);
        var bodyContract = new DesignContract("1", [new("Text", "Text", new("String"), false, true, null, "elsa.json")], [], []);
        var bodyPublication = DefaultPublication(_writeLineActivity.Id, _writeLineActivity.DefinitionId, bodyContract, bodyTemplate,
            new("clr", "1", JsonSerializer.SerializeToElement(new { })), ActivityDefinitionVersionResolutionKind.AuthorableActivity);
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()));
        var contract = new DesignContract("1", [new("value", "Value", new("String"), true, nullable, value, "elsa.json")], [], [new("done", "Done", true)]);
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
        {
            rootActivity = Node("body", Text("body")), variables = Array.Empty<object>(), outputMappings = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.DoesNotContain(await graph.ValidateAsync(manifest, contract), diagnostic => diagnostic.Severity == ActivityDiagnosticSeverity.Error);
        var publications = new DefaultPublications(bodyPublication);
        var templates = new DefaultTemplates(bodyTemplate);
        var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
            new ActivityTemplateDependencyDiscovererRegistry([graph]), publications, bodyVersionStore, templates,
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
            TestCompiler.SourceAuthority(bodyVersionStore, _activityStructureService), _activityStructureService);
        var compiled = await coordinator.CompileAsync(new(new ActivityDefinition
            { Id = "reusable-definition", ActivityTypeKey = "reusable-default", Category = "Test" },
            new ActivityTemplateCandidate("ActivityDraft", "reusable-draft", 1, contract, manifest), "reusable-version", "1.0.0", 0));
        Assert.True(compiled.IsSuccessful, string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var template = Assert.IsType<ExecutableActivityTemplate>(compiled.Template);
        var publication = DefaultPublication("reusable-version", "reusable-definition", contract, template, manifest,
            ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary);
        publications.Add(publication);
        templates.Add(template);
        var reusableVersion = new ActivityDefinitionVersion("1.0.0", publication.DefinitionId)
        {
            Id = publication.DefinitionVersionId,
            Definition = new ActivityDefinition { Id = publication.DefinitionId, ActivityTypeKey = publication.ActivityTypeKey, Category = "Test" },
            ProviderKey = manifest.ProviderKey, ProviderSchemaVersion = manifest.SchemaVersion,
            ConsumerKey = template.Root.Descriptor.ConsumerKey, ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
            DescriptorPayload = template.Root.Descriptor.Payload,
            Inputs = contract.Inputs.Select(input => new InputDefinition(input.ReferenceKey, input.Name, input.Type,
                input.StorageDriverKey, input.Name, null, input.IsNullable, IsRequired: input.IsRequired,
                DefaultValue: input.Default?.Value, DefaultSyntax: input.Default?.Syntax)).ToArray()
        };
        var consumer = new ActivityNode("consumer", publication.DefinitionVersionId, authored is null ? [] : [authored], []);
        ActivityNode earlier = new("earlier", producer.Id, [], []), later = new("later", producer.Id, [], []);
        var root = flowchart
            ? FlowchartNode("flowchart", [later, consumer, earlier],
                [new(new("earlier", "Done"), new("consumer", null)), new(new("consumer", "done"), new("later", null))], "earlier")
            : SequenceNode("sequence", [earlier, consumer, later]);
        var version = WorkflowVersion(root);
        var versions = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity, _flowchartActivity, producer, reusableVersion]);
        static WorkflowExecutableSourceReference Source(ActivityDefinitionVersionPublication item, ExecutableActivityTemplate itemTemplate) =>
            new(item.SourceReferenceId, itemTemplate.TemplateId, "ActivityDefinitionVersion", item.DefinitionVersionId, item.Version,
                item.DefinitionId, item.DefinitionVersionId, item.Version, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                WorkflowExecutableReferenceScope.Published);
        var versionStore = new FakeVersionStore(version);
        var compiler = TestCompiler.Create(versionStore, versions, _activityStructureService, registry,
            publications, templates, new DefaultSourceReferences(Source(publication, template), Source(bodyPublication, bodyTemplate)));
        return (Handler(null, compiler, versionStore), version);
    }

    private static ActivityDefinitionVersionPublication DefaultPublication(string versionId, string definitionId, DesignContract contract,
        ExecutableActivityTemplate template, ActivityProviderManifest manifest, ActivityDefinitionVersionResolutionKind kind) => new()
    {
        Id = versionId, DefinitionVersionId = versionId, DefinitionId = definitionId, Version = "1.0.0",
        ActivityTypeKey = versionId, ResolutionKind = kind, Contract = contract, Provider = manifest,
        TemplateId = template.TemplateId, TemplateHash = template.TemplateHash, SourceReferenceId = versionId + "-source",
        ProviderFingerprint = template.ProviderFingerprint, DirectDependencyCount = template.DirectDependencies.Count,
        ClosedTemplateCount = template.ClosedTemplates.Count,
        RuntimeRequirements = template.RuntimeRequirements.Select(requirement => new ActivityRuntimeRequirementDeclaration(requirement.ConsumerKey, requirement.SchemaVersion)).ToList(),
        Lifecycle = ActivityDefinitionVersionLifecycle.Active
    };

    private sealed class DefaultResultProducer : Activity<string>
    {
        protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete("produced"));
    }

    private sealed class DefaultContractCapabilities : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; } = [new("String", "String", "Test", "Test",
            Enum.GetValues<CollectionKind>().ToHashSet(), true, true, new HashSet<string>(StringComparer.Ordinal) { "elsa.json" })];
    }

    private sealed class DefaultPublications(params ActivityDefinitionVersionPublication[] values) : IActivityDefinitionVersionPublicationStore
    {
        private readonly Dictionary<string, ActivityDefinitionVersionPublication> _items = values.ToDictionary(item => item.DefinitionVersionId);
        public void Add(ActivityDefinitionVersionPublication item) => _items.Add(item.DefinitionVersionId, item);
        public Task<ActivityDefinitionVersionPublication?> FindAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(_items.Values.Where(item => item.DefinitionId == id).ToArray());
    }

    private sealed class DefaultTemplates(params ExecutableActivityTemplate[] values) : IExecutableActivityTemplateReader
    {
        private readonly Dictionary<string, ExecutableActivityTemplate> _items = values.ToDictionary(item => item.TemplateId);
        public void Add(ExecutableActivityTemplate item) => _items.Add(item.TemplateId, item);
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.GetValueOrDefault(id));
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.Values.SingleOrDefault(item => item.TemplateHash == hash));
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRuntimeStorePages.Page(request, _items.Values, item => item.TemplateId, "default-proof"));
    }

    private sealed class DefaultSourceReferences(params WorkflowExecutableSourceReference[] sources) : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult(sources.SingleOrDefault(source => source.SourceReferenceId == id));
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
