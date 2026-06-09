using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T010 (FR-013, SC-010) — migrated workflow-level input/output diff coverage. Workflow inputs
/// and outputs match by <c>ReferenceKey</c> and are distinct from per-activity I/O:
/// add/update/remove → <c>OnWorkflowInput*</c> / <c>OnWorkflowOutput*</c>.
/// </summary>
public sealed class WorkflowIoDiffTests
{
    [Fact]
    public async Task Workflow_input_add_update_remove_emit_OnWorkflowInput_events()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Add.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(inputs: [Input("in1", "Input1")]));
        var added = Assert.IsType<OnWorkflowInputAddedToDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("in1", added.Input.ReferenceKey);
        // Workflow-level event is distinct from the per-activity event.
        Assert.Null(host.EventPublisher.LastOf<OnActivityInputAddedToDraft>());

        // Update.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(inputs: [Input("in1", "Renamed")]));
        var updated = Assert.IsType<OnWorkflowInputUpdatedInDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("in1", updated.InputReferenceKey);

        // Remove.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(inputs: []));
        Assert.IsType<OnWorkflowInputRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
    }

    [Fact]
    public async Task Workflow_output_add_update_remove_emit_OnWorkflowOutput_events()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Add.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(outputs: [Output("out1", "Output1")]));
        var added = Assert.IsType<OnWorkflowOutputAddedToDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("out1", added.Output.ReferenceKey);

        // Update.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(outputs: [Output("out1", "Renamed")]));
        Assert.IsType<OnWorkflowOutputUpdatedInDraft>(Assert.Single(DiffEventsSince(host, skip)));

        // Remove.
        skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(outputs: []));
        Assert.IsType<OnWorkflowOutputRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
    }
}
