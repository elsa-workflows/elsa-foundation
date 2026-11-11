using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using DesignContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Characterises, on main before #4078, how executable-producing paths treat the reserved member-read kinds and a
/// member path on a whole-value kind. Each path refuses before any write, and the refusal comes from the shared data
/// selection analyzer, not from the binding compiler: the compiler first turns a member kind into an expression whose
/// language is the kind's name. Slice C of #4078 replaces the member-kind rows with real admission.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    /// <summary>The authored member-read shapes nothing on main can compile, with the finding the analyzer reports.</summary>
    public static TheoryData<string, string> UnsupportedMemberReads => new()
    {
        // Slice C flips this row: ActivityResultMember becomes an admitted kind.
        { "ActivityResultMember", "ActivityData/UnsupportedMemberBinding" },
        { "VariableMember", "ActivityData/UnsupportedMemberBinding" },
        { "WorkflowRequestMember", "ActivityData/UnsupportedMemberBinding" },
        { "InputMember", "ActivityData/UnsupportedMemberBinding" },
        { "ActivityResult+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "ActivityResult+path", "ActivityData/UnsupportedMemberSelection" },
        { "Variable+memberPath", "ActivityData/UnsupportedMemberSelection" },
        { "WorkflowRequest+memberPath", "ActivityData/UnsupportedMemberSelection" }
    };

    internal static WorkflowArgumentState MemberRead(string shape, string inputKey = "Text")
    {
        string[] path = ["customerId"];
        var (kind, payload) = shape switch
        {
            "ActivityResultMember" => ("ActivityResultMember", (object)new { producerNodeId = "producer", projectionKey = "$result", memberPath = path }),
            "VariableMember" => ("VariableMember", new { referenceKey = "customer", declaringScopeId = "workflow", memberPath = path }),
            "WorkflowRequestMember" => ("WorkflowRequestMember", new { memberKey = "customer", memberPath = path }),
            "InputMember" => ("InputMember", new { referenceKey = "customer", memberPath = path }),
            "ActivityResult+memberPath" => ("ActivityResult", new { producerNodeId = "producer", projectionKey = "$result", memberPath = path }),
            "ActivityResult+path" => ("ActivityResult", new { producerNodeId = "producer", projectionKey = "$result", path = "customerId" }),
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

    /// <summary>
    /// Before the analyzer runs, the binding compiler accepts a member kind as an expression of that language and
    /// silently drops a member path on a whole-value kind. Only the analyzer stands between these and publication.
    /// </summary>
    [Theory]
    [InlineData("ActivityResultMember")]
    [InlineData("ActivityResult+memberPath")]
    public void The_binding_compiler_alone_does_not_refuse_a_member_read(string shape)
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var definition = new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false);

        var binding = compiler.Compile("consumer", definition, MemberRead(shape));

        if (shape == "ActivityResultMember")
        {
            // Slice C replaces this with a member binding or a refusal.
            Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
            Assert.Equal("ActivityResultMember", binding.Expression!.Language);
            Assert.Contains("memberPath", binding.Expression.Expression, StringComparison.Ordinal);
        }
        else
        {
            // The path is dropped: this compiles to a whole-value read of the producer.
            Assert.Equal(RuntimeInputBindingSource.ActivityResult, binding.Source);
            Assert.Equal("$result", binding.ActivityResult!.ProjectionKey);
            Assert.DoesNotContain("customerId", JsonSerializer.Serialize(binding), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A reusable activity body is compiled by the template coordinator through the actual Graph provider, which runs
    /// the same analyzer on the provider's own document, so a member read inside a body is refused at template
    /// compilation. Activity-draft test runs compile through this coordinator before they save an executable.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnsupportedMemberReads))]
    public async Task Reusable_activity_template_compilation_refuses_a_member_read_in_its_body(string shape, string finding)
    {
        var bodyVersionStore = new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]);
        var graph = new GraphActivityProvider(_activityStructureService, new ActivityContractAuthoringValidator(new DefaultContractCapabilities()));
        var contract = new DesignContract("1", [new("customer", "Customer", new("String"), false, true, null, "elsa.json")], [], [new("done", "Done", true)]);
        var manifest = new ActivityProviderManifest(GraphActivityProvider.Key, "1", JsonSerializer.SerializeToElement(new
        {
            rootActivity = Node("consumer", MemberRead(shape)),
            variables = Array.Empty<object>(),
            outputMappings = Array.Empty<object>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var coordinator = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([graph]),
            new ActivityTemplateDependencyDiscovererRegistry([graph]), new DefaultPublications(), bodyVersionStore, new DefaultTemplates(),
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System, [graph],
            TestCompiler.SourceAuthority(bodyVersionStore, _activityStructureService), _activityStructureService);

        var exception = await Record.ExceptionAsync(async () =>
        {
            var compiled = await coordinator.CompileAsync(new(new ActivityDefinition
                { Id = "reusable-definition", ActivityTypeKey = "reusable-member-read", Category = "Test" },
                new ActivityTemplateCandidate("ActivityDraft", "reusable-draft", 1, contract, manifest), "reusable-version", "1.0.0", 0));
            Assert.Fail($"Template compilation was expected to refuse '{shape}', but returned success={compiled.IsSuccessful}: " +
                string.Join("; ", compiled.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        });

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception);
        Assert.Contains(refusal.Findings, item => item.Path == "consumer/inputs/Text" && item.Type == finding);
    }

    /// <summary>
    /// A workflow-level <c>Input</c> read on an ordinary activity input is what the analyzer offers for a declared
    /// workflow input. Pins what publication does with it on main, which decides where <c>InputMember</c> can appear.
    /// </summary>
    [Fact]
    public async Task A_workflow_level_Input_read_on_an_ordinary_input_publishes_as_an_expression_of_language_Input()
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(
            JsonSerializer.SerializeToElement(new { referenceKey = "customer" }), "Input"), null, null, null, null);
        var version = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], Node("consumer", read),
                [new InputDefinition("customer", "customer", new TypeReference("String"), null, "Customer", null, IsNullable: false)], [], null)
        };

        var publication = await Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings["Text"];
        // Not lowered to a request read: the compiler treats the unknown kind as an expression language. No runtime
        // evaluator is registered for "Input", so this binding cannot resolve at run time (not run here).
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Input", binding.Expression!.Language);
        Assert.Null(binding.WorkflowRequest);
    }
}
