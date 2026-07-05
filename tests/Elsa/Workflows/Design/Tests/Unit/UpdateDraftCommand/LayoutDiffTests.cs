using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T011 (FR-001a) — layout diff coverage, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). Designer-layout records
/// live on the <c>WorkflowDefinitionDraftLayout</c> sibling, NOT inside
/// <c>WorkflowDefinitionState</c> — the engine takes them as a separate argument. A new or changed
/// <c>DesignMetadataRecord</c> for a <c>NodeId</c> diffs to <c>OnActivityMovedInDraft</c>.
/// </summary>
public sealed class LayoutDiffTests
{
    [Fact]
    public void A_new_layout_record_emits_OnActivityMovedInDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [],
            desiredLayout: [LayoutRecord("n1", 10, 20, 100, 50)]);

        var moved = Assert.Single(diff.OfType<OnActivityMovedInDraft>());
        Assert.Equal("n1", moved.NodeId);
        Assert.Equal(10, moved.NewX);
        Assert.Equal(20, moved.NewY);
        Assert.Equal(100, moved.NewWidth);
        Assert.Equal(50, moved.NewHeight);
    }

    [Fact]
    public void A_changed_layout_record_emits_OnActivityMovedInDraft()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [LayoutRecord("n1", 10, 20)],
            desiredLayout: [LayoutRecord("n1", 99, 88)]);

        var moved = Assert.IsType<OnActivityMovedInDraft>(Assert.Single(diff.OfType<OnActivityMovedInDraft>()));
        Assert.Equal(99, moved.NewX);
        Assert.Equal(88, moved.NewY);
    }

    [Fact]
    public void An_unchanged_layout_record_emits_no_move()
    {
        var diff = Evaluate(
            State(activities: [Node("n1")]),
            State(activities: [Node("n1")]),
            storedLayout: [LayoutRecord("n1", 10, 20)],
            desiredLayout: [LayoutRecord("n1", 10, 20)]);

        Assert.Empty(diff.OfType<OnActivityMovedInDraft>());
    }
}
