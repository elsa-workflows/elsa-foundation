using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Two completions of one slot at once, as two nodes starting together run them (#2265). One completion is held at a
/// point inside an EF projection store while the other runs to the end, then let go: the interleaving is fixed, not
/// left to timing. Both must succeed, and the slot, the projections and the references must agree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConcurrentCompletionScenarios"/> hold a completion outside any transaction, between reading an activation's
/// trigger-binding projection state and reading its rows, so they run on every provider.
/// </para>
/// <para>
/// <see cref="ConcurrentSwitchScenarios"/> hold a projection switch inside its transaction: after it read the state and
/// before it read the rows, or after it decided and before it wrote. SQLite begins every transaction with
/// <c>BEGIN IMMEDIATE</c>, so a held switch holds the write lock and the other completion cannot switch until it ends;
/// the race cannot happen there, and these run on PostgreSQL, where a read committed transaction holds no lock until it
/// writes.
/// </para>
/// </remarks>
internal static partial class WorkflowActivationCrashRepairContract
{
    private static readonly Dictionary<string, Func<Func<IInterceptor[], ActivationStores>, Task>> Concurrent = new()
    {
        ["a-completion-that-read-the-projection-state-before-another-completed-it-converges"] = ACompletionThatReadTheStateBeforeAnotherCompletedItConvergesAsync,
        ["two-nodes-starting-together-complete-an-interrupted-replacement-without-a-failure"] = TwoNodesStartingTogetherCompleteAnInterruptedReplacementAsync
    };

    private static readonly Dictionary<string, Func<Func<IInterceptor[], ActivationStores>, Task>> ConcurrentSwitches = new()
    {
        ["a-trigger-switch-that-read-its-state-before-another-completion-switched-converges"] = open =>
            AHeldSwitchConvergesAsync(open, latch => new HoldBetweenStateAndRows(RuntimeTriggerBindingEfModule.ProjectionStateTableName, inTransaction: true, latch)),
        ["a-trigger-switch-that-wrote-after-another-completion-switched-converges"] = open =>
            AHeldSwitchConvergesAsync(open, latch => new HoldBeforeSwitchWrite<WorkflowTriggerBindingProjectionStateEntity>(latch)),
        ["a-schedule-switch-that-read-its-state-before-another-completion-switched-converges"] = open =>
            AHeldSwitchConvergesAsync(open, latch => new HoldBetweenStateAndRows(RuntimeOperationalStateEfModule.RecurringScheduleProjectionStateTableName, inTransaction: true, latch)),
        ["a-schedule-switch-that-wrote-after-another-completion-switched-converges"] = open =>
            AHeldSwitchConvergesAsync(open, latch => new HoldBeforeSwitchWrite<RecurringTriggerScheduleProjectionStateEntity>(latch))
    };

    public static TheoryData<string> ConcurrentCompletionScenarios => ToTheoryData(Concurrent.Keys);

    public static TheoryData<string> ConcurrentSwitchScenarios => ToTheoryData(ConcurrentSwitches.Keys);

    /// <summary>Runs a scenario of either set. <paramref name="open"/> starts a process whose context carries the given interceptors.</summary>
    public static Task RunConcurrentAsync(string scenario, Func<IInterceptor[], ActivationStores> open) =>
        (Concurrent.TryGetValue(scenario, out var run) ? run : ConcurrentSwitches[scenario])(open);

    private static TheoryData<string> ToTheoryData(IEnumerable<string> scenarios)
    {
        var data = new TheoryData<string>();
        foreach (var scenario in scenarios)
            data.Add(scenario);
        return data;
    }

    /// <summary>
    /// One completion reads the slot's activation prepared, and another completes it before the first reads its trigger
    /// bindings, so the first reads rows that serve under a state that does not. That is a concurrent switch, not a corrupt
    /// projection: the first completion reads again, finds nothing left to do and says so, rather than reporting the slot's
    /// activation failed (projection_activation_failed) while it serves. Rows that disagree with a state that stood still
    /// are still refused as corrupt; <c>EfWorkflowTriggerBindingStoreTests</c> covers that direction.
    /// </summary>
    private static async Task ACompletionThatReadTheStateBeforeAnotherCompletedItConvergesAsync(Func<IInterceptor[], ActivationStores> open)
    {
        await using var race = await HoldACompletionOfAnInterruptedReplacementAsync(
            open,
            latch => new HoldBetweenStateAndRows(RuntimeTriggerBindingEfModule.ProjectionStateTableName, inTransaction: false, latch),
            CompleteSlotAsync);
        await using var other = Start(open([]));
        var completed = await CompleteSlotAsync(other);

        var result = await race.ReleaseAsync();

        Assert.Equal((WorkflowActivationOutcome.Activated, "activation-1"), (completed.Outcome, completed.ReplacedActivationId));
        Assert.Equal((WorkflowActivationOutcome.AlreadyActive, (string?)null), (result.Outcome, result.Diagnostic));
        await AssertConvergedWithoutErrorsAsync(race.Loser, other);
    }

    /// <summary>
    /// Every node runs the shell-start pass, so two nodes starting together both complete an interrupted replacement. The
    /// first is held where the previous scenario holds it while the second's pass runs to the end. Neither pass logs a
    /// completion failure.
    /// </summary>
    private static async Task TwoNodesStartingTogetherCompleteAnInterruptedReplacementAsync(Func<IInterceptor[], ActivationStores> open)
    {
        await using var race = await HoldACompletionOfAnInterruptedReplacementAsync(
            open,
            latch => new HoldBetweenStateAndRows(RuntimeTriggerBindingEfModule.ProjectionStateTableName, inTransaction: false, latch),
            async node =>
            {
                await node.StartShellAsync();
                return true;
            });
        await using var other = Start(open([]));
        await other.StartShellAsync();

        await race.ReleaseAsync();

        await AssertConvergedWithoutErrorsAsync(race.Loser, other);
    }

    /// <summary>
    /// Both completions find the slot's activation prepared and decide to switch it. One is held inside its projection
    /// switch, having read the projection state but not its rows, or having decided but not written, while the other
    /// completes the activation. The held switch then reads rows switched under a state it read before, or writes over a
    /// revision that moved; either way it reads again, finds the switch made, and carries on as a no-op. Without that,
    /// the store reports the projection corrupt or changed concurrently, and the completion fails.
    /// </summary>
    private static async Task AHeldSwitchConvergesAsync(Func<IInterceptor[], ActivationStores> open, Func<Latch, IInterceptor> hold)
    {
        await using var race = await HoldACompletionOfAnInterruptedReplacementAsync(open, hold, CompleteSlotAsync);
        await using var other = Start(open([]));
        var completed = await CompleteSlotAsync(other);

        var result = await race.ReleaseAsync();

        Assert.Equal((WorkflowActivationOutcome.Activated, "activation-1"), (completed.Outcome, completed.ReplacedActivationId));
        Assert.Equal((WorkflowActivationOutcome.Activated, "activation-1", (string?)null), (result.Outcome, result.ReplacedActivationId, result.Diagnostic));
        await AssertConvergedWithoutErrorsAsync(race.Loser, other);
    }

    /// <summary>
    /// Leaves activation-2 interrupted after replacing activation-1, then starts <paramref name="complete"/> in a process
    /// whose context carries <paramref name="hold"/>, and waits until it is held there.
    /// </summary>
    private static async Task<Race<T>> HoldACompletionOfAnInterruptedReplacementAsync<T>(
        Func<IInterceptor[], ActivationStores> open,
        Func<Latch, IInterceptor> hold,
        Func<ActivationNode, Task<T>> complete)
    {
        await ActivateAsync(() => open([]), "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(() => open([]), "activation-2", "artifact-2");
        var latch = new Latch();
        var held = Start(open([hold(latch)]));
        var completing = complete(held);
        Assert.Same(latch.Reached, await Task.WhenAny(completing, latch.Reached));
        return new(held, completing, latch);
    }

    private static Task<WorkflowActivationResult> CompleteSlotAsync(ActivationNode node) => node.Coordinator.CompleteAsync(DefinitionId, SlotName).AsTask();

    private static async Task AssertConvergedWithoutErrorsAsync(ActivationNode node, ActivationNode other)
    {
        await node.AssertConsistentAsync("activation-2", "activation-1");
        Assert.DoesNotContain(node.Log.Entries.Concat(other.Log.Entries), entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>
    /// Holds a process once, at <c>latch</c>, just before it reads an activation's rows after reading its projection state
    /// from <c>stateTable</c>: the state is read and the rows are not. <c>inTransaction</c> picks the read a projection
    /// switch makes inside its transaction over the reads a completion makes outside one before it switches.
    /// </summary>
    private sealed class HoldBetweenStateAndRows(string stateTable, bool inTransaction, Latch latch) : DbCommandInterceptor
    {
        private bool _stateRead;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_stateRead)
                await latch.PassAsync();
            _stateRead = command.CommandText.Contains(stateTable, StringComparison.Ordinal) && (command.Transaction is not null) == inTransaction;
            return result;
        }
    }

    /// <summary>Holds a process once, at <c>latch</c>, just before it saves a switch of <typeparamref name="TState"/>: it has decided and not written.</summary>
    private sealed class HoldBeforeSwitchWrite<TState>(Latch latch) : SaveChangesInterceptor where TState : class
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TState>().Any(entry => entry.State == EntityState.Modified))
                await latch.PassAsync();
            return result;
        }
    }
}
