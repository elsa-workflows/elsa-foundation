using Elsa.Workflows.Design.Persistence.EFCore.Constants;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T018 (SC-003) — the command acquires the per-Draft <c>workflow-draft:{DraftId}</c> lock
/// exactly once per call; concurrent calls on the same Draft serialise on that key; calls on
/// different Drafts use different keys and proceed without contention.
/// </summary>
public sealed class LockingTests
{
    [Fact]
    public async Task One_execute_acquires_the_draft_lock_exactly_once()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var key = LockKeys.DraftKey(draftId);
        var before = host.LockProvider.AcquireCounts.GetValueOrDefault(key, 0);

        await Update(host, draftId, State(activities: [Node("n1")]));

        Assert.Equal(before + 1, host.LockProvider.AcquireCounts[key]);
    }

    [Fact]
    public async Task Concurrent_calls_on_the_same_draft_serialise_and_both_complete()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        var key = LockKeys.DraftKey(draftId);
        var before = host.LockProvider.AcquireCounts.GetValueOrDefault(key, 0);

        // Two updates racing on the same Draft. The per-name semaphore serialises them; both
        // must complete without deadlock or error.
        await Task.WhenAll(
            Update(host, draftId, State(activities: [Node("n1")])),
            Update(host, draftId, State(activities: [Node("n2")])));

        Assert.Equal(before + 2, host.LockProvider.AcquireCounts[key]);

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.NotNull(draft.StateSource);
    }

    [Fact]
    public async Task Calls_on_different_drafts_use_distinct_keys()
    {
        // Two Drafts → two distinct lock keys (proves the lock is per-Draft, not global). Cross-key
        // parallelism itself is proven directly in LockSemanticsTests; here we assert the command
        // keys each Draft separately. Run sequentially — the SQLite test host shares one connection.
        using var host = WorkflowsDesignTestHost.Create();
        var draftA = await SeedEmptyDraft(host, "wf-a");
        var draftB = await SeedEmptyDraft(host, "wf-b");

        var keyA = LockKeys.DraftKey(draftA);
        var keyB = LockKeys.DraftKey(draftB);
        // CreateDraft (the seed) already acquired each per-Draft lock once via ExecuteCreation;
        // snapshot those baselines and assert each Update adds exactly one acquisition on its
        // own key.
        var beforeA = host.LockProvider.AcquireCounts.GetValueOrDefault(keyA, 0);
        var beforeB = host.LockProvider.AcquireCounts.GetValueOrDefault(keyB, 0);

        await Update(host, draftA, State(activities: [Node("n1")]));
        await Update(host, draftB, State(activities: [Node("n2")]));

        Assert.NotEqual(keyA, keyB);
        Assert.Equal(beforeA + 1, host.LockProvider.AcquireCounts[keyA]);
        Assert.Equal(beforeB + 1, host.LockProvider.AcquireCounts[keyB]);
    }
}
