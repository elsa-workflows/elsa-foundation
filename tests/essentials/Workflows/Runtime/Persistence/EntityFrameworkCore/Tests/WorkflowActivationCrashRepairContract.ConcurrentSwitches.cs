using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Another writer commits inside a switch's or a discard's own transaction (#2230): each is held once it has read and
/// staged, just before it writes, while the other commits, then let go. The interleaving is fixed, not left to timing.
/// </summary>
/// <remarks>
/// These are the races the EF switch closes with its concurrency tokens rather than with the order of the coordinator's
/// steps, which the other scenarios fix. SQLite begins every transaction with <c>BEGIN IMMEDIATE</c>, so a held
/// transaction holds the write lock and the other writer cannot commit until it ends; the race cannot happen there, and
/// these run on PostgreSQL, where a read committed transaction holds no lock until it writes.
/// </remarks>
internal static partial class WorkflowActivationCrashRepairContract
{
    private static readonly Dictionary<string, Func<Func<IInterceptor[], ActivationStores>, Task>> ConcurrentSwitches = new()
    {
        ["a-switch-whose-slot-another-switch-moved-before-it-wrote-is-refused"] = ASwitchWhoseSlotAnotherSwitchMovedIsRefusedAsync,
        ["a-discard-that-read-a-prepared-activation-before-a-switch-made-it-serve-is-refused"] = ADiscardThatReadAPreparedActivationBeforeASwitchIsRefusedAsync,
        ["a-switch-that-read-a-prepared-activation-before-a-discard-deleted-it-moves-nothing"] = ASwitchThatReadAPreparedActivationBeforeADiscardMovesNothingAsync
    };

    public static TheoryData<string> ConcurrentSwitchScenarios => ToTheoryData(ConcurrentSwitches.Keys);

    /// <summary>Runs a scenario. <paramref name="open"/> starts a process whose context carries the given interceptors.</summary>
    public static Task RunConcurrentAsync(string scenario, Func<IInterceptor[], ActivationStores> open) => ConcurrentSwitches[scenario](open);

    /// <summary>
    /// Two activations switch one slot at once. One has staged its switch and not written when the other commits. Its write
    /// loses to the slot revision the other moved, so it reads again, is refused, and discards its own activation: one
    /// activation wins, and only it serves.
    /// </summary>
    private static async Task ASwitchWhoseSlotAnotherSwitchMovedIsRefusedAsync(Func<IInterceptor[], ActivationStores> open)
    {
        await ActivateAsync(() => open([]), "activation-1", "artifact-1");
        await using var held = await HoldAtWriteAsync(open, IsStaged<WorkflowActivationSlotEntity>(EntityState.Modified), node => node.ActivateAsync("activation-2", "artifact-2"));
        await using var other = Start(open([]));

        Assert.Equal(WorkflowActivationOutcome.Activated, (await other.ActivateAsync("activation-3", "artifact-3")).Outcome);
        var result = await held.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.Conflict, result.Outcome);
        await other.AssertConsistentAsync("activation-3", "activation-1");
        await other.AssertDiscardedAsync("activation-2");
    }

    /// <summary>
    /// The EF fence of #2251's window. A call that shares the winner's activation id failed and is discarding it: it read
    /// the projection prepared and staged its deletion. The winner's switch commits meanwhile. The discard's write loses to
    /// the revision the switch moved, so it reads again, finds the activation serving, and is refused, and the call reports
    /// it already active instead of deleting the projections the slot now serves through.
    /// </summary>
    private static async Task ADiscardThatReadAPreparedActivationBeforeASwitchIsRefusedAsync(Func<IInterceptor[], ActivationStores> open)
    {
        await ActivateAsync(() => open([]), "activation-1", "artifact-1");
        await using var winner = await StartRaceAsync(() => open([]));
        await using var loser = await HoldAtWriteAsync(
            open,
            IsStaged<WorkflowTriggerBindingProjectionStateEntity>(EntityState.Deleted),
            node =>
            {
                node.FailNextPreparation(new InvalidOperationException("The trigger indexer is unavailable."));
                return node.ActivateAsync("activation-2", "artifact-2");
            });

        Assert.Equal(WorkflowActivationOutcome.Activated, (await winner.ReleaseAsync()).Outcome);
        var lost = await loser.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, lost.Outcome);
        await loser.Loser.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The other order at the fence: the winner read the activation prepared and staged its switch, and the loser's discard
    /// deletes the projection meanwhile. The switch's write loses, it reads again, finds nothing prepared, and fails loudly
    /// having moved nothing; the predecessor keeps serving.
    /// </summary>
    private static async Task ASwitchThatReadAPreparedActivationBeforeADiscardMovesNothingAsync(Func<IInterceptor[], ActivationStores> open)
    {
        await ActivateAsync(() => open([]), "activation-1", "artifact-1");
        await using var winner = await HoldAtWriteAsync(open, IsStaged<WorkflowActivationSlotEntity>(EntityState.Modified), node => node.ActivateAsync("activation-2", "artifact-2"));
        await using var loser = Start(open([]));
        loser.FailNextPreparation(new InvalidOperationException("The trigger indexer is unavailable."));

        Assert.Equal(WorkflowActivationStep.ProjectionPreparation, (await loser.ActivateAsync("activation-2", "artifact-2")).FailedStep);
        var won = await winner.ReleaseAsync();

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.SlotTransition), (won.Outcome, won.FailedStep));
        await loser.AssertConsistentAsync("activation-1");
        await loser.AssertDiscardedAsync("activation-2");
    }

    /// <summary>Starts <paramref name="call"/> in a process held once, just before it saves changes that <paramref name="when"/> picks out.</summary>
    private static async Task<Race<WorkflowActivationResult>> HoldAtWriteAsync(
        Func<IInterceptor[], ActivationStores> open,
        Func<ChangeTracker, bool> when,
        Func<ActivationNode, Task<WorkflowActivationResult>> call)
    {
        var latch = new Latch();
        var node = Start(open([new HoldBeforeSave(latch, when)]));
        var pending = call(node);
        Assert.Same(latch.Reached, await Task.WhenAny(pending, latch.Reached));
        return new(node, pending, latch);
    }

    private static Func<ChangeTracker, bool> IsStaged<TEntity>(EntityState state) where TEntity : class =>
        tracker => tracker.Entries<TEntity>().Any(entry => entry.State == state);

    /// <summary>Holds a process once, at <c>latch</c>, as it is about to save changes that <c>when</c> picks out: it has read and staged, and not written.</summary>
    private sealed class HoldBeforeSave(Latch latch, Func<ChangeTracker, bool> when) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (when(eventData.Context!.ChangeTracker))
                await latch.PassAsync();
            return result;
        }
    }
}
