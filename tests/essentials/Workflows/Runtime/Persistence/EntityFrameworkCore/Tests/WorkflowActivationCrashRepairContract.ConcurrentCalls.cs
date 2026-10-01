using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Concurrent calls for one activation through real root-write leases (#2274), and the race that keeps the artifact
/// reconciler a <c>[SingleNodeTask]</c>.
/// </summary>
internal static partial class WorkflowActivationCrashRepairContract
{
    /// <summary>The options of every root-write lease manager here, and the source of <see cref="LeaseDuration"/>.</summary>
    private static readonly WorkflowExecutableGarbageCollectionOptions GarbageCollectionOptions = new();

    private static readonly TimeSpan LeaseDuration = GarbageCollectionOptions.RootWriteLeaseDuration;

    /// <summary>
    /// Two nodes activate the same artifact at once, so they share one activation id, and each holds a root-write lease
    /// for it (#2274). The loser holds before its slot transition while the winner runs to the end and releases its lease.
    /// Had both held one lease, as they share the id, the winner's release would have ended the loser's: the artifact would
    /// be open to reference garbage collection while the loser still ran, and the loser's next renewal would fail and
    /// discard its result. Each call's lease is its own, so the loser still fences the artifact, its renewal succeeds, and
    /// it answers as before; once it has finished too, nothing fences the artifact.
    /// </summary>
    private static async Task SameActivationLoserKeepsItsLeaseAfterTheWinnerReleasesItsOwnAsync(Func<ActivationStores> open)
    {
        var clock = new FakeTimeProvider(Now);
        var renewals = new FirstRenewal();
        await using var collector = await StartCollectorAsync(open);
        await using var race = await StartRaceAsync(Leased);
        await ActivateWinnerAsync(Leased);

        Assert.Null(await TryBeginCollectingAsync(collector, clock.GetUtcNow()));
        clock.Advance(LeaseDuration / 3);
        Assert.True(await renewals.Result.WaitAsync(TimeSpan.FromSeconds(30)));
        var result = await race.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        await race.Loser.AssertConsistentAsync("activation-2");
        var guard = await TryBeginCollectingAsync(collector, clock.GetUtcNow());
        Assert.NotNull(guard);
        Assert.True(await collector.Stores.Executables.CancelDeletionAsync(guard));

        ActivationStores Leased()
        {
            var stores = open();
            return stores with { Executables = renewals.Watch(stores.Executables), LeaseClock = clock };
        }
    }

    /// <summary>
    /// A call stops for good inside its root-write lease, once its slot transition commits. Nothing renews or releases that
    /// lease, and no concurrent call takes it over, since each call takes its own (#2274): a second call for the same
    /// activation, held before its slot transition meanwhile, runs to the end and releases its own lease without ending the
    /// first's, so the artifact stays fenced from reference garbage collection until the first lease expires, and no longer.
    /// </summary>
    private static async Task LeaseOfACallThatStoppedInsideItExpiresAsync(Func<ActivationStores> open)
    {
        await using var collector = await StartCollectorAsync(open);
        // The stopped process's clock, which nobody advances.
        var stopped = new FakeTimeProvider(Now);
        ActivationStores Stopped() => open() with { LeaseClock = stopped };
        // The second call holds its own lease, held before its slot transition, while the first runs and stops.
        await using var race = await StartRaceAsync(Stopped);
        await StopAfterSlotTransitionAsync(Stopped, "activation-2", "artifact-2");
        await race.ReleaseAsync();
        var expiry = Now.Add(LeaseDuration);

        Assert.Null(await TryBeginCollectingAsync(collector, expiry.AddSeconds(-1)));
        var guard = await TryBeginCollectingAsync(collector, expiry);

        Assert.NotNull(guard);
        Assert.True(await collector.Stores.Executables.CancelDeletionAsync(guard));
    }

    /// <summary>
    /// KNOWN BAD: pins the window that keeps the artifact reconciler a <c>[SingleNodeTask]</c> (#2274); it is not a
    /// guarantee. When #2230 lands, flip it: the winner's activation-2 must stay serving and its <c>Activated</c> must hold.
    /// The loser is cancelled, as a node shutting down mid-reconcile is, while its slot transition is in flight, and then
    /// reads the slot naming its activation.
    /// It cannot tell the winner's transition from its own, so it compensates the activation as its own and hands the slot
    /// back to the activation it replaced. The slot, the projections and the references agree, but the winner reported
    /// <see cref="WorkflowActivationOutcome.Activated"/> for an activation that no longer serves, and nothing says so until
    /// the slot is activated again. Switching the slot and the projections in one transaction (#2230) closes this.
    /// </summary>
    private static async Task KnownBadSameActivationLoserCancelledInItsSlotTransitionHandsTheSlotBackAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        await using var race = await StartRaceAsync(open, cancellationToken: cancellation.Token);
        await ActivateWinnerAsync(open);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(race.ReleaseAsync);
        // TODO(#2230): flip. These pin the bad outcome: activation-1 serves again and activation-2 is failed, though the winner reported Activated.
        await race.Loser.AssertConsistentAsync("activation-1");
        await race.Loser.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Missing);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await race.Loser.FindReferenceAsync("activation-2")).DeletedReason);
    }

    /// <summary>The reference garbage collector's process, with artifact-2 stored for activations to lease.</summary>
    private static async Task<ActivationNode> StartCollectorAsync(Func<ActivationStores> open)
    {
        var collector = Start(open());
        await collector.Stores.Executables.SaveAsync(Executable("artifact-2"));
        return collector;
    }

    /// <summary>The collector's first step for artifact-2: a deletion guard, refused while a live root-write lease holds it.</summary>
    private static async Task<WorkflowExecutableDeletionGuard?> TryBeginCollectingAsync(ActivationNode collector, DateTimeOffset now) =>
        await collector.Stores.Executables.TryBeginDeletionAsync("artifact-2", "reference-collector", now.Add(LeaseDuration), now);

    /// <summary>Reports the first root-write lease renewal through any store it watches, and whether it succeeded.</summary>
    private sealed class FirstRenewal
    {
        private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> Result => _result.Task;

        public IWorkflowExecutableStore Watch(IWorkflowExecutableStore inner) => new Watched(inner, _result);

        private sealed class Watched(IWorkflowExecutableStore inner, TaskCompletionSource<bool> result) : WorkflowExecutableStoreDecorator(inner)
        {
            public override async ValueTask<bool> RenewRootWriteLeaseAsync(
                WorkflowExecutableRootWriteLease lease,
                DateTimeOffset expiresAt,
                DateTimeOffset now,
                CancellationToken cancellationToken = default)
            {
                try
                {
                    var renewed = await base.RenewRootWriteLeaseAsync(lease, expiresAt, now, cancellationToken);
                    result.TrySetResult(renewed);
                    return renewed;
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                    throw;
                }
            }
        }
    }
}
