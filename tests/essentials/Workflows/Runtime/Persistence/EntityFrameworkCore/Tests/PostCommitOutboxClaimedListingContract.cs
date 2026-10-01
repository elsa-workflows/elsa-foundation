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

    // Every claim is taken at this one instant, so no claim lapses while the next is taken; the drain reads at Now.
    private static readonly DateTimeOffset ClaimedAt = Now - TimeSpan.FromMinutes(5);
    private static readonly RuntimePostCommitRetryPolicy Retrying = new(3, TimeSpan.FromMinutes(2));

    /// <summary>
    /// Lists the execution's claimed items of the kind, whoever claimed them and for however long, a claim that has
    /// already lapsed included, and the ones whose attempt failed and waits for a retry. Earliest
    /// <see cref="RuntimePostCommitOutboxClaimTransitions.ClaimableAt"/> first. Pending, delivered and finally failed
    /// items, other kinds and other executions are left out.
    /// </summary>
    public static async Task ListsOneExecutionsHeldItemsOfOneKindAsync(Backend backend)
    {
        // The recording time orders the claims below: each takes the next item.
        await backend.AddPendingAsync(Pending("lapsed", Execution, Continuation, ClaimedAt));
        await backend.AddPendingAsync(Pending("held-longer", Execution, Continuation, ClaimedAt.AddSeconds(1)));
        await backend.AddPendingAsync(Pending("held-shorter", Execution, Continuation, ClaimedAt.AddSeconds(2)));
        await backend.AddPendingAsync(Pending("delivered", Execution, Continuation, ClaimedAt.AddSeconds(3)));
        await backend.AddPendingAsync(Pending("failed-retryable", Execution, Continuation, ClaimedAt.AddSeconds(4)));
        await backend.AddPendingAsync(Pending("failed-final", Execution, Continuation, ClaimedAt.AddSeconds(5)));
        await backend.AddPendingAsync(Pending("pending", Execution, Continuation, ClaimedAt.AddSeconds(6)));
        await backend.AddPendingAsync(Pending("other-kind", Execution, "contract.other", ClaimedAt));
        await backend.AddPendingAsync(Pending("other-execution", "wf-other", Continuation, ClaimedAt));
        // Visible again at Now - 4 min: lapsed by the time the drain reads, and still listed.
        await ClaimAsync(backend, "dead-sweep", TimeSpan.FromMinutes(1), Execution, Continuation);
        await ClaimAsync(backend, "sweep-a", TimeSpan.FromMinutes(15), Execution, Continuation);
        await ClaimAsync(backend, "sweep-b", TimeSpan.FromMinutes(6), Execution, Continuation);
        await CompleteAsync(backend, await ClaimAsync(backend, "sweep-c", TimeSpan.FromMinutes(1), Execution, Continuation), RuntimePostCommitOutboxStatus.Delivered);
        // Retried at ClaimedAt + 2 min, so it sorts between the lapsed claim and the live ones.
        await CompleteAsync(backend, await ClaimAsync(backend, "sweep-d", TimeSpan.FromMinutes(1), Execution, Continuation), RuntimePostCommitOutboxStatus.FailedRetryable);
        await CompleteAsync(backend, await ClaimAsync(backend, "sweep-e", TimeSpan.FromMinutes(1), Execution, Continuation), RuntimePostCommitOutboxStatus.FailedFinal);
        await ClaimAsync(backend, "sweep-f", TimeSpan.FromMinutes(1), Execution, "contract.other");
        await ClaimAsync(backend, "sweep-g", TimeSpan.FromMinutes(1), "wf-other", Continuation);

        Assert.Equal(["lapsed", "failed-retryable", "held-shorter", "held-longer"], await ListAsync(backend, limit: 10));
        Assert.Equal(["lapsed"], await ListAsync(backend, limit: 1));
    }

    private static async Task<RuntimePostCommitOutboxClaim> ClaimAsync(
        Backend backend,
        string ownerId,
        TimeSpan visibilityTimeout,
        string workflowExecutionId,
        string intentKind) =>
        Assert.Single(await backend.Claims.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            ownerId, ClaimedAt, visibilityTimeout, limit: 1, workflowExecutionId, intentKind)));

    private static async Task CompleteAsync(Backend backend, RuntimePostCommitOutboxClaim claim, RuntimePostCommitOutboxStatus status) =>
        await backend.Claims.RecordDeliveryResultAsync(claim, new RuntimePostCommitOutboxDeliveryResult(
            claim.OutboxItemId,
            status,
            ClaimedAt,
            status == RuntimePostCommitOutboxStatus.Delivered ? null : "The delivery failed."));

    private static async Task<string[]> ListAsync(Backend backend, int limit) =>
        (await backend.Claims.ListClaimedAsync(new RuntimePostCommitOutboxClaimedQuery(Execution, Continuation, limit)))
        .Select(item => item.OutboxItemId)
        .ToArray();

    // Every item is available from the claim instant; the recording time alone orders the claims.
    private static RuntimePostCommitOutboxItem Pending(string outboxItemId, string workflowExecutionId, string kind, DateTimeOffset recordedAt) =>
        new(
            outboxItemId,
            new RuntimePostCommitIntent($"intent-{outboxItemId}", workflowExecutionId, kind, recordedAt, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            availableAt: ClaimedAt,
            retryPolicy: Retrying);

    /// <summary>The EF Core outbox store over one context and scope.</summary>
    public static Backend EntityFramework(RuntimeDbContext context, string scope)
    {
        var outbox = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor(scope));
        return new Backend(outbox, item => outbox.SavePendingAsync(item));
    }

    /// <summary>One provider's claim store and a way to seed it.</summary>
    public sealed record Backend(IRuntimePostCommitOutboxClaimStore Claims, Func<RuntimePostCommitOutboxItem, ValueTask> AddPendingAsync);
}
