using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T006 (FR-013, SC-010) — activity-graph diff coverage. Per-diff mutation-event publication has
/// been retired from <c>IUpdateDraftCommand</c>, so the event half now drives
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly:
/// adding a node (new <c>NodeId</c> in desired) emits <c>OnActivityAddedToDraft</c>; removing one
/// (<c>NodeId</c> absent from desired) emits <c>OnActivityRemovedFromDraft</c>. Match key is
/// <c>NodeId</c>. The persistence half is retained as a command-level round-trip.
/// </summary>
public sealed class ActivityDiffTests
{
    [Fact]
    public void Adding_an_activity_emits_OnActivityAddedToDraft()
    {
        var diff = Evaluate(State(), State(activities: [Node("node-1")]));

        var added = Assert.IsType<OnActivityAddedToDraft>(Assert.Single(diff));
        Assert.Equal("node-1", added.NodeId);
    }

    [Fact]
    public async Task Adding_an_activity_persists_it()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        await Update(host, draftId, State(activities: [Node("node-1")]));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }

    [Fact]
    public void Removing_an_activity_emits_OnActivityRemovedFromDraft()
    {
        var diff = Evaluate(State(activities: [Node("node-1")]), State(activities: []));

        var removed = Assert.IsType<OnActivityRemovedFromDraft>(Assert.Single(diff));
        Assert.Equal("node-1", removed.NodeId);
    }
}
