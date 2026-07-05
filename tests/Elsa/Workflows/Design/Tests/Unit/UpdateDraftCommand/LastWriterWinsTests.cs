using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T014 (SC-014, FR-013c) — last-writer-wins persistence. A desired state computed from a stale
/// read overwrites a concurrent writer's changes wholesale; the command completes with no
/// conflict/version error (the entity has no version column) and the final stored State is exactly
/// the last writer's desired State. Observed via a state round-trip (per-diff event observation
/// retired).
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
        await Update(host, draftId, State(activities: [Node("a"), Node("c")]));

        // B's write wins wholesale: the stored State is {a,c}, A's b is gone. No conflict thrown.
        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("\"c\"", draft.StateSource);
        Assert.DoesNotContain("\"b\"", draft.StateSource);
    }
}
