using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkItems;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The claimable-backlog discovery contract (#2188) every scheduler work queue provider meets, with the head-of-line rule
/// a delay by identity keeps (#3690), written once so the in-memory queue and each EF Core provider are held to the same
/// semantics. Each scenario expects an empty queue.
/// </summary>
internal static class SchedulerWorkQueueClaimableDiscoveryContract
{
    public static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Seeds one execution per claim state and checks discovery both ways: it lists every execution a claim at the same
    /// instant would serve, and none that a claim would refuse.
    /// </summary>
    public static async Task ListsExactlyTheExecutionsAClaimWouldServeAsync(IWorkflowSchedulerWorkQueue queue)
    {
        Assert.True(queue.SupportsClaimableBacklogDiscovery);
        await queue.EnqueueAsync(Work("wf-fresh", "work-1", 1));
        await EnqueueClaimedAsync(queue, "wf-claimed", Now);
        await EnqueueClaimedAsync(queue, "wf-lapsed", Now - Lease - Lease);
        await EnqueueReleasedAsync(queue, "wf-backoff", visibleAt: Now + Lease);
        await EnqueueReleasedAsync(queue, "wf-due", visibleAt: Now);
        // Strict FIFO: a visible item behind a head under a live claim is not claimable.
        await EnqueueClaimedAsync(queue, "wf-blocked", Now);
        await queue.EnqueueAsync(Work("wf-blocked", "work-2", 2));

        var listed = await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now));

        Assert.Equal(["wf-due", "wf-fresh", "wf-lapsed"], listed);
        foreach (var workflowExecutionId in new[] { "wf-fresh", "wf-claimed", "wf-lapsed", "wf-backoff", "wf-due", "wf-blocked" })
        {
            var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "owner-probe", Now, Lease));
            Assert.True(listed.Contains(workflowExecutionId) == claim is not null, $"Discovery and ClaimAsync disagree on '{workflowExecutionId}'.");
        }
    }

    /// <summary>
    /// Pages in ordinal (UTF-16 code unit) order after an exclusive bound, which need not be a queued ID. The IDs mix
    /// case and a shared prefix so a provider that sorted by a collation or by length would fail.
    /// </summary>
    public static async Task PagesInOrdinalOrderAfterTheBoundAsync(IWorkflowSchedulerWorkQueue queue)
    {
        foreach (var workflowExecutionId in new[] { "wf-c", "wf-a-1", "wf-B", "wf-a" })
            await queue.EnqueueAsync(Work(workflowExecutionId, "work-1", 1));

        Assert.Equal(["wf-B", "wf-a"], await ListAsync(queue, 2, after: null));
        Assert.Equal(["wf-a-1", "wf-c"], await ListAsync(queue, 2, after: "wf-a"));
        Assert.Empty(await ListAsync(queue, 2, after: "wf-c"));
        Assert.Equal(["wf-a-1", "wf-c"], await ListAsync(queue, 10, after: "wf-a-0"));
        Assert.Equal(["wf-B", "wf-a", "wf-a-1", "wf-c"], await ListAsync(queue, 10, after: null));
    }

    /// <summary>
    /// Reads, in one request, the item a claim would take next for each execution: its FIFO head whatever the head's
    /// visibility, and nothing for an execution without queued work.
    /// </summary>
    public static async Task ReadsTheNextItemAClaimWouldTakeAsync(IWorkflowSchedulerWorkQueue queue)
    {
        await queue.EnqueueAsync(Work("wf-two", "work-1", 1));
        await queue.EnqueueAsync(Work("wf-two", "work-2", 2));
        await EnqueueClaimedAsync(queue, "wf-claimed", Now);
        await queue.EnqueueAsync(Work("wf-claimed", "work-2", 2));

        var next = await queue.ListNextWorkItemsAsync(["wf-two", "wf-claimed", "wf-empty"]);

        Assert.Equal(["wf-claimed", "wf-two"], next.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("work-1", next["wf-claimed"].WorkItemId);
        var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-two", "owner-probe", Now, Lease));
        Assert.Equal(next["wf-two"].WorkItemId, claim?.Item.WorkItemId);
    }

    /// <summary>
    /// Delays one item by its identity behind a head another writer queued first (#3690). The head stays claimable and is
    /// served first; the delayed item, once it is the head, hides its execution until it is due, so later work does not
    /// overtake it. A claim that has not lapsed keeps its item; a lapsed one is fenced out by the delay.
    /// </summary>
    public static async Task DefersOneItemByIdentityBehindAnEarlierHeadAsync(IWorkflowSchedulerWorkQueue queue)
    {
        var deferral = Assert.IsAssignableFrom<IWorkflowSchedulerWorkDeferral>(queue);
        await queue.EnqueueAsync(Work("wf-deferred", "work-1", 1));
        await queue.EnqueueAsync(Work("wf-deferred", "work-2", 2));
        await queue.EnqueueAsync(Work("wf-deferred", "work-3", 3));
        var due = Now + Lease;

        Assert.True(await deferral.DeferAsync("wf-deferred", "work-2", Now, due));
        Assert.False(await deferral.DeferAsync("wf-deferred", "work-absent", Now, due));

        Assert.Equal([("work-1", (DateTimeOffset?)null), ("work-2", due), ("work-3", null)],
            (await Assert.IsAssignableFrom<IWorkflowSchedulerWorkVisibilityReader>(queue)
                .ListWithVisibilityAsync(new RuntimeSchedulerWorkQuery("wf-deferred"))).Items
            .Select(entry => (entry.Item.WorkItemId, entry.VisibleAfter)));
        Assert.Equal(["wf-deferred"], await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now)));
        var head = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-deferred", "owner-a", Now, Lease));
        Assert.Equal("work-1", head?.Item.WorkItemId);
        Assert.True((await queue.CompleteClaimAsync(head!)).Succeeded);

        Assert.Empty(await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(due - TimeSpan.FromTicks(1))));
        Assert.Null(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-deferred", "owner-a", due - TimeSpan.FromTicks(1), Lease)));
        Assert.Equal(["wf-deferred"], await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(due)));
        var live = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-deferred", "owner-b", due, Lease));
        Assert.Equal("work-2", live?.Item.WorkItemId);

        Assert.False(await deferral.DeferAsync("wf-deferred", "work-2", due, due + Lease + Lease));
        var kept = Assert.Single(await Assert.IsAssignableFrom<IWorkflowSchedulerWorkClaimInspection>(queue).ListActiveClaimsAsync("wf-deferred", due));
        Assert.Equal((live!.Item.WorkItemId, live.FencingToken, live.VisibleAfter), (kept.Item.WorkItemId, kept.FencingToken, kept.VisibleAfter));

        Assert.True(await deferral.DeferAsync("wf-deferred", "work-2", due + Lease, due + Lease));
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await queue.CompleteClaimAsync(live)).Status);
        var redelivered = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-deferred", "owner-c", due + Lease, Lease));
        Assert.Equal("work-2", redelivered?.Item.WorkItemId);
        // The deferral advanced the fencing token itself, as the claim of a claim and release would, so the claim that
        // redelivers the item is fenced past both the lapsed claim and the deferral; a plain release would leave it one past.
        Assert.True(redelivered!.FencingToken > live.FencingToken + 1,
            $"Expected the redelivering claim's token past {live.FencingToken + 1}, saw {redelivered.FencingToken}.");
    }

    /// <summary>Queues one item for the execution and claims it at <paramref name="claimedAt"/> for one lease.</summary>
    public static async Task<RuntimeSchedulerWorkClaim> EnqueueClaimedAsync(IWorkflowSchedulerWorkQueue queue, string workflowExecutionId, DateTimeOffset claimedAt)
    {
        await queue.EnqueueAsync(Work(workflowExecutionId, "work-1", 1));
        return await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(workflowExecutionId, "owner-a", claimedAt, Lease))
               ?? throw new InvalidOperationException($"The fresh head of '{workflowExecutionId}' could not be claimed.");
    }

    /// <summary>Queues one item for the execution, claims it, and releases it with the given visibility, as a backoff does.</summary>
    public static async Task EnqueueReleasedAsync(IWorkflowSchedulerWorkQueue queue, string workflowExecutionId, DateTimeOffset visibleAt)
    {
        var claim = await EnqueueClaimedAsync(queue, workflowExecutionId, Now - Lease);
        Assert.True((await queue.ReleaseClaimAsync(claim, visibleAt)).Succeeded);
    }

    private static async Task<IReadOnlyCollection<string>> ListAsync(IWorkflowSchedulerWorkQueue queue, int limit, string? after) =>
        await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(Now, limit, after));
}
