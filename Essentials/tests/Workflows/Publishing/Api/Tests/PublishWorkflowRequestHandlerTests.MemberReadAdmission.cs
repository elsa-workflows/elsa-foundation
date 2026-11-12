using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// How executable-producing paths treat member reads. A member read of an activity result publishes only where the
/// host turns member reads on, and then exactly where the whole read of the same result would; every other member
/// kind, and a member path on a whole-value kind, is refused before any write whatever the setting.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    /// <summary>The member-read shapes refused with member reads off, with the finding reported.</summary>
    public static TheoryData<string, string> UnsupportedMemberReads => new()
    {
        { "ActivityResultMember", "ActivityData/UnsupportedMemberBinding" },
        { "VariableMember", "ActivityData/UnsupportedMemberBinding" },
        { "WorkflowRequestMember", "ActivityData/UnsupportedMemberBinding" },
        { "InputMember", "ActivityData/UnsupportedMemberBinding" },
        { "ActivityResult+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "ActivityResult+path", "ActivityData/UnsupportedMemberSelection" },
        { "Variable+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "WorkflowRequest+memberPath", "ActivityData/UnsupportedMemberSelection" }
    };

    /// <summary>The member-read shapes still refused with member reads on, with the finding reported.</summary>
    public static TheoryData<string, string> RefusedWithMemberReadsOn => new()
    {
        { "VariableMember+noScope", "ActivityData/InvalidSource" },
        { "VariableMember+unknown", "Variable/UnresolvableReference" },
        { "WorkflowRequestMember", "ActivityData/UnsupportedMemberBinding" },
        { "InputMember", "ActivityData/UnsupportedMemberBinding" },
        { "ActivityResult+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "ActivityResult+path", "ActivityData/UnsupportedMemberSelection" },
        { "Variable+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "WorkflowRequest+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "ActivityResultMember+scope", "ActivityData/InvalidSource" },
        { "ActivityResultMember+emptyPath", "ActivityData/InvalidMemberPath" },
        { "ActivityResultMember+numberSegment", "ActivityData/InvalidMemberPath" },
        { "ActivityResultMember+tooManySegments", "ActivityData/InvalidMemberPath" },
        { "ActivityResultMember+tooLong", "ActivityData/InvalidMemberPath" }
    };

    internal static WorkflowArgumentState MemberRead(string shape, string inputKey = "Text", string producer = "producer")
    {
        string[] path = ["customerId"];
        var (kind, payload) = shape switch
        {
            "ActivityResultMember" => ("ActivityResultMember", (object)new { producerNodeId = producer, projectionKey = "$result", memberPath = path }),
            "ActivityResultMember+scope" => ("ActivityResultMember", new { producerNodeId = producer, projectionKey = "$result", producerScopeId = "scope:root", memberPath = path }),
            "ActivityResultMember+emptyPath" => ("ActivityResultMember", new { producerNodeId = producer, projectionKey = "$result", memberPath = Array.Empty<string>() }),
            "ActivityResultMember+numberSegment" => ("ActivityResultMember", new { producerNodeId = producer, projectionKey = "$result", memberPath = new object[] { "a", 1 } }),
            "ActivityResultMember+tooManySegments" => ("ActivityResultMember", new { producerNodeId = producer, projectionKey = "$result", memberPath = Enumerable.Repeat("a", 65).ToArray() }),
            "ActivityResultMember+tooLong" => ("ActivityResultMember", new { producerNodeId = producer, projectionKey = "$result", memberPath = new[] { new string('a', 4097) } }),
            "VariableMember" => ("VariableMember", new { referenceKey = "customer", declaringScopeId = "workflow", memberPath = path }),
            "VariableMember+noScope" => ("VariableMember", new { referenceKey = "customer", memberPath = path }),
            "VariableMember+unknown" => ("VariableMember", new { referenceKey = "missing", declaringScopeId = "workflow", memberPath = path }),
            "WorkflowRequestMember" => ("WorkflowRequestMember", new { memberKey = "customer", memberPath = path }),
            "InputMember" => ("InputMember", new { referenceKey = "customer", memberPath = path }),
            "ActivityResult+memberPath" => ("ActivityResult", new { producerNodeId = producer, projectionKey = "$result", memberPath = path }),
            "ActivityResult+path" => ("ActivityResult", new { producerNodeId = producer, projectionKey = "$result", path = "customerId" }),
            "Variable+memberPath" => ("Variable", new { referenceKey = "customer", declaringScopeId = "workflow", memberPath = path }),
            "WorkflowRequest+memberPath" => ("WorkflowRequest", new { memberKey = "customer", memberPath = path }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };
        return new(inputKey, new ArgumentValue(JsonSerializer.SerializeToElement(payload), kind), null, null, null, null);
    }

    [Theory]
    [MemberData(nameof(UnsupportedMemberReads))]
    public async Task Workflow_publication_refuses_a_member_read_in_the_shared_analyzer_before_any_write(string shape, string finding)
    {
        var version = WorkflowVersion(SequenceNode("sequence", [Node("producer", Text("hello")), Node("consumer", MemberRead(shape))]));
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(version, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
        Assert.Contains(refusal.Findings, item => item.Path == "consumer/inputs/Text" && item.Type == finding);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
    }

    [Theory]
    [MemberData(nameof(RefusedWithMemberReadsOn))]
    public async Task With_member_reads_on_every_other_member_read_is_still_refused_before_any_write(string shape, string finding)
    {
        var version = WorkflowVersion(SequenceNode("sequence", [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), Node("consumer", MemberRead(shape))]));
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
        Assert.Contains(refusal.Findings, item => item.Path == "consumer/inputs/Text" && item.Type == finding);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
    }

    /// <summary>
    /// With member reads on, a member read of a result publishes as a result member read with its exact segments and
    /// no scope, and the executable requires the engine to select members.
    /// </summary>
    [Fact]
    public async Task With_member_reads_on_a_result_member_read_publishes_with_its_selection_and_requirement()
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new
        {
            producerNodeId = "producer", projectionKey = "$result", isOptional = false, memberPath = new[] { "address", "a.b", "" }
        }), "ActivityResultMember"), null, null, null, null);
        var version = WorkflowVersion(SequenceNode("sequence", [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), Node("consumer", read)]));

        var publication = await MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var consumer = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.AuthoredActivityId == "consumer");
        var binding = consumer.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, binding.Source);
        Assert.Equal(["address", "a.b", ""], binding.Selection!.Segments);
        Assert.Equal("producer", binding.ActivityResult!.ProducerExecutableNodeId);
        Assert.Null(binding.ActivityResult.ProducerScopeId);
        Assert.Equal("$result", binding.ActivityResult.ProjectionKey);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    /// <summary>
    /// A member read is admitted exactly where the whole read of the same result is: on a Flowchart cycle, where a
    /// scope-free whole read is refused as an earlier producer run, the member read is refused for the same reason.
    /// </summary>
    [Fact]
    public async Task With_member_reads_on_a_result_member_read_is_refused_where_the_whole_read_is()
    {
        List<FlowchartConnection> connections =
        [
            new(new FlowchartEndpoint("start", "Done"), new FlowchartEndpoint("producer", null)),
            new(new FlowchartEndpoint("producer", "Done"), new FlowchartEndpoint("consumer", null)),
            new(new FlowchartEndpoint("consumer", "Done"), new FlowchartEndpoint("start", null))
        ];
        var version = WorkflowVersion(FlowchartNode("flowchart",
            [Node("start", Text("start")), new ActivityNode("producer", CustomerProducerVersion.Id, [], []), Node("consumer", MemberRead("ActivityResultMember"))],
            connections, "start"));
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            MemberReadHandler(version, _flowchartActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "consumer/inputs/Text" && finding.Type == "ValueFlow/EarlierProducerRun");
        Assert.Equal(before, await CountPublishWritesAsync());
    }

    /// <summary>
    /// With member reads on, a member read of a workflow variable publishes as a deferred variable member read in the
    /// variable's explicit scope, with no plan, and the executable requires the engine to select members.
    /// </summary>
    [Fact]
    public async Task With_member_reads_on_a_variable_member_read_publishes_deferred_with_its_requirement()
    {
        var root = Node("consumer", MemberRead("VariableMember"));
        var version = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([new VariableDefinition("customer", "Customer", new TypeReference("Object"), null, null)], root, [], [], null)
        };

        var publication = await MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.VariableMember, binding.Source);
        Assert.Equal(("customer", "workflow"), (binding.Variable!.VariableKey, binding.Variable.DeclaringScopeId));
        Assert.Equal(["customerId"], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Null(binding.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    [Fact]
    public void The_binding_compiler_compiles_a_variable_member_read_as_deferred_in_its_explicit_scope()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var definition = new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false);

        var member = compiler.Compile("consumer", definition, MemberRead("VariableMember"));
        var noScope = Assert.Throws<ActivityDataSelectionRejectedException>(() => compiler.Compile("consumer", definition, MemberRead("VariableMember+noScope")));

        Assert.Equal(RuntimeInputBindingSource.VariableMember, member.Source);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, member.Selection!.Mode);
        Assert.Equal(["customerId"], member.Selection.Segments);
        Assert.Equal(("customer", "workflow"), (member.Variable!.VariableKey, member.Variable.DeclaringScopeId));
        Assert.Null(member.ConversionPlan);
        Assert.Equal(("consumer/inputs/Text", "ActivityData/InvalidSource"), (Assert.Single(noScope.Findings).Path, noScope.Findings[0].Type));
    }

    /// <summary>
    /// A variable member is never typed at publication, so an explicit conversion on its read cannot be planned and is
    /// refused; the default conversion is left to the runtime's fit check.
    /// </summary>
    [Theory]
    [InlineData(AuthoredValueConversionMode.Json)]
    [InlineData(AuthoredValueConversionMode.None)]
    public void The_binding_compiler_refuses_an_explicit_conversion_on_a_variable_member_read(AuthoredValueConversionMode mode)
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var definition = new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false);

        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            compiler.Compile("consumer", definition, MemberRead("VariableMember") with { Conversion = new AuthoredValueConversionRequest(mode) }));
        var automatic = compiler.Compile("consumer", definition, MemberRead("VariableMember") with { Conversion = new AuthoredValueConversionRequest() });

        Assert.Equal(ValueConversionRejectionReason.SelectedMemberContractUnknown, exception.ReasonCode);
        Assert.Null(automatic.ConversionPlan);
    }

    /// <summary>
    /// The binding compiler refuses a member path on a whole-value kind itself, and compiles a result member read to a
    /// deferred selection, before the analysis has typed the member.
    /// </summary>
    [Fact]
    public void The_binding_compiler_compiles_a_result_member_read_as_deferred_and_refuses_a_path_on_a_whole_value_read()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var definition = new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false);

        var member = compiler.Compile("consumer", definition, MemberRead("ActivityResultMember"));
        var refusal = Assert.Throws<ActivityDataSelectionRejectedException>(() => compiler.Compile("consumer", definition, MemberRead("ActivityResult+memberPath")));

        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, member.Source);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, member.Selection!.Mode);
        Assert.Equal(["customerId"], member.Selection.Segments);
        Assert.Null(member.ConversionPlan);
        Assert.Equal(("consumer/inputs/Text", "ActivityData/UnsupportedMemberSelection"), (Assert.Single(refusal.Findings).Path, refusal.Findings[0].Type));
    }

    /// <summary>
    /// A reusable activity body is compiled by the template coordinator through the actual Graph provider, which runs
    /// the same analyzer on the provider's own document, so a member read inside a body is refused at template
    /// compilation while member reads are off. Activity-draft test runs compile through this coordinator before they
    /// save an executable.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnsupportedMemberReads))]
    public async Task Reusable_activity_template_compilation_refuses_a_member_read_in_its_body(string shape, string finding)
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            var compiled = await CompileReusableBodyAsync(Node("consumer", MemberRead(shape)), memberReads: false);
            Assert.Fail($"Template compilation was expected to refuse '{shape}', but returned success={compiled.IsSuccessful}: " +
                string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        });

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception);
        Assert.Contains(refusal.Findings, item => item.Path == "consumer/inputs/Text" && item.Type == finding);
    }

    /// <summary>
    /// With member reads on, the template coordinator's analysis admits a result member read in a body. This fixture
    /// publishes none of the body's dependencies, so compilation stops after the analysis, at dependency resolution.
    /// </summary>
    [Fact]
    public async Task With_member_reads_on_a_reusable_activity_body_admits_a_result_member_read()
    {
        // A graph manifest is read with web defaults, so the body's structure payload is written with them too.
        var body = new ActivityNode("body", "activity-sequence", [], [], new ActivityNodeStructure(
            SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(
                [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), Node("consumer", MemberRead("ActivityResultMember"))]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var compiled = await CompileReusableBodyAsync(body, memberReads: true);

        Assert.All(compiled.Diagnostics, diagnostic => Assert.Equal("activity.dependency.version-not-found", diagnostic.Code));
    }

    private async Task<ActivityTemplateCompilerResult> CompileReusableBodyAsync(ActivityNode root, bool memberReads)
    {
        var bodyVersionStore = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity, CustomerProducerVersion]);
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()));
        var contract = new DesignContract("1", [new("customer", "Customer", new("String"), false, true, null, "elsa.json")], [], [new("done", "Done", true)]);
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
        {
            rootActivity = root,
            variables = Array.Empty<object>(),
            outputMappings = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
            new ActivityTemplateDependencyDiscovererRegistry([graph]), new DefaultPublications(), bodyVersionStore, new DefaultTemplates(),
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
            TestCompiler.SourceAuthority(bodyVersionStore, _activityStructureService, memberReads), _activityStructureService);
        return await coordinator.CompileAsync(new(new ActivityDefinition
            { Id = "reusable-definition", ActivityTypeKey = "reusable-member-read", Category = "Test" },
            new ActivityTemplateCandidate("ActivityDraft", "reusable-draft", 1, contract, manifest), "reusable-version", "1.0.0", 0));
    }

    private PublishWorkflowRequestHandler MemberReadHandler(WorkflowDefinitionVersion version, params ActivityDefinitionVersion[] extra)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(CustomerProducer), TypeAliasConvention.CanonicalAlias(typeof(CustomerProducer)));
        var versionStore = new FakeVersionStore(version);
        var activities = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity, CustomerProducerVersion, .. extra]);
        return Handler(null, TestCompiler.Create(versionStore, activities, _activityStructureService, registry,
            dataSelectionAnalyzer: TestCompiler.SourceAuthority(activities, _activityStructureService, memberReads: true)), versionStore);
    }

    private static readonly ActivityDefinitionVersion CustomerProducerVersion = SecretBindingCompilerFixture.ClrActivityVersion(typeof(CustomerProducer));

    public sealed record CustomerAddress(string Street);

    public sealed record CustomerRecord(string CustomerId, CustomerAddress Address);

    /// <summary>Produces a customer whose serialized members a reader selects from.</summary>
    public sealed class CustomerProducer : Activity<CustomerRecord>
    {
        protected override ValueTask<ActivityTransition<CustomerRecord>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new CustomerRecord("c-1", new CustomerAddress("Main"))));
    }

}
