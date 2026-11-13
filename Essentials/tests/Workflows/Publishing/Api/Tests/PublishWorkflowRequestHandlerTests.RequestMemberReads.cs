using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// With member reads on, a member read of a workflow request member publishes as a deferred request member read, and an
/// input member read is lowered to the request member read of the declared input's name with its path unchanged.
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    public static TheoryData<string, string> RequestMemberPayloads => new()
    {
        { "WorkflowRequestMember", """{"memberKey":"customer","memberPath":["address","a.b",""]}""" },
        { "InputMember", """{"referenceKey":"customer-ref","memberPath":["address","a.b",""]}""" }
    };

    [Theory]
    [MemberData(nameof(RequestMemberPayloads))]
    public async Task With_member_reads_on_a_request_or_input_member_read_publishes_as_a_deferred_request_member_read(string role, string payload)
    {
        var read = new WorkflowArgumentState("Text", new ArgumentValue(JsonDocument.Parse(payload).RootElement.Clone(), role), null, null, null, null);
        var version = InputReadVersion(Node("consumer", read));

        var publication = await MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequestMember, binding.Source);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
        Assert.Null(binding.WorkflowRequest.Path);
        Assert.Equal(["address", "a.b", ""], binding.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, binding.Selection.Mode);
        Assert.Null(binding.ConversionPlan);
        Assert.Contains(new RuntimeRequirement(RuntimeReadSelection.RequirementKey, "1"), executable.RuntimeRequirements);
    }

    [Fact]
    public async Task With_member_reads_on_a_request_member_read_of_an_open_request_publishes()
    {
        var version = WorkflowVersion(Node("consumer", MemberRead("WorkflowRequestMember")));

        var publication = await MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var binding = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId)).RootActivity.InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequestMember, binding.Source);
        Assert.Equal("customer", binding.WorkflowRequest!.MemberKey);
    }

    [Fact]
    public async Task An_intrinsic_value_bound_to_an_input_member_read_is_lowered_too()
    {
        var set = new ActivityNode("set", "elsa.intrinsic.set@1",
            [new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, new ArgumentValue(
                JsonSerializer.SerializeToElement(new { referenceKey = "customer-ref", memberPath = new[] { "id" } }), "InputMember"), null, null, null, null)], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("String"), new VariableReference("target", "workflow"))
        };
        var version = InputReadVersion(SequenceNode("sequence", [set]), [new VariableDefinition("target", "Target", new TypeReference("String"), null, null)]);

        var publication = await MemberReadHandler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var binding = Assert.Single(Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId)).RootActivity.DescendantsAndSelf(),
            node => node.IntrinsicKind == WorkflowIntrinsicKind.Set).InputBindings[WorkflowIntrinsicInputKeys.Value];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequestMember, binding.Source);
        Assert.Equal(("customer", "id"), (binding.WorkflowRequest!.MemberKey, binding.Selection!.Segments.Single()));
    }

    [Fact]
    public void The_binding_compiler_compiles_a_request_member_read_as_deferred_and_refuses_an_explicit_conversion()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var definition = new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false);

        var member = compiler.Compile("consumer", definition, MemberRead("WorkflowRequestMember"));
        var converted = Assert.Throws<ValueConversionPublicationException>(() => compiler.Compile("consumer", definition,
            MemberRead("WorkflowRequestMember") with { Conversion = new AuthoredValueConversionRequest(AuthoredValueConversionMode.Json) }));

        Assert.Equal(RuntimeInputBindingSource.WorkflowRequestMember, member.Source);
        Assert.Equal(RuntimeReadSelectionMode.DeferredFit, member.Selection!.Mode);
        Assert.Equal(["customerId"], member.Selection.Segments);
        Assert.Null(member.WorkflowRequest!.Path);
        Assert.Equal(ValueConversionRejectionReason.SelectedMemberContractUnknown, converted.ReasonCode);
    }
}
