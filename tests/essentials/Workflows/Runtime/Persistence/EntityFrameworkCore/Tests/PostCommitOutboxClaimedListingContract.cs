using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="IRuntimePostCommitOutboxClaimStore.ListClaimedAsync"/> (#2225), written once so the in-memory store and
/// each EF Core provider are held to the same semantics. A live drain waits on what this lists, so an item it wrongly
/// includes stalls the drain on someone else's work, and one it wrongly leaves out ends the drain early.
/// </summary>
internal static class PostCommitOutboxClaimedListingContract
{
    private const string Execution = "wf-draining";
    private const string Continuation = RuntimePostCommitIntentKinds.EnqueueSchedulerWork;
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>
    /// Lists the execution's claimed items of the kind, whoever claimed them and for however long, earliest visibility
    /// deadline first. Pending and delivered items, other kinds and other executions are left out.
    /// </summary>
    public static async Task ListsOneExecutionsClaimedItemsOfOneKindAsync(Backend backend)
    {
        await backend.AddPendingAsync(Pending("held-longer", Execution, Continuation, Now));
        await backend.AddPendingAsync(Pending("held-shorter", Execution, Continuation, Now.AddSeconds(1)));
        await backend.AddPendingAsync(Pending("delivered", Execution, Continuation, Now.AddSeconds(2)));
        await backend.AddPendingAsync(Pending("pending", Execution, Continuation, Now.AddSeconds(3)));
        await backend.AddPendingAsync(Pending("other-kind", Execution, "contract.other", Now));
        await backend.AddPendingAsync(Pending("other-execution", "wf-other", Continuation, Now));
        await ClaimAsync(backend, "sweep-a", TimeSpan.FromMinutes(10), Execution, Continuation);
        await ClaimAsync(backend, "sweep-b", TimeSpan.FromMinutes(1), Execution, Continuation);
        var delivered = await ClaimAsync(backend, "sweep-c", TimeSpan.FromMinutes(1), Execution, Continuation);
        await backend.Claims.RecordDeliveryResultAsync(delivered, new RuntimePostCommitOutboxDeliveryResult(
            delivered.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        await ClaimAsync(backend, "sweep-d", TimeSpan.FromMinutes(1), Execution, "contract.other");
        await ClaimAsync(backend, "sweep-e", TimeSpan.FromMinutes(1), "wf-other", Continuation);

        Assert.Equal(["held-shorter", "held-longer"], await ListAsync(backend, limit: 10));
        Assert.Equal(["held-shorter"], await ListAsync(backend, limit: 1));
    }

    private static async Task<RuntimePostCommitOutboxClaim> ClaimAsync(
        Backend backend,
        string ownerId,
        TimeSpan visibilityTimeout,
        string workflowExecutionId,
        string intentKind) =>
        Assert.Single(await backend.Claims.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            ownerId, Now, visibilityTimeout, limit: 1, workflowExecutionId, intentKind)));

    private static async Task<string[]> ListAsync(Backend backend, int limit) =>
        (await backend.Claims.ListClaimedAsync(new RuntimePostCommitOutboxClaimedQuery(Execution, Continuation, limit)))
        .Select(item => item.OutboxItemId)
        .ToArray();

    // Every item is available now; the recording time only orders the claims, so each claim above takes the next one.
    private static RuntimePostCommitOutboxItem Pending(string outboxItemId, string workflowExecutionId, string kind, DateTimeOffset recordedAt) =>
        new(
            outboxItemId,
            new RuntimePostCommitIntent($"intent-{outboxItemId}", workflowExecutionId, kind, recordedAt, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            availableAt: Now);

    /// <summary>The EF Core outbox store over one context and scope.</summary>
    public static Backend EntityFramework(RuntimeDbContext context, string scope)
    {
        var outbox = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor(scope));
        return new Backend(outbox, item => outbox.SavePendingAsync(item));
    }

    /// <summary>One provider's claim store and a way to seed it.</summary>
    public sealed record Backend(IRuntimePostCommitOutboxClaimStore Claims, Func<RuntimePostCommitOutboxItem, ValueTask> AddPendingAsync);
}
