using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The #2225 claim contract every post-commit outbox claim store meets, written once so the in-memory store and each EF
/// Core provider are held to the same semantics: a claim that defers continuations to their execution's owner skips a
/// scheduler-work continuation while that execution holds an unexpired ownership lease, and nothing else.
/// </summary>
internal static class PostCommitOutboxContinuationDeferralContract
{
    public static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    public static readonly RuntimeExecutionOwnershipOptions OwnershipOptions = new() { OwnerId = "live-drain" };

    // Longer than the lease, so an item claimed earlier in a scenario stays hidden when a later claim runs past the lease.
    private static readonly TimeSpan ClaimVisibility = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The race behind #2225: a live drain owns its execution, so a sweep claim leaves that execution's continuation alone
    /// while still claiming its other intent kinds and every other execution's continuations. A claim that does not
    /// defer, which is how a drain claims its own work, still takes the continuation.
    /// </summary>
    public static async Task ASweepLeavesAContinuationToTheDrainThatOwnsItsExecutionAsync(Backend backend)
    {
        await backend.AddPendingAsync(Pending("owned-continuation", "wf-owned", RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        await backend.AddPendingAsync(Pending("owned-other-kind", "wf-owned", "contract.other"));
        await backend.AddPendingAsync(Pending("idle-continuation", "wf-idle", RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        await backend.Ownership.AcquireAsync("wf-owned");

        Assert.Equal(["idle-continuation", "owned-other-kind"], await ClaimAsync(backend, Now, defer: true));
        Assert.Equal(["owned-continuation"], await ClaimAsync(backend, Now, defer: false));
    }

    /// <summary>
    /// The deferral lasts only as long as the lease. A released lease (the drain finished) and an expired one (the drain
    /// died) both hand the continuation back to the sweep, which is what keeps it a crash backstop.
    /// </summary>
    public static async Task ASweepClaimsAContinuationOnceItsOwnerReleasesOrLosesTheLeaseAsync(Backend backend)
    {
        await backend.AddPendingAsync(Pending("released-continuation", "wf-released", RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        await backend.AddPendingAsync(Pending("lapsed-continuation", "wf-lapsed", RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        var released = await backend.Ownership.AcquireAsync("wf-released");
        await backend.Ownership.AcquireAsync("wf-lapsed");
        Assert.True((await backend.Ownership.ReleaseAsync(released)).Succeeded);

        Assert.Equal(["released-continuation"], await ClaimAsync(backend, Now, defer: true));
        Assert.Equal(["lapsed-continuation"], await ClaimAsync(backend, Now + OwnershipOptions.LeaseDuration, defer: true));
    }

    private static async Task<string[]> ClaimAsync(Backend backend, DateTimeOffset now, bool defer)
    {
        var claims = await backend.Claims.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            defer ? "resumption-sweep" : "drain-claim",
            now,
            ClaimVisibility,
            limit: 10,
            deferContinuationsToExecutionOwner: defer));
        return claims.Select(claim => claim.OutboxItemId).Order(StringComparer.Ordinal).ToArray();
    }

    private static RuntimePostCommitOutboxItem Pending(string outboxItemId, string workflowExecutionId, string kind) =>
        new(
            outboxItemId,
            new RuntimePostCommitIntent($"intent-{outboxItemId}", workflowExecutionId, kind, Now, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now);

    /// <summary>The EF Core outbox and liveness stores over one context, as the runtime composes them.</summary>
    public static Backend EntityFramework(RuntimeDbContext context, string scope)
    {
        var accessor = new FixedAccessor(scope);
        var outbox = new EfRuntimePostCommitOutboxStore(context, accessor);
        var liveness = new EfExecutionLivenessStateStore(context, accessor, new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
        return new Backend(outbox, item => outbox.SavePendingAsync(item), Ownership(liveness));
    }

    public static IRuntimeExecutionOwnershipService Ownership(IExecutionLivenessStateStore liveness) =>
        new RuntimeExecutionOwnershipService(liveness, new FixedTimeProvider(Now), OwnershipOptions);

    /// <summary>One provider's claim store, a way to seed it, and an ownership service over the same liveness records.</summary>
    public sealed record Backend(
        IRuntimePostCommitOutboxClaimStore Claims,
        Func<RuntimePostCommitOutboxItem, ValueTask> AddPendingAsync,
        IRuntimeExecutionOwnershipService Ownership);
}
