using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T010 (FR-013, SC-010) — workflow-level input/output diff coverage, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). Workflow inputs and
/// outputs match by <c>ReferenceKey</c> and are distinct from per-activity I/O:
/// add/update/remove → <c>OnWorkflowInput*</c> / <c>OnWorkflowOutput*</c>.
/// </summary>
public sealed class WorkflowIoDiffTests
{
    [Fact]
    public void Workflow_input_add_update_remove_emit_OnWorkflowInput_events()
    {
        // Add.
        var added = Assert.IsType<OnWorkflowInputAddedToDraft>(Assert.Single(
            Evaluate(State(), State(inputs: [Input("in1", "Input1")]))));
        Assert.Equal("in1", added.Input.ReferenceKey);

        // Update.
        var updated = Assert.IsType<OnWorkflowInputUpdatedInDraft>(Assert.Single(
            Evaluate(State(inputs: [Input("in1", "Input1")]), State(inputs: [Input("in1", "Renamed")]))));
        Assert.Equal("in1", updated.InputReferenceKey);

        // Remove.
        Assert.IsType<OnWorkflowInputRemovedFromDraft>(Assert.Single(
            Evaluate(State(inputs: [Input("in1", "Renamed")]), State(inputs: []))));
    }

    [Fact]
    public void Workflow_output_add_update_remove_emit_OnWorkflowOutput_events()
    {
        // Add.
        var added = Assert.IsType<OnWorkflowOutputAddedToDraft>(Assert.Single(
            Evaluate(State(), State(outputs: [Output("out1", "Output1")]))));
        Assert.Equal("out1", added.Output.ReferenceKey);

        // Update.
        Assert.IsType<OnWorkflowOutputUpdatedInDraft>(Assert.Single(
            Evaluate(State(outputs: [Output("out1", "Output1")]), State(outputs: [Output("out1", "Renamed")]))));

        // Remove.
        Assert.IsType<OnWorkflowOutputRemovedFromDraft>(Assert.Single(
            Evaluate(State(outputs: [Output("out1", "Renamed")]), State(outputs: []))));
    }
}
