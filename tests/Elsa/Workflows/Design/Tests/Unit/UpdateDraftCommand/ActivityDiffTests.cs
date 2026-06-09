using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T006 (FR-013, SC-010) — migrated activity-graph diff coverage, now driving the single
/// <c>IUpdateDraftCommand</c>: adding a node (new <c>NodeId</c> in desired) emits
/// <c>OnActivityAddedToDraft</c>; removing one (<c>NodeId</c> absent from desired) emits
/// <c>OnActivityRemovedFromDraft</c>. Match key is <c>NodeId</c>.
/// </summary>
public sealed class ActivityDiffTests
{
    [Fact]
    public async Task Adding_an_activity_emits_OnActivityAddedToDraft_and_persists_it()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("node-1")]));

        var diff = DiffEventsSince(host, skip);
        var added = Assert.IsType<OnActivityAddedToDraft>(Assert.Single(diff));
        Assert.Equal("node-1", added.NodeId);

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }

    [Fact]
    public async Task Removing_an_activity_emits_OnActivityRemovedFromDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(activities: [Node("node-1")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: []));

        var diff = DiffEventsSince(host, skip);
        var removed = Assert.IsType<OnActivityRemovedFromDraft>(Assert.Single(diff));
        Assert.Equal("node-1", removed.NodeId);
    }
}
