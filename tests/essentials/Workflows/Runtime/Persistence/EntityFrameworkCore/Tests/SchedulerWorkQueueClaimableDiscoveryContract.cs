using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.SchedulerWorkItems;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The claimable-backlog discovery contract (#2188) every scheduler work queue provider meets, written once so the
/// in-memory queue and each EF Core provider are held to the same semantics. Each scenario expects an empty queue.
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
