using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T011 (FR-001a) — layout diff coverage. Designer-layout records live on the
/// <c>WorkflowDefinitionDraftLayout</c> sibling, NOT inside <c>WorkflowDefinitionState</c>. A new
/// or changed <c>DesignMetadataRecord</c> for a <c>NodeId</c> diffs to
/// <c>OnActivityMovedInDraft</c> — proving layout is diffed from the sibling, separately from State.
/// </summary>
public sealed class LayoutDiffTests
{
    [Fact]
    public async Task A_new_layout_record_emits_OnActivityMovedInDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1")]), layout: [LayoutRecord("n1", 10, 20, 100, 50)]);

        // Activity add + the layout move (distinct dimensions).
        var diff = DiffEventsSince(host, skip);
        var moved = Assert.Single(diff.OfType<OnActivityMovedInDraft>());
        Assert.Equal("n1", moved.NodeId);
        Assert.Equal(10, moved.NewX);
        Assert.Equal(20, moved.NewY);
        Assert.Equal(100, moved.NewWidth);
        Assert.Equal(50, moved.NewHeight);
    }

    [Fact]
    public async Task A_changed_layout_record_emits_OnActivityMovedInDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1")]), layout: [LayoutRecord("n1", 10, 20)]);

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1")]), layout: [LayoutRecord("n1", 99, 88)]);

        var moved = Assert.IsType<OnActivityMovedInDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal(99, moved.NewX);
        Assert.Equal(88, moved.NewY);
    }

    [Fact]
    public async Task An_unchanged_layout_record_emits_no_move()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("n1")]), layout: [LayoutRecord("n1", 10, 20)]);

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("n1")]), layout: [LayoutRecord("n1", 10, 20)]);

        Assert.Empty(DiffEventsSince(host, skip));
    }
}
