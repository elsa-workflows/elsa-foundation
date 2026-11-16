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
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Result member contracts in a reusable body are pinned at placement using the body occurrence identity;
/// producer remapping and conversion linking preserve that selected contract.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    [Fact]
    public async Task A_member_read_in_a_placed_reusable_body_is_published_known_with_its_producer_remapped()
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
        Assert.Equal(RuntimeReadSelectionMode.Known, binding.Selection.Mode);
        Assert.Equal(new ValueTypeDescriptor("String"), binding.Selection.SelectedType);
        Assert.False(binding.Selection.MayBeAbsent);
        Assert.False(binding.Selection.MayBeNull);
        Assert.Equal("elsa.activity-value-json/1", binding.Selection.ProfileRevision);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    [Fact]
    public async Task An_explicit_conversion_on_a_known_member_in_a_placed_reusable_body_is_linked()
    {
        var executable = await PublishPlacedBodyAsync(BodyRead(new AuthoredValueConversionRequest(AuthoredValueConversionMode.None)));
        var binding = Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.InputBindings.TryGetValue("Text", out var text) && text.Source == RuntimeInputBindingSource.ActivityResultMember).InputBindings["Text"];

        Assert.Equal(RuntimeReadSelectionMode.Known, binding.Selection!.Mode);
        Assert.Equal(ValueConversionMode.None, binding.ConversionPlan!.Mode);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan.Operation);
        Assert.Null(binding.ConversionRequest);
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

    /// <summary>
    /// An input member read inside a reusable body reads the boundary's own input: placement lowers it to the request
    /// member read of the boundary input's reference key, with the member path unchanged, and publishes it deferred.
    /// </summary>
    [Fact]
    public async Task An_input_member_read_in_a_placed_reusable_body_is_published_as_the_boundary_request_member_read()
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            referenceKey = "customer", memberPath = new[] { "address", "a.b", "" }
        }), "InputMember"), null, null, null, null);

        var executable = await PublishPlacedBodyAsync(read, declaresCustomerInput: true);

        var member = Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.InputBindings.TryGetValue("Text", out var text) && text.Source == RuntimeInputBindingSource.WorkflowRequestMember).InputBindings["Text"];
        Assert.Equal("customer", member.WorkflowRequest!.MemberKey);
        Assert.Null(member.WorkflowRequest.Path);
        Assert.Equal(["address", "a.b", ""], member.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, member.Selection.Mode);
        Assert.Null(member.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetOutput_is_published_with_its_enclosing_output_contract_and_placed_in_its_authored_slot(bool root)
    {
        var executable = await PublishPlacedBodyAsync(Text("ready"), assignOutput: true, rootSetter: root);
        var boundary = executable.RootActivity.DescendantsAndSelf().Single(x => x.Descriptor.ConsumerKey == WellKnownRuntimeActivityConsumers.GraphActivity);
        var setter = Assert.Single(boundary.DescendantsAndSelf(), x => x.IntrinsicKind == WorkflowIntrinsicKind.SetOutput);
        Assert.Equal("output-key", setter.Descriptor.Payload.GetProperty("referenceKey").GetString());
        Assert.Equal("String", setter.InputBindings["value"].TargetType.Alias);
        Assert.Equal("2", setter.Descriptor.SchemaVersion);
        Assert.Contains(new RuntimeRequirement("intrinsic", "2"), executable.RuntimeRequirements);
        var entry = Assert.Single(Assert.Single(boundary.ChildSlots).Activities);
        Assert.Equal(entry.ExecutableNodeId, boundary.Descriptor.Payload.GetProperty("entryNodeId").GetString());
        if (root)
            Assert.Same(setter, entry);
        else
            Assert.Equal(setter.ExecutableNodeId, entry.Structure!.Payload.GetProperty("activities")[1].GetString());
    }

    [Fact]
    public async Task Placed_SetOutput_preserves_explicit_JavaScript_argument_names_and_activity_input_reference_keys()
    {
        var sum = new ExpressionDefinition("JavaScript", "args.A + args.B", new TypeReference("Double"),
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["A"] = new WorkflowRequestExpressionParameterBinding("a"),
                ["B"] = new WorkflowRequestExpressionParameterBinding("b")
            }, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);
        var value = new WorkflowArgumentState("value", new ArgumentValue(
            JsonSerializer.SerializeToElement(sum, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "JavaScript"), null, null, null, null);
        var executable = await PublishPlacedBodyAsync(value, assignOutput: true, rootSetter: true, sumInputs: true);

        var placed = executable.RootActivity.DescendantsAndSelf().Single(x => x.IntrinsicKind == WorkflowIntrinsicKind.SetOutput);
        var binding = placed.InputBindings["value"];
        Assert.Equal("Double", binding.TargetType.Alias);
        Assert.Equal("args.A + args.B", binding.Expression!.Expression);
        Assert.Equal("a", Assert.IsType<WorkflowRequestExpressionParameterBinding>(binding.Expression.Parameters["A"]).MemberKey);
        Assert.Equal("b", Assert.IsType<WorkflowRequestExpressionParameterBinding>(binding.Expression.Parameters["B"]).MemberKey);
    }

    [Fact]
    public async Task A_placed_reusable_body_retains_original_contract_after_same_version_metadata_drift()
    {
        var shape = new CustomerIdShape();
        var calls = 0;
        var executable = await PublishPlacedBodyAsync(BodyRead(null), shapeSource: shape, beforePlacement: template =>
        {
            var overlay = Assert.Single(template.DirectDependencies, dependency => dependency.OccurrenceId == "reader").OccurrenceOverlay!;
            Assert.Equal("2", overlay.SchemaVersion);
            Assert.Equal("String", Assert.Single(overlay.MemberReads!).Value.Selection.SelectedType!.Alias);
            Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), template.RuntimeRequirements);
            Assert.Equal(template.TemplateHash, ExecutableActivityTemplateBehaviorHasher.Compute(template.Root, template.ResumeTargets,
                template.DirectDependencies, template.ClosedTemplates, template.RuntimeRequirements, template.StorageDriverRequirements,
                template.ProviderFingerprint, template.CompatibilityMetadata));
            calls = shape.Calls;
            shape.Alias = "Int32";
            shape.Profile = "unavailable/new-profile";
            shape.RefuseQueries = true;
        });
        var selection = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.InputBindings.Values.Any(
            binding => binding.Source == RuntimeInputBindingSource.ActivityResultMember)).InputBindings["Text"].Selection!;
        Assert.Equal("String", selection.SelectedType!.Alias);
        Assert.Equal("elsa.activity-value-json/1", selection.ProfileRevision);
        Assert.Equal(calls, shape.Calls);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    public async Task Repeated_and_nested_placed_reusable_body_occurrences_keep_their_own_known_sources(bool nested, int expected)
    {
        var executable = await PublishPlacedBodyAsync(BodyRead(null), copies: 2, nested: nested);
        var reads = executable.RootActivity.DescendantsAndSelf().Where(node => node.InputBindings.Values.Any(
            binding => binding.Source == RuntimeInputBindingSource.ActivityResultMember)).ToArray();
        Assert.Equal(expected, reads.Length);
        Assert.Equal(expected, reads.Select(node => node.InputBindings["Text"].ActivityResult!.ProducerExecutableNodeId).Distinct().Count());
        foreach (var reader in reads)
        {
            var producerId = reader.InputBindings["Text"].ActivityResult!.ProducerExecutableNodeId;
            var parent = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.ChildSlots.SelectMany(slot => slot.Activities)
                .Any(child => child.ExecutableNodeId == reader.ExecutableNodeId));
            Assert.Contains(parent.ChildSlots.SelectMany(slot => slot.Activities), node => node.ExecutableNodeId == producerId);
            Assert.Equal(RuntimeReadSelectionMode.Known, reader.InputBindings["Text"].Selection!.Mode);
            Assert.NotEqual("reader", reader.AuthoredActivityId);
        }
    }

    [Theory]
    [InlineData(ActivitySerializedShapeState.Unknown)]
    [InlineData(ActivitySerializedShapeState.Truncated)]
    [InlineData(ActivitySerializedShapeState.Unavailable)]
    public async Task An_untyped_result_in_a_placed_reusable_body_keeps_legacy_deferred_behavior(ActivitySerializedShapeState state)
    {
        var executable = await PublishPlacedBodyAsync(BodyRead(null), shapeSource: new CustomerIdShape { State = state }, beforePlacement: template =>
        {
            var overlay = Assert.Single(template.DirectDependencies, dependency => dependency.OccurrenceId == "reader").OccurrenceOverlay!;
            Assert.Equal("1", overlay.SchemaVersion);
            Assert.Null(overlay.MemberReads);
        });
        var binding = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.InputBindings.Values.Any(
            input => input.Source == RuntimeInputBindingSource.ActivityResultMember)).InputBindings["Text"];
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection!.Mode);
        Assert.Null(binding.ConversionPlan);
    }

    [Fact]
    public async Task A_known_incompatible_member_in_a_placed_reusable_body_refuses_before_all_publication_writes()
    {
        await PublishPlacedBodyAsync(BodyRead(null)); // Positive controls for all four stores.
        async Task<string> State() => JsonSerializer.Serialize(new
        {
            Executables = await _store.ListAllAsync(), References = await _referenceStore.ListAllAsync(),
            Publications = await _publicationStore.ListBySlotAsync(DefaultSlotId), Activation = await _activationAuthority.FindAsync("definition-1", "default")
        });
        var before = await State();
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            PublishPlacedBodyAsync(BodyRead(null), shapeSource: new CustomerIdShape { Alias = "Int32" }));
        var refusal = Assert.IsType<ValueConversionPublicationException>(error.InnerException);
        Assert.NotEqual(ValueConversionRejectionReason.SelectedMemberContractUnknown, refusal.ReasonCode);
        Assert.Equal(before, await State());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_local_SetOutput_member_is_pinned_in_its_original_template_and_placed_with_its_public_contract(bool explicitNone)
    {
        var executable = await PublishPlacedBodyAsync(BodyRead(explicitNone
            ? new AuthoredValueConversionRequest(AuthoredValueConversionMode.None) : null), assignOutput: true,
            beforePlacement: template =>
            {
                var originalSetter = Assert.Single(template.Root.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput);
                var originalBinding = originalSetter.InputBindings["value"];
                Assert.Equal(RuntimeReadSelectionMode.Known, originalBinding.Selection!.Mode);
                Assert.Equal(new ValueTypeDescriptor("String"), originalBinding.Selection.SelectedType);
                Assert.Equal(["customerId"], originalBinding.Selection.Segments);
                Assert.Equal("producer", originalBinding.ActivityResult!.ProducerExecutableNodeId);
                Assert.DoesNotContain(template.DirectDependencies, dependency => dependency.OccurrenceId == "assign");
                Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), template.RuntimeRequirements);
                Assert.Contains(new RuntimeRequirement("intrinsic", "2"), template.RuntimeRequirements);
                Assert.Equal(template.TemplateHash, ExecutableActivityTemplateBehaviorHasher.Compute(template.Root, template.ResumeTargets,
                    template.DirectDependencies, template.ClosedTemplates, template.RuntimeRequirements, template.StorageDriverRequirements,
                    template.ProviderFingerprint, template.CompatibilityMetadata));
            });

        var producer = Assert.Single(executable.RootActivity.DescendantsAndSelf(),
            node => node.ActivityContract?.ActivityTypeKey == CustomerProducerVersion.Definition.ActivityTypeKey);
        var setter = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput);
        var binding = setter.InputBindings["value"];
        Assert.Equal(RuntimeReadSelectionMode.Known, binding.Selection!.Mode);
        Assert.Equal(new ValueTypeDescriptor("String"), binding.Selection.SelectedType);
        Assert.Equal(["customerId"], binding.Selection.Segments);
        Assert.Equal(producer.ExecutableNodeId, binding.ActivityResult!.ProducerExecutableNodeId);
        Assert.NotEqual("producer", binding.ActivityResult.ProducerExecutableNodeId);
        Assert.Null(binding.ActivityResult.ProducerScopeId);
        Assert.Equal(explicitNone ? ValueConversionMode.None : ValueConversionMode.Auto, binding.ConversionPlan!.Mode);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan.Operation);
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.Equal("output-key", setter.Descriptor.Payload.GetProperty("referenceKey").GetString());
        Assert.Equal("2", setter.Descriptor.SchemaVersion);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
        Assert.Contains(new RuntimeRequirement("intrinsic", "2"), executable.RuntimeRequirements);
    }

    [Fact]
    public async Task A_local_member_requirement_is_included_before_the_original_template_hash_is_computed()
    {
        await PublishPlacedBodyAsync(BodyRead(null), assignOutput: true, beforePlacement: template =>
        {
            Assert.DoesNotContain(template.DirectDependencies, dependency => dependency.OccurrenceOverlay?.MemberReads is { Count: > 0 });
            Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), template.RuntimeRequirements);
            Assert.Equal(template.TemplateHash, ExecutableActivityTemplateBehaviorHasher.Compute(template.Root, template.ResumeTargets,
                template.DirectDependencies, template.ClosedTemplates, template.RuntimeRequirements, template.StorageDriverRequirements,
                template.ProviderFingerprint, template.CompatibilityMetadata));
        });
    }

    [Fact]
    public async Task A_known_incompatible_local_SetOutput_member_refuses_before_all_publication_writes()
    {
        await PublishPlacedBodyAsync(BodyRead(null), assignOutput: true);
        async Task<string> State() => JsonSerializer.Serialize(new
        {
            Executables = await _store.ListAllAsync(), References = await _referenceStore.ListAllAsync(),
            Publications = await _publicationStore.ListBySlotAsync(DefaultSlotId), Activation = await _activationAuthority.FindAsync("definition-1", "default")
        });
        var before = await State();
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            PublishPlacedBodyAsync(BodyRead(null), shapeSource: new CustomerIdShape { Alias = "Int32" }, assignOutput: true));
        var refusal = Assert.IsType<ValueConversionPublicationException>(error.InnerException);
        Assert.NotEqual(ValueConversionRejectionReason.SelectedMemberContractUnknown, refusal.ReasonCode);
        Assert.Equal(before, await State());
    }

    private static WorkflowArgumentState BodyRead(AuthoredValueConversionRequest? conversion) =>
        new("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            producerNodeId = "producer", projectionKey = "$result", isOptional = false, memberPath = new[] { "customerId" }
        }), "ActivityResultMember"), null, null, null, null, conversion);

    private static ActivityNode BodySequence(string nodeId, IReadOnlyCollection<ActivityNode> activities) =>
        new(nodeId, "activity-sequence", Inputs: [], Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    // Genuine provider compilation, immutable publication, placement, and the actual publish handler.
    private async Task<WorkflowExecutable> PublishPlacedBodyAsync(WorkflowArgumentState read, object[]? bodyVariables = null,
        bool declaresCustomerInput = false, IActivitySerializedShapeSource? shapeSource = null,
        Action<ExecutableActivityTemplate>? beforePlacement = null, int copies = 1, bool nested = false,
        bool assignOutput = false, bool rootSetter = false, bool sumInputs = false)
    {
        var shape = shapeSource ?? new CustomerIdShape();
        void Shape(IServiceCollection services) => services.AddSingleton<IActivitySerializedShapeSource>(shape);
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerProducer), TypeAliasConvention.CanonicalAlias(typeof(CustomerProducer)));
        var versions = new List<ActivityDefinitionVersion> { _writeLineActivity, _sequenceActivity, CustomerProducerVersion };
        var dependencyVersions = new FakeActivityVersionStore(versions);
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
        using var services = new ServiceCollection().AddScoped<IActivityTemplateInputBindingCompiler>(_ => new RuntimeInputBindingCompiler(registry)).BuildServiceProvider();
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()),
            services.GetRequiredService<IServiceScopeFactory>());
        var frozen = new List<(ExecutableActivityTemplate Template, string Bytes, ActivityProviderManifest Manifest, string Body)>();
        async Task<ActivityDefinitionVersion> PublishReusable(string id, ActivityNode body, DesignContract contract, object[] variables)
        {
            var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
            {
                rootActivity = body, variables, outputMappings = Array.Empty<object>()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
                new ActivityTemplateDependencyDiscovererRegistry([graph]), publications, dependencyVersions, templates,
                new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
                TestCompiler.SourceAuthority(dependencyVersions, _activityStructureService, memberReads: true, Shape), _activityStructureService);
            var compiled = await coordinator.CompileAsync(new(new ActivityDefinition
                { Id = id + "-definition", ActivityTypeKey = id, Category = "Test" },
                new ActivityTemplateCandidate("ActivityDraft", id + "-draft", 1, contract, manifest), id + "-version", "1.0.0", 0));
            Assert.True(compiled.IsSuccessful, string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            var template = compiled.Template!;
            frozen.Add((template, JsonSerializer.Serialize(template), manifest, manifest.Payload.GetRawText()));
            var publication = DefaultPublication(id + "-version", id + "-definition", contract, template, manifest,
                ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary);
            publications.Add(publication);
            templates.Add(template);
            sources.Add(SourceOf(publication, template));
            var version = new ActivityDefinitionVersion("1.0.0", publication.DefinitionId)
            {
                Id = publication.DefinitionVersionId,
                Definition = new ActivityDefinition { Id = publication.DefinitionId, ActivityTypeKey = publication.ActivityTypeKey, Category = "Test" },
                ProviderKey = manifest.ProviderKey, ProviderSchemaVersion = manifest.SchemaVersion,
                ConsumerKey = template.Root.Descriptor.ConsumerKey, ConsumerSchemaVersion = template.Root.Descriptor.SchemaVersion,
                DescriptorPayload = template.Root.Descriptor.Payload, Inputs = []
            };
            versions.Add(version);
            return version;
        }
        var reader = assignOutput
            ? new ActivityNode("assign", "elsa.intrinsic.set-output@1", [Text("PublicResult") with { ReferenceKey = "name" }, read with { ReferenceKey = "value" }], [])
            {
                Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("Elsa.Any"))
            }
            : Node("reader", read);
        var body = rootSetter ? reader : BodySequence("body",
            [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), reader]);
        var contract = new DesignContract("1", sumInputs ? [new("a", "A", new("Double"), false, true, null, "elsa.json"), new("b", "B", new("Double"), false, true, null, "elsa.json")] : declaresCustomerInput ? [new("customer", "Customer", new("String"), false, true, null, "elsa.json")] : [],
            assignOutput ? [new("output-key", "PublicResult", new(sumInputs ? "Double" : "String"), true, false, "elsa.json")] : [], [new("done", "Done", true)]);
        var reusable = await PublishReusable("reusable-member-read", body, contract, bodyVariables ?? []);
        beforePlacement?.Invoke(frozen[0].Template);
        if (nested)
            reusable = await PublishReusable("wrapper", BodySequence("wrapper-body",
                [new ActivityNode("nested-a", reusable.Id, [], []), new ActivityNode("nested-b", reusable.Id, [], [])]),
                new DesignContract("1", [], [], [new("done", "Done", true)]), []);
        var workflow = WorkflowVersion(SequenceNode("workflow", Enumerable.Range(0, copies)
            .Select(index => new ActivityNode("placed-" + index, reusable.Id, [], [])).ToArray()));
        var versionStore = new FakeVersionStore(workflow);
        var compiler = TestCompiler.Create(versionStore, dependencyVersions, _activityStructureService, registry, publications, templates,
            new DefaultSourceReferences([.. sources]),
            dataSelectionAnalyzer: TestCompiler.SourceAuthority(dependencyVersions, _activityStructureService, memberReads: true, Shape));
        var authoredBefore = JsonSerializer.Serialize(workflow.State);
        try
        {
            var published = await Handler(null, compiler, versionStore).Handle(new PublishWorkflow(workflow.Id), CancellationToken.None);
            return Assert.IsType<WorkflowExecutable>(await _store.FindAsync(published.ArtifactId));
        }
        finally
        {
            Assert.Equal(authoredBefore, JsonSerializer.Serialize(workflow.State));
            foreach (var snapshot in frozen)
            {
                Assert.Equal(snapshot.Bytes, JsonSerializer.Serialize(snapshot.Template));
                Assert.Equal(snapshot.Body, snapshot.Manifest.Payload.GetRawText());
            }
        }
    }

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
        public string Alias { get; set; } = "String";
        public string Profile { get; set; } = "elsa.activity-value-json/1";
        public ActivitySerializedShapeState State { get; set; } = ActivitySerializedShapeState.Known;
        public int Calls { get; private set; }
        public bool RefuseQueries { get; set; }
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
        {
            if (admittedVersion.Id != CustomerProducerVersion.Id) return ValueTask.FromResult<ActivitySerializedShape?>(null);
            if (RefuseQueries) throw new InvalidOperationException("Placement must not consult newer body metadata.");
            Calls++;
            return ValueTask.FromResult<ActivitySerializedShape?>(memberPath.SequenceEqual(["customerId"])
                ? new(State, ActivitySerializedValueKind.Scalar, State == ActivitySerializedShapeState.Known ? new(Alias) : null,
                    "contract", Profile, "metadata")
                { IsNullable = false, Presence = ActivitySerializedPresence.Required, Representation = ValueRepresentation.TextValue }
                : new(ActivitySerializedShapeState.Known, ActivitySerializedValueKind.Object, new("Object"), "contract", Profile, "metadata"));
        }

        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
