using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Concurrent calls for one activation (#2251), as two nodes reconciling one mounted set make: they share one activation
/// id, and with it one source reference and one set of projections. Every interleaving the coordinator can meet is fixed
/// here with latches, not left to timing.
/// </summary>
internal static partial class WorkflowActivationCrashRepairContract
{
    /// <summary>The options of every root-write lease manager here, and the source of <see cref="LeaseDuration"/>.</summary>
    private static readonly WorkflowExecutableGarbageCollectionOptions GarbageCollectionOptions = new();

    private static readonly TimeSpan LeaseDuration = GarbageCollectionOptions.RootWriteLeaseDuration;

    private static readonly Dictionary<string, Func<Func<ActivationStores>, Task>> ConcurrentCalls = new()
    {
        ["same-activation-losing-the-switch-keeps-the-winner"] = SameActivationLosingTheSwitchKeepsTheWinnerAsync,
        ["same-activation-losing-to-a-winner-that-stopped-after-its-switch-keeps-it"] = SameActivationLosingToAWinnerThatStoppedKeepsItAsync,
        ["same-activation-refused-at-preparation-keeps-the-winner"] = SameActivationRefusedAtPreparationKeepsTheWinnerAsync,
        ["cancelled-same-activation-keeps-the-winner"] = open => CancelledSameActivationKeepsTheWinnerAsync(open, winnerStops: false),
        ["cancelled-same-activation-keeps-a-winner-that-stopped"] = open => CancelledSameActivationKeepsTheWinnerAsync(open, winnerStops: true),
        ["same-activation-loser-discarding-before-the-winner-switches-fails-the-winner-loudly"] = SameActivationLoserDiscardingBeforeTheWinnerSwitchesAsync,
        ["same-activation-loser-discarding-after-the-winner-switched-keeps-the-winner"] = SameActivationLoserDiscardingAfterTheWinnerSwitchedAsync,
        ["same-activation-loser-discarding-before-the-winner-prepares-leaves-the-winners-reference-live"] = SameActivationLoserDiscardingBeforeTheWinnerPreparesAsync,
        ["same-activation-loser-cancelled-in-its-switch-keeps-the-winner"] = SameActivationLoserCancelledInItsSwitchKeepsTheWinnerAsync,
        ["a-call-cancelled-in-a-switch-that-did-not-commit-discards-its-activation"] = ACallCancelledInASwitchThatDidNotCommitDiscardsItsActivationAsync,
        ["a-call-cancelled-as-its-switch-committed-keeps-its-activation"] = ACallCancelledAsItsSwitchCommittedKeepsItsActivationAsync,
        ["same-activation-loser-keeps-its-lease-after-the-winner-releases-its-own"] = SameActivationLoserKeepsItsLeaseAfterTheWinnerReleasesItsOwnAsync,
        ["lease-of-a-call-that-stopped-inside-it-expires"] = LeaseOfACallThatStoppedInsideItExpiresAsync
    };

    /// <summary>
    /// Both calls prepare before either switches, and the second to switch is refused. Compensating it would delete the
    /// winner's projections and retire its reference. The loser's discard is refused because the activation serves, and it
    /// answers as if it had arrived just after the winner.
    /// </summary>
    private static async Task SameActivationLosingTheSwitchKeepsTheWinnerAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var race = await StartRaceAsync(open);
        await ActivateAsync(open, "activation-2", "artifact-2");

        var result = await race.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Equal("activation-2", result.Slot.ActiveActivationId);
        await race.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The same race against a winner that stopped for good once its switch committed: the loser finds the activation
    /// already active.
    /// </summary>
    private static async Task SameActivationLosingToAWinnerThatStoppedKeepsItAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var race = await StartRaceAsync(open);
        await StopAfterSwitchAsync(open, "activation-2", "artifact-2");

        var result = await race.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        await race.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The other order: the loser passes the checks before the sequence while the slot still names the activation it
    /// would replace, but prepares only once the winner serves. Preparing a serving activation is refused, and that
    /// refusal must not compensate the winner either.
    /// </summary>
    private static async Task SameActivationRefusedAtPreparationKeepsTheWinnerAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var race = await StartRaceAsync(open, holdBeforeSequence: true);
        await ActivateAsync(open, "activation-2", "artifact-2");

        var result = await race.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        await race.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The loser of that order is cancelled instead, as a node shutting down mid-reconcile is. The cancellation is
    /// rethrown, not reported as a failure, and its discard leaves the winner's activation alone, whether the winner
    /// finished or stopped once its switch committed.
    /// </summary>
    private static async Task CancelledSameActivationKeepsTheWinnerAsync(Func<ActivationStores> open, bool winnerStops)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        await using var race = await StartRaceAsync(open, holdBeforeSequence: true, cancellationToken: cancellation.Token);
        if (winnerStops)
            await StopAfterSwitchAsync(open, "activation-2", "artifact-2");
        else
            await ActivateAsync(open, "activation-2", "artifact-2");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(race.ReleaseAsync);
        await race.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A discard before the winner's switch. The loser fails before its switch and discards the
    /// shared activation while the winner is still on its way to its own switch. The discard deletes the prepared
    /// projections, so the winner's switch finds nothing to switch on: it fails loudly and moves nothing, rather than
    /// leaving the slot naming an activation that serves nothing. The predecessor keeps serving.
    /// </summary>
    private static async Task SameActivationLoserDiscardingBeforeTheWinnerSwitchesAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var winner = await StartRaceAsync(open);
        await using var loser = Start(open());
        loser.FailNextPreparation(new InvalidOperationException("The trigger indexer is unavailable."));

        var lost = await loser.ActivateAsync("activation-2", "artifact-2");
        var won = await winner.ReleaseAsync();

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.ProjectionPreparation), (lost.Outcome, lost.FailedStep));
        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.SlotTransition), (won.Outcome, won.FailedStep));
        Assert.Contains("no prepared", won.Diagnostic, StringComparison.Ordinal);
        await loser.AssertConsistentAsync("activation-1");
        await loser.AssertDiscardedAsync("activation-2");
    }

    /// <summary>
    /// The other order: the loser decided to discard before the
    /// winner switched, and discards only after. Its discard is refused, because the activation now serves, so the winner's
    /// <see cref="WorkflowActivationOutcome.Activated"/> holds and the loser reports the activation already active.
    /// </summary>
    private static async Task SameActivationLoserDiscardingAfterTheWinnerSwitchedAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var winner = await StartRaceAsync(open);
        var discard = new Latch();
        var loserStores = open();
        await using var loser = Start(loserStores with { Switch = new InterceptedSwitch(loserStores.Switch) { BeforeDiscard = discard.PassAsync } });
        loser.FailNextPreparation(new InvalidOperationException("The trigger indexer is unavailable."));
        var losing = loser.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(discard.Reached, await Task.WhenAny(losing, discard.Reached));

        var won = await winner.ReleaseAsync();
        discard.Release();
        var lost = await losing;

        Assert.Equal(WorkflowActivationOutcome.Activated, won.Outcome);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, lost.Outcome);
        await loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// One step earlier, in a direction that would look like success: the loser discards the shared
    /// activation after the winner minted its reference and before it prepared. There are no projections to delete yet,
    /// so the discard retires the reference, and the winner prepares and switches as if nothing happened. The winner's
    /// switch resumes the reference in the commit that makes the activation serve, so it never serves with a retired
    /// reference that would leave its artifact to garbage collection.
    /// </summary>
    private static async Task SameActivationLoserDiscardingBeforeTheWinnerPreparesAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var preparation = new Latch();
        await using var winner = Start(open());
        winner.HoldNextPreparation(preparation);
        var winning = winner.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(preparation.Reached, await Task.WhenAny(winning, preparation.Reached));
        await using var loser = Start(open());
        loser.FailNextPreparation(new InvalidOperationException("The trigger indexer is unavailable."));

        Assert.Equal(WorkflowActivationStep.ProjectionPreparation, (await loser.ActivateAsync("activation-2", "artifact-2")).FailedStep);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await loser.FindReferenceAsync("activation-2")).DeletedReason);
        preparation.Release();

        Assert.Equal(WorkflowActivationOutcome.Activated, (await winning).Outcome);
        await loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A cancelled loser (#2274). The loser is cancelled, as a node shutting down mid-reconcile
    /// is, while its switch is in flight, and then finds the slot naming its activation. It cannot tell the winner's switch
    /// from its own, so it hands nothing back: its discard is refused because the activation serves. The winner's
    /// activation-2 stays serving, and its <see cref="WorkflowActivationOutcome.Activated"/> holds.
    /// </summary>
    private static async Task SameActivationLoserCancelledInItsSwitchKeepsTheWinnerAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        await using var race = await StartRaceAsync(open, cancellationToken: cancellation.Token);
        await ActivateAsync(open, "activation-2", "artifact-2");
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(race.ReleaseAsync);
        await race.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The other direction of that cancellation: no other call switched, so the cancelled switch committed nothing. The
    /// call discards its activation and rethrows, and the predecessor keeps serving.
    /// </summary>
    private static async Task ACallCancelledInASwitchThatDidNotCommitDiscardsItsActivationAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        await using var race = await StartRaceAsync(open, cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(race.ReleaseAsync);
        await race.Loser.AssertConsistentAsync("activation-1");
        await race.Loser.AssertDiscardedAsync("activation-2");
    }

    /// <summary>
    /// A switch that commits and is then cancelled, as a provider that observes cancellation while committing is. The call
    /// rethrows the cancellation, and its activation stands, as it would if the process stopped there.
    /// </summary>
    private static async Task ACallCancelledAsItsSwitchCommittedKeepsItsActivationAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        var stores = open();
        await using var node = Start(stores with
        {
            Switch = new InterceptedSwitch(stores.Switch)
            {
                AfterActivate = () =>
                {
                    cancellation.Cancel();
                    return new OperationCanceledException(cancellation.Token);
                }
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.ActivateAsync("activation-2", "artifact-2", cancellation.Token));
        await node.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// Two nodes activate the same artifact at once, and each holds a root-write lease for it (#2274). The loser holds
    /// before its switch while the winner runs to the end and releases its lease. Each call's lease is its own, so the loser
    /// still fences the artifact, its renewal succeeds, and it answers as before; once it has finished too, nothing fences
    /// the artifact.
    /// </summary>
    private static async Task SameActivationLoserKeepsItsLeaseAfterTheWinnerReleasesItsOwnAsync(Func<ActivationStores> open)
    {
        var clock = new FakeTimeProvider(Now);
        var renewals = new FirstRenewal();
        await using var collector = await StartCollectorAsync(open);
        await using var race = await StartRaceAsync(Leased);
        await ActivateAsync(Leased, "activation-2", "artifact-2");

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
    /// A call stops for good inside its root-write lease, once its switch commits. Nothing renews or releases that lease,
    /// and no concurrent call takes it over, since each call takes its own (#2274): a second call for the same activation,
    /// held before its switch meanwhile, runs to the end and releases its own lease without ending the first's, so the
    /// artifact stays fenced from reference garbage collection until the first lease expires, and no longer.
    /// </summary>
    private static async Task LeaseOfACallThatStoppedInsideItExpiresAsync(Func<ActivationStores> open)
    {
        await using var collector = await StartCollectorAsync(open);
        // The stopped process's clock, which nobody advances.
        var stopped = new FakeTimeProvider(Now);
        ActivationStores Stopped() => open() with { LeaseClock = stopped };
        await using var race = await StartRaceAsync(Stopped);
        await StopAfterSwitchAsync(Stopped, "activation-2", "artifact-2");
        await race.ReleaseAsync();
        var expiry = Now.Add(LeaseDuration);

        Assert.Null(await TryBeginCollectingAsync(collector, expiry.AddSeconds(-1)));
        var guard = await TryBeginCollectingAsync(collector, expiry);

        Assert.NotNull(guard);
        Assert.True(await collector.Stores.Executables.CancelDeletionAsync(guard));
    }

    /// <summary>
    /// Starts a call that activates <paramref name="activationId"/> and waits until it is held: once it has prepared, just
    /// before its switch, or with <paramref name="holdBeforeSequence"/> before its sequence, after the checks that answer a
    /// call without one.
    /// </summary>
    private static async Task<Race<WorkflowActivationResult>> StartRaceAsync(
        Func<ActivationStores> open,
        string activationId = "activation-2",
        string artifactId = "artifact-2",
        bool holdBeforeSequence = false,
        CancellationToken cancellationToken = default)
    {
        var latch = new Latch();
        var stores = open();
        var held = holdBeforeSequence
            ? Start(stores, beforeSequence: latch.PassAsync)
            : Start(stores with { Switch = new InterceptedSwitch(stores.Switch) { BeforeActivate = latch.PassAsync } });
        var call = held.ActivateAsync(activationId, artifactId, cancellationToken);
        Assert.Same(latch.Reached, await Task.WhenAny(call, latch.Reached));
        return new(held, call, latch);
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
