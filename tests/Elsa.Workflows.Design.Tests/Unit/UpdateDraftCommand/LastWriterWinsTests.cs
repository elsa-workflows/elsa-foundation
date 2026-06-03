using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T014 (SC-014, FR-013c) — last-writer-wins. A desired state computed from a stale read
/// overwrites a concurrent writer's changes wholesale; the diff emits the resulting REMOVE/ADD
/// events and the command completes with no conflict/version error (the entity has no version
/// column).
/// </summary>
public sealed class LastWriterWinsTests
{
    [Fact]
    public async Task Stale_write_overwrites_concurrent_changes_without_conflict()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Common ancestor both writers read.
        await Update(host, draftId, State(activities: [Node("a")]));

        // Writer A commits: adds activity b.
        await Update(host, draftId, State(activities: [Node("a"), Node("b")]));

        // Writer B, working from the pre-A read (just {a}), adds c instead — overwriting A's b.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("a"), Node("c")]));

        var diff = DiffEventsSince(host, skip);

        // B's write is diffed against the CURRENT stored state ({a,b}): c added, b removed.
        Assert.Single(diff.OfType<OnActivityAddedToDraft>(), e => e.NodeId == "c");
        Assert.Single(diff.OfType<OnActivityRemovedFromDraft>(), e => e.NodeId == "b");

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("\"c\"", draft.StateSource);
        Assert.DoesNotContain("\"b\"", draft.StateSource);
    }
}
