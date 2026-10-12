using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T007 (FR-013, SC-010) — per-activity I/O diff coverage, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). A matched activity
/// (same <c>NodeId</c>) diffs its <c>Inputs</c>/<c>Outputs</c> by (<c>NodeId</c>,<c>ReferenceKey</c>):
/// add/update/remove map to the six <c>ActivityInput*</c>/<c>ActivityOutput*</c> events.
/// </summary>
public sealed class ActivityIoDiffTests
{
    [Fact]
    public void Adding_an_activity_input_emits_ActivityInputAddedToDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]),
            State(activities: [Node("n1", inputs: [Arg("ak1", "x"), Arg("ak2", "y")])]));

        var added = Assert.IsType<ActivityInputAddedToDraft>(Assert.Single(diff));
        Assert.Equal("n1", added.NodeId);
        Assert.Equal("ak2", added.InputReferenceKey);
    }

    [Fact]
    public void Updating_an_activity_input_payload_emits_ActivityInputUpdatedInDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]),
            State(activities: [Node("n1", inputs: [Arg("ak1", "changed")])]));

        var updated = Assert.IsType<ActivityInputUpdatedInDraft>(Assert.Single(diff));
        Assert.Equal("n1", updated.NodeId);
        Assert.Equal("ak1", updated.InputReferenceKey);
    }

    [Fact]
    public void Removing_an_activity_input_emits_ActivityInputRemovedFromDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1", inputs: [Arg("ak1", "x")])]),
            State(activities: [Node("n1", inputs: [])]));

        var removed = Assert.IsType<ActivityInputRemovedFromDraft>(Assert.Single(diff));
        Assert.Equal("n1", removed.NodeId);
    }

    [Fact]
    public void Output_add_update_remove_emit_the_OnActivityOutput_events()
    {
        // Add.
        Assert.IsType<ActivityOutputAddedToDraft>(Assert.Single(Evaluate(
            State(activities: [Node("n1", outputs: [Arg("ok1", "x")])]),
            State(activities: [Node("n1", outputs: [Arg("ok1", "x"), Arg("ok2", "y")])]))));

        // Update.
        Assert.IsType<ActivityOutputUpdatedInDraft>(Assert.Single(Evaluate(
            State(activities: [Node("n1", outputs: [Arg("ok1", "x"), Arg("ok2", "y")])]),
            State(activities: [Node("n1", outputs: [Arg("ok1", "changed"), Arg("ok2", "y")])]))));

        // Remove.
        Assert.IsType<ActivityOutputRemovedFromDraft>(Assert.Single(Evaluate(
            State(activities: [Node("n1", outputs: [Arg("ok1", "changed"), Arg("ok2", "y")])]),
            State(activities: [Node("n1", outputs: [Arg("ok1", "changed")])]))));
    }
}
