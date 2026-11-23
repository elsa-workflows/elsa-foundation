using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityTemplatePlacementTests
{
    [Fact]
    public async Task Known_native_type_cannot_hide_its_independent_capability_in_a_false_contract_pin()
    {
        var node = await new WorkflowExecutableCompilerTests().NativeStateScriptNodeAsync();
        var original = node.ActivityContract!;
        var contract = new Elsa.Activities.Runtime.Core.Models.ActivityContract(original.ActivityTypeKey, original.ContractVersion,
            original.DescriptorKind, original.DescriptorPayload, original.Inputs.Values, original.Result, original.Outcomes, original.Activation,
            original.SideEffectProfile, original.SupportsBackgroundExecution, original.AccessesUndeclaredVariables, accessesWorkflowState: false);
        var hidden = new ExecutableNode(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion,
            node.Descriptor, node.InputBindings, node.OutputCaptures, node.Metadata, activityContract: contract);
        var (placer, request) = ClosedScopeGraph("open", computeRoot: hidden, registry: WorkflowExecutableCompilerTests.StateScriptTypes());
        await RefusesState(placer, request);
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("[{\"referenceKey\":\"script\",\"value\":{\"expressionType\":\"Input\",\"value\":{\"referenceKey\":\"amount\"}}}]", true)]
    [InlineData("[{\"referenceKey\":\"script\",\"value\":{\"expressionType\":\"Literal\",\"value\":\"function {\"}}]", true)]
    [InlineData("[{\"referenceKey\":\"script\",\"value\":{\"expressionType\":\"Literal\",\"value\":\"return 1;\"}}]", false)]
    public async Task Unknown_occurrence_body_cannot_retain_a_read_only_template_pin(string inputs, bool analyzer)
    {
        var node = await new WorkflowExecutableCompilerTests().EngineStateScriptNodeAsync();
        Assert.Equal("false", node.Metadata["accessesWorkflowState"]);
        var (placer, request) = ClosedScopeGraph("closed", includeGraphContract: true, computeRoot: node, computeInputs: inputs,
            readResolver: analyzer ? WorkflowExecutableCompilerTests.StateReadResolver() : null,
            registry: WorkflowExecutableCompilerTests.StateScriptTypes());
        await RefusesState(placer, request);
    }

    [Theory]
    [InlineData("workflowState.setCorrelationId('new')")]
    [InlineData("workflowState.getOutput('x')")]
    public async Task Nested_occurrence_input_expressions_refuse_state_access_without_variable_reads(string source)
    {
        var inputs = System.Text.Json.JsonSerializer.Serialize(new[] { new { referenceKey = "total", value = new { expressionType = "JavaScript", value = source } } });
        var (placer, request) = ClosedScopeGraph("closed", formatInputs: inputs, readResolver: WorkflowExecutableCompilerTests.StateReadResolver());
        await RefusesState(placer, request);
    }

    [Fact]
    public async Task Native_occurrence_keeps_literal_script_validation_in_addition_to_its_declaration()
    {
        var node = await new WorkflowExecutableCompilerTests().NativeStateScriptNodeAsync();
        var (placer, request) = ClosedScopeGraph("open", computeRoot: node,
            computeInputs: """[{"referenceKey":"script","value":{"expressionType":"Literal","value":"workflowState.setCorrelationId(42);"}}]""",
            readResolver: WorkflowExecutableCompilerTests.StateReadResolver(), registry: WorkflowExecutableCompilerTests.StateScriptTypes());
        await RefusesState(placer, request, "VF-STATE-001");
    }

    [Fact]
    public async Task Open_structural_template_cannot_bypass_state_placement_refusal()
    {
        var node = await new WorkflowExecutableCompilerTests().NativeStateScriptNodeAsync();
        var structure = new ExecutableActivityStructure("elsa.sequence.structure", "1.0.0", Json("""{"activities":[],"variables":[]}"""));
        var structural = new ExecutableNode(node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion,
            node.Descriptor, node.InputBindings, node.OutputCaptures, node.Metadata, structure: structure, activityContract: node.ActivityContract);
        var template = Template("structural", "structural-hash", structural);
        var publication = Publication("structural-definition", "structural-version", "structural-type", template);
        var reference = Reference(publication, ExecutableLayoutSidecar.Empty);
        var placer = CreatePlacer(new PublicationStore([publication]), new TemplateReader([template]), new SourceReader([reference]), new Sha256ActivityPlacementHasher());
        await RefusesState(placer, new(publication, template, reference, ActivityInvocationOrigin.Empty, publication.ActivityTypeKey,
            node.InputBindings, node.OutputCaptures));
    }

    [Fact]
    public async Task Closed_ancestor_remains_closed_through_an_open_intermediate_template()
    {
        var node = await new WorkflowExecutableCompilerTests().NativeStateScriptNodeAsync();
        var leaf = Template("leaf-state", "leaf-state-hash", node);
        var middle = Template("middle", "middle-hash", Node("middle-root"),
            [new("leaf-def", "leaf-version", "1.0.0", leaf.TemplateId, leaf.TemplateHash, "leaf", ActivityInvocationOrigin.Empty)]);
        var closedRoot = new ExecutableNode("closed", "closed", "elsa.graph-activity", "1", new("elsa.graph-activity", "1",
            Json("""{"entryOccurrenceId":"middle","scopeModel":"closed","variables":[]}""")),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        var outer = Template("outer", "outer-hash", closedRoot,
            [new("middle-def", "middle-version", "1.0.0", middle.TemplateId, middle.TemplateHash, "middle", ActivityInvocationOrigin.Empty)]);
        var publications = new[] { Publication("outer-def", "outer-version", "outer-type", outer),
            Publication("middle-def", "middle-version", "middle-type", middle), Publication("leaf-def", "leaf-version", "leaf-type", leaf) };
        var references = publications.Select(item => Reference(item, ExecutableLayoutSidecar.Empty)).ToArray();
        var placer = CreatePlacer(new PublicationStore(publications), new TemplateReader([outer, middle, leaf]), new SourceReader(references), new Sha256ActivityPlacementHasher());
        await RefusesState(placer, new(publications[0], outer, references[0], ActivityInvocationOrigin.Empty, "outer-type",
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>()));
    }

    private static async Task RefusesState(ActivityTemplatePlacer placer, ActivityTemplatePlacementRequest request, string code = "VF-STATE-002")
    {
        var error = await Assert.ThrowsAsync<ActivityDataSelectionRejectedException>(() => placer.PlaceAsync(request).AsTask());
        Assert.Contains(error.Findings, finding => finding.Type == code);
    }
}
