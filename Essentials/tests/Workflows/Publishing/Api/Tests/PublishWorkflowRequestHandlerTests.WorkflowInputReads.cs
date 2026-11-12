using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A workflow-level <c>Input</c> read names a declared workflow input by its reference key. A workflow's request is
/// keyed by each input's name, so publication lowers the read to the request read of that name, on ordinary inputs
/// and on an intrinsic's value alike. A reference key that names no declared input is refused before any write.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    // The reference key and the name differ, as they do for an input Studio has renamed.
    private static readonly InputDefinition CustomerInput =
        new("customer-ref", "customer", new TypeReference("String"), null, "Customer", null, IsNullable: false);

    public static TheoryData<string> InputReadPayloads => new() { "stored", "in-process" };

    [Theory]
    [MemberData(nameof(InputReadPayloads))]
    public async Task A_workflow_level_Input_read_publishes_as_the_request_read_of_the_inputs_name(string payload)
    {
        // Studio stores { referenceKey }; an in-process caller may hand over the reference itself.
        var read = new WorkflowArgumentState("Text", payload == "stored"
            ? new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref" }), "Input")
            : new ArgumentValue(new VariableReference("customer-ref"), "Input"), null, null, null, null);
        var version = InputReadVersion(Node("consumer", read));

        var publication = await Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.WorkflowRequest.Path);
        Assert.Null(binding.Expression);
    }

    [Fact]
    public async Task A_workflow_level_Input_read_of_an_undeclared_input_is_refused_before_any_write()
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "missing" }), "Input"),
            null, null, null, null);
        var version = InputReadVersion(Node("consumer", read));
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ActivityDataSelectionRejectedException>(exception.InnerException);
        Assert.Contains(refusal.Findings, finding => finding.Path == "consumer/inputs/Text" && finding.Type == "ActivityData/UnknownInput");
        Assert.Equal(before, await CountPublishWritesAsync());
    }

    [Fact]
    public async Task An_intrinsic_value_bound_to_a_workflow_level_Input_read_is_lowered_too()
    {
        var set = new ActivityNode("set", "elsa.intrinsic.set@1",
            [new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value,
                new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref" }), "Input"), null, null, null, null)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("String"), new VariableReference("target", "workflow"))
        };
        var version = InputReadVersion(SequenceNode("sequence", [set]), [new VariableDefinition("target", "Target", new TypeReference("String"), null, null)]);

        var publication = await Handler(version, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.Set)
            .InputBindings[WorkflowIntrinsicInputKeys.Value];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
    }

    private static WorkflowDefinitionVersion InputReadVersion(ActivityNode root, VariableDefinition[]? variables = null) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState(variables ?? [], root, [CustomerInput], [], null)
        };
}
