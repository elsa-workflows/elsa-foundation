using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A result member read between two nodes of a reusable activity's body is published deferred, even where the
/// producer's shape types the member: the body is compiled only when it is placed, after the template's analysis, and
/// the workflow's analysis does not look inside a placed body. Placement remaps the producer and keeps the segments;
/// the runtime checks the member's fit when it reads it. An explicit conversion on such a read is refused.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    [Fact]
    public async Task A_member_read_in_a_placed_reusable_body_is_published_deferred_with_its_producer_remapped()
    {
        var executable = await PublishPlacedBodyAsync(BodyRead(conversion: null));

        var placedProducer = Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.ActivityContract?.ActivityTypeKey == CustomerProducerVersion.Definition.ActivityTypeKey);
        var placedReader = Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.InputBindings.TryGetValue("Text", out var text) && text.Source == RuntimeInputBindingSource.ActivityResultMember);
        var binding = placedReader.InputBindings["Text"];
        Assert.Equal(placedProducer.ExecutableNodeId, binding.ActivityResult!.ProducerExecutableNodeId);
        Assert.NotEqual("producer", binding.ActivityResult.ProducerExecutableNodeId);
        Assert.Null(binding.ActivityResult.ProducerScopeId);
        Assert.Equal(["customerId"], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Null(binding.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    [Fact]
    public async Task An_explicit_conversion_on_a_member_read_in_a_placed_reusable_body_is_refused()
    {
        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            PublishPlacedBodyAsync(BodyRead(new AuthoredValueConversionRequest(AuthoredValueConversionMode.Json))));

        var refusal = Assert.IsType<ValueConversionPublicationException>(exception.InnerException);
        Assert.Equal(ValueConversionRejectionReason.SelectedMemberContractUnknown, refusal.ReasonCode);
    }

    /// <summary>
    /// A member read of a graph variable inside a reusable body is placed with the variable's scope moved to the
    /// placed graph's scope, as a whole-value read of it is, and is published deferred with its segments.
    /// </summary>
    [Fact]
    public async Task A_variable_member_read_in_a_placed_reusable_body_is_published_with_its_scope_remapped()
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            referenceKey = "total", declaringScopeId = "activity-graph", memberPath = new[] { "customerId" }
        }), "VariableMember"), null, null, null, null);
        var whole = new WorkflowArgumentState("Text", new ArgumentValue(new Elsa.Expressions.Core.Models.VariableReference("total", "activity-graph"), "Variable"),
            null, null, null, null);
        object[] variables = [new { referenceKey = "total", name = "Total", type = new { alias = "String", collectionKind = "Single" }, storageDriverKey = "elsa.json" }];

        var memberExecutable = await PublishPlacedBodyAsync(read, variables);
        var wholeExecutable = await PublishPlacedBodyAsync(whole, variables);

        RuntimeInputBinding Reader(WorkflowExecutable executable) => Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.InputBindings.TryGetValue("Text", out var text) && text.Variable is not null).InputBindings["Text"];
        var member = Reader(memberExecutable);
        var placedScope = Reader(wholeExecutable).Variable!.DeclaringScopeId;
        Assert.Equal(RuntimeInputBindingSource.VariableMember, member.Source);
        Assert.NotEqual("activity-graph", placedScope);
        Assert.Equal(("total", placedScope), (member.Variable!.VariableKey, member.Variable.DeclaringScopeId));
        Assert.Equal(["customerId"], member.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, member.Selection.Mode);
        Assert.Null(member.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), memberExecutable.RuntimeRequirements);
    }

    private static WorkflowArgumentState BodyRead(AuthoredValueConversionRequest? conversion) =>
        new("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            producerNodeId = "producer", projectionKey = "$result", isOptional = false, memberPath = new[] { "customerId" }
        }), "ActivityResultMember"), null, null, null, null, conversion);

    // Compiles a reusable activity whose body produces a customer and reads its typed customerId, places it in a
    // workflow and publishes that workflow, with member reads on in both analyses.
    private async Task<WorkflowExecutable> PublishPlacedBodyAsync(WorkflowArgumentState read, object[]? bodyVariables = null)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerProducer), TypeAliasConvention.CanonicalAlias(typeof(CustomerProducer)));
        var dependencyVersions = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity, CustomerProducerVersion]);
        var publications = new DefaultPublications();
        var templates = new DefaultTemplates();
        var sources = new List<WorkflowExecutableSourceReference>();
        foreach (var (dependency, root) in new[]
                 {
                     (_writeLineActivity, Node("write", Text("body"))),
                     (CustomerProducerVersion, new ActivityNode("produce", CustomerProducerVersion.Id, [], [])),
                     (_sequenceActivity, SequenceNode("steps", [Node("write", Text("body"))]))
                 })
            await PublishAuthorableAsync(dependency, root, dependencyVersions, registry, publications, templates, sources);

        // The body: a producer and a reader of one of its members, in a Sequence.
        var body = new ActivityNode("body", _sequenceActivity.Id, [], [], new ActivityNodeStructure(
            SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(
                [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), Node("reader", read)]), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()));
        var contract = new DesignContract("1", [], [], [new("done", "Done", true)]);
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
        {
            rootActivity = body, variables = bodyVariables ?? [], outputMappings = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
            new ActivityTemplateDependencyDiscovererRegistry([graph]), publications, dependencyVersions, templates,
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
            TestCompiler.SourceAuthority(dependencyVersions, _activityStructureService, memberReads: true, TypedCustomerId), _activityStructureService);
        var compiled = await coordinator.CompileAsync(new(new ActivityDefinition
            { Id = "reusable-definition", ActivityTypeKey = "reusable-member-read", Category = "Test" },
            new ActivityTemplateCandidate("ActivityDraft", "reusable-draft", 1, contract, manifest), "reusable-version", "1.0.0", 0));
        Assert.True(compiled.IsSuccessful, string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var template = compiled.Template!;

        // Place the reusable activity in a workflow and publish that.
        var publication = DefaultPublication("reusable-version", "reusable-definition", contract, template, manifest,
            ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary);
        publications.Add(publication);
        templates.Add(template);
        sources.Add(SourceOf(publication, template));
        var reusableVersion = new ActivityDefinitionVersion("1.0.0", publication.DefinitionId)
        {
            Id = publication.DefinitionVersionId,
            Definition = new ActivityDefinition { Id = publication.DefinitionId, ActivityTypeKey = publication.ActivityTypeKey, Category = "Test" },
            ProviderKey = manifest.ProviderKey, ProviderSchemaVersion = manifest.SchemaVersion,
            ConsumerKey = template.Root.Descriptor.ConsumerKey, ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
            DescriptorPayload = template.Root.Descriptor.Payload, Inputs = []
        };
        var version = WorkflowVersion(SequenceNode("workflow", [new ActivityNode("placed", reusableVersion.Id, [], [])]));
        var versionStore = new FakeVersionStore(version);
        var workflowVersions = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity, CustomerProducerVersion, reusableVersion]);
        var compiler = TestCompiler.Create(versionStore, workflowVersions, _activityStructureService, registry, publications, templates,
            new DefaultSourceReferences([.. sources]),
            dataSelectionAnalyzer: TestCompiler.SourceAuthority(workflowVersions, _activityStructureService, memberReads: true, TypedCustomerId));

        var published = await Handler(null, compiler, versionStore).Handle(new PublishWorkflow(version.Id), CancellationToken.None);
        return Assert.IsType<WorkflowExecutable>(await _store.FindAsync(published.ArtifactId));
    }

    private static void TypedCustomerId(IServiceCollection services) =>
        services.AddSingleton<IActivitySerializedShapeSource>(new CustomerIdShape());

    // Compiles a one-node workflow of the dependency and publishes it as an authorable activity a body can place.
    private async Task PublishAuthorableAsync(ActivityDefinitionVersion dependency, ActivityNode root, FakeActivityVersionStore versions,
        IWellKnownTypeRegistry registry, DefaultPublications publications, DefaultTemplates templates, List<WorkflowExecutableSourceReference> sources)
    {
        var executable = await TestCompiler.Create(new FakeVersionStore(WorkflowVersion(root)), versions, _activityStructureService, registry)
            .CompileAsync(new("version-1", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, dependency.Id + "-"));
        var hash = ExecutableActivityTemplateBehaviorHasher.Compute(executable.RootActivity, executable.ResumeTargets,
            [], [], executable.RuntimeRequirements, executable.StorageDriverRequirements, dependency.Id, new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate(dependency.Id + "-template", hash, executable.RootActivity, executable.ResumeTargets, [], [],
            executable.RuntimeRequirements, dependency.Id, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch, executable.StorageDriverRequirements);
        var contract = new DesignContract("1", dependency.Inputs.Select(input =>
            new Elsa.Activities.Design.Core.Models.ActivityInputContract(input.ReferenceKey, input.Name, input.Type, false, true, null, "elsa.json")).ToArray(), [], []);
        var publication = DefaultPublication(dependency.Id, dependency.DefinitionId, contract, template,
            new("clr", "1", JsonSerializer.SerializeToElement(new { })), ActivityDefinitionVersionResolutionKind.AuthorableActivity);
        publications.Add(publication);
        templates.Add(template);
        sources.Add(SourceOf(publication, template));
    }

    private static WorkflowExecutableSourceReference SourceOf(ActivityDefinitionVersionPublication publication, ExecutableActivityTemplate template) =>
        new(publication.SourceReferenceId, template.TemplateId, "ActivityDefinitionVersion", publication.DefinitionVersionId, publication.Version,
            publication.DefinitionId, publication.DefinitionVersionId, publication.Version, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            WorkflowExecutableReferenceScope.Published);

    /// <summary>Types the producer's <c>customerId</c> member as a required, non-null string.</summary>
    private sealed class CustomerIdShape : IActivitySerializedShapeSource
    {
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(admittedVersion.Id != CustomerProducerVersion.Id
                ? null
                : memberPath.SequenceEqual(["customerId"])
                    ? new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Scalar, new("String"), "contract", "elsa.activity-value-json/1", "metadata")
                    {
                        IsNullable = false, Presence = ActivitySerializedPresence.Required, Representation = ValueRepresentation.TextValue
                    }
                    : new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Object, new("Object"), "contract", "elsa.activity-value-json/1", "metadata"));

        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
