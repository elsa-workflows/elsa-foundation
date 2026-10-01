using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The activation crash window of #2193, written once and run against the in-memory stores and EF Core on SQLite
/// (<c>WorkflowActivationCrashRepairContractTests</c>) and on PostgreSQL (the provider tests).
/// </summary>
/// <remarks>
/// A crash is a process that stops for good once its slot transition commits (<see cref="PauseAfterSlotTransition"/>
/// with nothing to resume it), so neither the projection switch nor any compensation runs. An in-flight activation is
/// the same pause, resumed later. Each scenario then works through another process over the same durable state and
/// checks what serves, through the queries the stimulus router and the recurring pump use.
/// </remarks>
internal static class WorkflowActivationCrashRepairContract
{
    private const string DefinitionId = "definition-crash";
    private const string SlotName = "default";
    private const string NodeId = "node-start";
    private const string StimulusType = "Event";
    private const string StimulusHash = "crash-window";
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly string SlotId = WorkflowActivationSlotIdentity.Create(DefinitionId, SlotName);

    private static readonly Dictionary<string, Func<Func<ActivationStores>, Task>> All = new()
    {
        ["same-artifact-completes-an-interrupted-first-activation"] = SameArtifactCompletesAnInterruptedFirstActivationAsync,
        ["same-artifact-completes-an-interrupted-replacement"] = SameArtifactCompletesAnInterruptedReplacementAsync,
        ["newer-artifact-replaces-an-interrupted-replacement"] = NewerArtifactReplacesAnInterruptedReplacementAsync,
        ["deactivating-an-interrupted-replacement-serves-nothing"] = DeactivatingAnInterruptedReplacementServesNothingAsync,
        ["completion-finishes-after-the-trigger-store-switched"] = CompletionFinishesAfterTheTriggerStoreSwitchedAsync,
        ["shell-start-completes-an-interrupted-replacement"] = ShellStartCompletesAnInterruptedReplacementAsync,
        ["completion-leaves-a-prepared-candidate-alone"] = CompletionLeavesAPreparedCandidateAloneAsync,
        ["completion-writes-nothing-once-another-node-moved-the-slot"] = CompletionWritesNothingOnceAnotherNodeMovedTheSlotAsync,
        ["completion-refuses-two-serving-candidates"] = CompletionRefusesTwoServingCandidatesAsync,
        ["cancelled-completion-is-rethrown-and-resumed-later"] = CancelledCompletionIsRethrownAndResumedLaterAsync,
        ["projection-switch-is-a-no-op-once-made"] = ProjectionSwitchIsANoOpOnceMadeAsync,
        ["in-flight-activation-and-a-concurrent-completion-agree"] = InFlightActivationAndAConcurrentCompletionAgreeAsync,
        ["in-flight-activation-failing-after-a-completion-restores-the-predecessor"] = InFlightActivationFailingAfterACompletionRestoresThePredecessorAsync,
        ["completion-retires-a-replaced-reference-left-live"] = CompletionRetiresAReplacedReferenceLeftLiveAsync,
        ["projection-switch-is-refused-once-the-replaced-activation-is-off"] = ProjectionSwitchIsRefusedOnceTheReplacedActivationIsOffAsync,
        ["retry-refuses-projections-a-failed-compensation-left-behind"] = RetryRefusesProjectionsAFailedCompensationLeftBehindAsync,
        ["completion-leaves-a-predecessor-that-compensation-restored"] = CompletionLeavesAPredecessorThatCompensationRestoredAsync,
        ["deactivating-turns-off-a-serving-activation-whose-reference-is-retired"] = DeactivatingTurnsOffAServingActivationWhoseReferenceIsRetiredAsync
    };

    public static TheoryData<string> Scenarios
    {
        get
        {
            var scenarios = new TheoryData<string>();
            foreach (var scenario in All.Keys)
                scenarios.Add(scenario);
            return scenarios;
        }
    }

    public static Task RunAsync(string scenario, Func<ActivationStores> open) => All[scenario](open);

    /// <summary>
    /// The direction that looked like success: the slot names the activation and its reference is live, yet nothing
    /// serves. Activating the same artifact again, as the next reconcile does, completes it before answering.
    /// </summary>
    private static async Task SameArtifactCompletesAnInterruptedFirstActivationAsync(Func<ActivationStores> open)
    {
        await StopAfterSlotTransitionAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        Assert.Equal("activation-1", await node.SlotActivationAsync());
        await node.AssertServingAsync();

        var result = await node.ActivateAsync("activation-1-retry", "artifact-1");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        await node.AssertServingAsync("activation-1");
    }

    /// <summary>
    /// An interrupted replacement keeps its predecessor serving although the slot names the candidate. Completing it
    /// switches serving over and retires the predecessor's reference.
    /// </summary>
    private static async Task SameArtifactCompletesAnInterruptedReplacementAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        Assert.Equal("activation-2", await node.SlotActivationAsync());
        await node.AssertServingAsync("activation-1");

        var result = await node.ActivateAsync("activation-2", "artifact-2");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        await node.AssertServingAsync("activation-2");
        await node.AssertRetiredAsync("activation-1");
        await node.AssertLiveAsync("activation-2");
    }

    /// <summary>
    /// Replacing an interrupted activation completes it first. Without that, the durable stores refuse to switch off a
    /// replaced activation that never served, and the in-memory stores would leave the predecessor serving too.
    /// </summary>
    private static async Task NewerArtifactReplacesAnInterruptedReplacementAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());

        var result = await node.ActivateAsync("activation-3", "artifact-3");

        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("activation-2", result.ReplacedActivationId);
        await node.AssertServingAsync("activation-3");
        await node.AssertRetiredAsync("activation-1");
        await node.AssertRetiredAsync("activation-2");
    }

    /// <summary>
    /// Deactivating an interrupted activation completes nothing first. It turns off the activation the slot names and
    /// the one it replaced, which still serves, and retires that one's reference.
    /// </summary>
    private static async Task DeactivatingAnInterruptedReplacementServesNothingAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());

        var result = await node.DeactivateAsync("artifact-2");

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Null(await node.SlotActivationAsync());
        await node.AssertServingAsync();
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Missing);
        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Missing);
        await node.AssertRetiredAsync("activation-1");
    }

    /// <summary>
    /// A process can also stop between the two projection stores. The trigger store has already switched, so
    /// completing it again must be a no-op rather than a refusal; only the schedules still switch.
    /// </summary>
    private static async Task CompletionFinishesAfterTheTriggerStoreSwitchedAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        await node.Stores.Bindings.ActivateAsync("activation-2", "activation-1");

        var result = await node.Coordinator.CompleteAsync(DefinitionId, SlotName);

        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("activation-1", result.ReplacedActivationId);
        await node.AssertServingAsync("activation-2");
        await node.AssertRetiredAsync("activation-1");
    }

    /// <summary>
    /// Nothing calls the coordinator again for a workflow published from the designer, so shell start completes it.
    /// A second start finds nothing left to do.
    /// </summary>
    private static async Task ShellStartCompletesAnInterruptedReplacementAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());

        await node.StartShellAsync();

        await node.AssertServingAsync("activation-2");
        await node.AssertRetiredAsync("activation-1");
        await node.StartShellAsync();
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await node.Coordinator.CompleteAsync(DefinitionId, SlotName)).Outcome);
        await node.AssertServingAsync("activation-2");
    }

    /// <summary>
    /// A concurrent candidate that has minted its reference and prepared its projection, but not yet won the slot, is
    /// not the replaced activation. Completion leaves its projection prepared and its reference live.
    /// </summary>
    private static async Task CompletionLeavesAPreparedCandidateAloneAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        await node.PrepareCandidateAsync("activation-3", "artifact-3");

        var result = await node.Coordinator.CompleteAsync(DefinitionId, SlotName);

        Assert.Equal("activation-1", result.ReplacedActivationId);
        await node.AssertServingAsync("activation-2");
        await node.AssertProjectionsAsync("activation-3", WorkflowActivationProjectionState.Prepared);
        await node.AssertLiveAsync("activation-3");
    }

    /// <summary>
    /// Completion must not race a writer that moves the slot on. Another node completes the interrupted first
    /// activation itself and replaces it while this node is still looking for the activation it replaced. This node
    /// reads the slot again before writing, sees it moved, and writes nothing, so the replaced activation is not
    /// switched back on beside its successor.
    /// </summary>
    private static async Task CompletionWritesNothingOnceAnotherNodeMovedTheSlotAsync(Func<ActivationStores> open)
    {
        await StopAfterSlotTransitionAsync(open, "activation-1", "artifact-1");
        await using var other = Start(open());
        await using var node = Start(open(), beforePredecessorScan: async () =>
            Assert.Equal(WorkflowActivationOutcome.Activated, (await other.ActivateAsync("activation-2", "artifact-2")).Outcome));

        var result = await node.Coordinator.CompleteAsync(DefinitionId, SlotName);

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Equal("activation-2", await node.SlotActivationAsync());
        await node.AssertServingAsync("activation-2");
        await node.AssertRetiredAsync("activation-1");
    }

    /// <summary>
    /// When two activations besides the slot's own still serve its slot, the replaced one cannot be told apart.
    /// Completion fails loudly and switches nothing rather than guess, and a same-artifact activation reports that
    /// failure instead of "already active". Unpublishing still succeeds and turns every one of them off: that is how an
    /// operator clears the slot.
    /// </summary>
    private static async Task CompletionRefusesTwoServingCandidatesAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using (var stray = Start(open()))
        {
            await stray.PrepareCandidateAsync("activation-stray", "artifact-stray");
            await stray.SwitchAsync("activation-stray", null);
        }

        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());

        var result = await node.Coordinator.CompleteAsync(DefinitionId, SlotName);

        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(WorkflowActivationStep.ProjectionActivation, result.FailedStep);
        Assert.Contains("activation-1", result.Diagnostic);
        Assert.Contains("activation-stray", result.Diagnostic);
        Assert.Equal(WorkflowActivationOutcome.Failed, (await node.ActivateAsync("activation-2", "artifact-2")).Outcome);
        await node.AssertServingAsync("activation-1", "activation-stray");
        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Prepared);

        Assert.Equal(WorkflowActivationOutcome.Deactivated, (await node.DeactivateAsync("artifact-2")).Outcome);
        await node.AssertServingAsync();
        await node.AssertRetiredAsync("activation-1");
        await node.AssertRetiredAsync("activation-stray");
    }

    /// <summary>
    /// A cancelled completion is rethrown as a cancellation, not reported as a failure, and leaves the activation for
    /// the next call to complete.
    /// </summary>
    private static async Task CancelledCompletionIsRethrownAndResumedLaterAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        using var cancellation = new CancellationTokenSource();
        await using var node = Start(open(), beforePredecessorScan: () => cancellation.CancelAsync());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await node.Coordinator.CompleteAsync(DefinitionId, SlotName, cancellation.Token));
        await node.AssertServingAsync("activation-1");

        Assert.Equal(WorkflowActivationOutcome.Activated, (await node.Coordinator.CompleteAsync(DefinitionId, SlotName)).Outcome);
        await node.AssertServingAsync("activation-2");
    }

    /// <summary>
    /// Every projection store reports an activation's lifecycle, and switching an activation on that already serves,
    /// with its replaced activation already off, is a no-op success. A completion and the activation's own sequence
    /// both make that switch, in either order.
    /// </summary>
    private static async Task ProjectionSwitchIsANoOpOnceMadeAsync(Func<ActivationStores> open)
    {
        await using var node = Start(open());
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Missing);
        await node.PrepareCandidateAsync("activation-1", "artifact-1");
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Prepared);
        await node.SwitchAsync("activation-1", null);
        await node.PrepareCandidateAsync("activation-2", "artifact-2");

        for (var attempt = 0; attempt < 2; attempt++)
            await node.SwitchAsync("activation-2", "activation-1");

        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Active);
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Replaced);
        await node.AssertServingAsync("activation-2");
    }

    /// <summary>
    /// An activation still running on one node, past its slot transition, races a completion of that activation on
    /// another. Whichever switches the projections second finds the switch made and carries on, so both succeed and the
    /// slot, the projections and the references agree.
    /// </summary>
    private static async Task InFlightActivationAndAConcurrentCompletionAgreeAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var stores = open();
        var authority = new PauseAfterSlotTransition(stores.Authority, resume.Task);
        await using var inFlight = Start(stores with { Authority = authority });
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(authority.Paused, await Task.WhenAny(activation, authority.Paused));
        await using var other = Start(open());

        Assert.Equal(WorkflowActivationOutcome.Activated, (await other.Coordinator.CompleteAsync(DefinitionId, SlotName)).Outcome);
        resume.SetResult();
        var result = await activation;

        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        await other.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// The same race, but the in-flight activation then fails after the completion has retired the predecessor's
    /// reference. Its compensation puts the predecessor back in the slot and its projections back on, and must restore
    /// that reference too, or the slot would serve an activation whose reference reads as retired.
    /// </summary>
    private static async Task InFlightActivationFailingAfterACompletionRestoresThePredecessorAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var stores = open();
        var authority = new PauseAfterSlotTransition(stores.Authority, resume.Task);
        await using var inFlight = Start(stores with { Authority = authority }, observer: new FailOnceObserver());
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(authority.Paused, await Task.WhenAny(activation, authority.Paused));
        await using var other = Start(open());

        Assert.Equal(WorkflowActivationOutcome.Activated, (await other.Coordinator.CompleteAsync(DefinitionId, SlotName)).Outcome);
        await other.AssertRetiredAsync("activation-1");
        resume.SetResult();
        var result = await activation;

        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.Null(result.CompensationDiagnostic);
        await other.AssertConsistentAsync("activation-1");
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await other.FindReferenceAsync("activation-2")).DeletedReason);
    }

    /// <summary>
    /// A process can also stop after the projection switch but before step 6, leaving the replaced activation serving
    /// nothing with a live reference. The next activation of the slot retires it, and leaves a prepared concurrent
    /// candidate alone.
    /// </summary>
    private static async Task CompletionRetiresAReplacedReferenceLeftLiveAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        await node.SwitchAsync("activation-2", "activation-1");
        await node.PrepareCandidateAsync("activation-3", "artifact-3");
        await node.AssertServingAsync("activation-2");
        await node.AssertLiveAsync("activation-1");

        var result = await node.ActivateAsync("activation-2", "artifact-2");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        await node.AssertConsistentAsync("activation-2", "activation-1");
        await node.AssertLiveAsync("activation-3");
        await node.AssertProjectionsAsync("activation-3", WorkflowActivationProjectionState.Prepared);
    }

    /// <summary>
    /// Every projection store, the in-memory ones included, refuses to switch on a candidate whose replaced activation no
    /// longer serves, which fences a late completion, and refuses a candidate that serves beside its replaced activation.
    /// Nothing changes on a refusal.
    /// </summary>
    private static async Task ProjectionSwitchIsRefusedOnceTheReplacedActivationIsOffAsync(Func<ActivationStores> open)
    {
        await using var node = Start(open());
        await node.PrepareCandidateAsync("activation-1", "artifact-1");
        await node.SwitchAsync("activation-1", null);
        await node.PrepareCandidateAsync("activation-2", "artifact-2");
        await node.SwitchAsync("activation-2", "activation-1");
        await node.PrepareCandidateAsync("activation-3", "artifact-3");

        await node.AssertSwitchRefusedAsync("activation-3", "activation-1");
        await node.AssertSwitchRefusedAsync("activation-3", "activation-missing");
        await node.SwitchAsync("activation-3", null);
        await node.AssertSwitchRefusedAsync("activation-3", "activation-2");

        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Replaced);
        await node.AssertServingAsync("activation-2", "activation-3");
    }

    /// <summary>
    /// A double fault: an activation fails after its projection switch, and its compensation hands the slot back but
    /// cannot delete the candidate's projections, so they stay stored as replaced. A retry must not reuse them. They read
    /// as an activation that served and was replaced, so a completion of the slot that runs once the retry has resumed its
    /// reference would retire that reference, and the retry would then win the slot and serve with it retired. Preparing
    /// the activation again is refused instead: the retry fails loudly, its compensation deletes the projections, and the
    /// next retry activates cleanly.
    /// </summary>
    private static async Task RetryRefusesProjectionsAFailedCompensationLeftBehindAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var failingStores = open();
        await using (var failing = Start(failingStores with { Bindings = new BindingsThatCannotDelete(failingStores.Bindings) }, observer: new FailOnceObserver()))
        {
            var failed = await failing.ActivateAsync("activation-2", "artifact-2");
            Assert.Equal(WorkflowActivationOutcome.Failed, failed.Outcome);
            Assert.Contains("Candidate projection compensation failed", failed.CompensationDiagnostic);
        }

        await using var other = Start(open());
        await other.AssertConsistentAsync("activation-1");
        await other.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Replaced);
        var retryStores = open();
        var reference = WorkflowActivationReferenceIdentity.Create("activation-2");
        await using var retry = Start(retryStores with
        {
            Authority = new AfterSlotRead(
                retryStores.Authority,
                async () => (await retryStores.References.FindAsync(reference))?.DeletedAt is null,
                () => other.Coordinator.CompleteAsync(DefinitionId, SlotName).AsTask())
        });

        var result = await retry.ActivateAsync("activation-2", "artifact-2");

        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(WorkflowActivationStep.ProjectionPreparation, result.FailedStep);
        Assert.Contains("cannot be prepared again", result.Diagnostic);
        Assert.Null(result.CompensationDiagnostic);
        await other.AssertConsistentAsync("activation-1");
        await other.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Missing);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await other.Coordinator.CompleteAsync(DefinitionId, SlotName)).Outcome);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await other.FindReferenceAsync("activation-2")).DeletedReason);

        Assert.Equal(WorkflowActivationOutcome.Activated, (await other.ActivateAsync("activation-2", "artifact-2")).Outcome);
        await other.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A completion switches an in-flight activation's projections on and reads the slot one last time before retiring
    /// the predecessor's reference. Just after that read, the in-flight activation fails and its compensation hands the
    /// slot and the projections back to the predecessor, finding its reference still live. Completion reads the
    /// predecessor's projection state again before retiring, finds it serving, and leaves its reference alone; otherwise
    /// the slot would serve an activation whose reference reads as retired.
    /// </summary>
    private static async Task CompletionLeavesAPredecessorThatCompensationRestoredAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var inFlightStores = open();
        var pause = new PauseAfterSlotTransition(inFlightStores.Authority, resume.Task);
        await using var inFlight = Start(inFlightStores with { Authority = pause }, observer: new FailOnceObserver());
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(pause.Paused, await Task.WhenAny(activation, pause.Paused));
        var otherStores = open();
        await using var other = Start(otherStores with
        {
            Authority = new AfterSlotRead(
                otherStores.Authority,
                async () => await otherStores.Bindings.FindActivationStateAsync("activation-2") == WorkflowActivationProjectionState.Active,
                async () =>
                {
                    resume.SetResult();
                    Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, (await activation).FailedStep);
                })
        });

        var completion = await other.Coordinator.CompleteAsync(DefinitionId, SlotName);

        Assert.Equal(WorkflowActivationOutcome.Activated, completion.Outcome);
        Assert.Null(completion.ReplacedActivationId);
        Assert.Null((await activation).CompensationDiagnostic);
        await other.AssertConsistentAsync("activation-1");
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await other.FindReferenceAsync("activation-2")).DeletedReason);
    }

    /// <summary>
    /// The activation an interrupted replacement replaced still serves, but its reference was retired. The projection
    /// stores still name it for the slot, so unpublishing turns it off too, rather than leaving it serving with nothing
    /// that would ever find it again.
    /// </summary>
    private static async Task DeactivatingTurnsOffAServingActivationWhoseReferenceIsRetiredAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSlotTransitionAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        await node.Stores.References.RetireAsync(WorkflowActivationReferenceIdentity.Create("activation-1"), Now, "retired-elsewhere");
        Assert.Equal(["activation-1"], await node.Stores.Bindings.ListServingActivationIdsAsync(SlotId));
        Assert.Equal(["activation-1"], await node.Stores.Schedules.ListServingActivationIdsAsync(SlotId));

        var result = await node.DeactivateAsync("artifact-2");

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        await node.AssertServingAsync();
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Missing);
        Assert.Empty(await node.Stores.Bindings.ListServingActivationIdsAsync(SlotId));
        Assert.Empty(await node.Stores.Schedules.ListServingActivationIdsAsync(SlotId));
    }

    private static ActivationNode Start(
        ActivationStores stores,
        Func<Task>? beforePredecessorScan = null,
        IWorkflowTriggerIndexObserver? observer = null) =>
        new(stores, beforePredecessorScan, observer);

    private static async Task ActivateAsync(Func<ActivationStores> open, string activationId, string artifactId)
    {
        await using var node = Start(open());
        Assert.Equal(WorkflowActivationOutcome.Activated, (await node.ActivateAsync(activationId, artifactId)).Outcome);
    }

    /// <summary>Runs an activation in a process that stops for good once its slot transition commits.</summary>
    private static async Task StopAfterSlotTransitionAsync(Func<ActivationStores> open, string activationId, string artifactId)
    {
        var stores = open();
        var authority = new PauseAfterSlotTransition(stores.Authority);
        await using var node = Start(stores with { Authority = authority });

        var activation = node.ActivateAsync(activationId, artifactId);

        Assert.Same(authority.Paused, await Task.WhenAny(activation, authority.Paused));
    }

    private static WorkflowExecutable Executable(string artifactId) => new(
        new WorkflowExecutableIdentity(artifactId, DefinitionId, "version-1", "1.0.0", $"sha256:{artifactId}"),
        new ExecutableNode(
            NodeId,
            NodeId,
            "test/activity",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        Now,
        new Dictionary<string, string>(),
        IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutableSourceReference Reference(string artifactId) => new(
        SourceReferenceId: "caller-reference",
        ArtifactId: artifactId,
        SourceKind: "workflow-definition-version",
        SourceId: "version-1",
        SourceVersion: "1.0.0",
        DefinitionId: DefinitionId,
        DefinitionVersionId: "version-1",
        ArtifactVersion: "1.0.0",
        CreatedAt: Now,
        PublishedAt: Now,
        Scope: WorkflowExecutableReferenceScope.Published);

    /// <summary>One process: a coordinator and a shell-start pass over its own view of the durable stores.</summary>
    private sealed class ActivationNode : IAsyncDisposable
    {
        private readonly OneTriggerIndexer _indexer;
        private readonly CompleteInterruptedActivationsStartupTask _shellStart;

        public ActivationNode(ActivationStores stores, Func<Task>? beforePredecessorScan, IWorkflowTriggerIndexObserver? observer)
        {
            Stores = stores;
            _indexer = new(stores.Bindings, stores.Schedules);
            var references = beforePredecessorScan is null
                ? stores.References
                : new ReferenceStoreWithScanHook(stores.References, beforePredecessorScan);
            Coordinator = new(
                stores.Authority,
                references,
                new UnleasedRootWrites(),
                new FixedTimeProvider(Now),
                _indexer,
                stores.Bindings,
                stores.Schedules,
                observer is null ? null : [observer],
                NullLogger<WorkflowActivationCoordinator>.Instance);
            _shellStart = new(
                stores.References,
                stores.Authority,
                Coordinator,
                new FixedTimeProvider(Now),
                NullLogger<CompleteInterruptedActivationsStartupTask>.Instance);
        }

        public ActivationStores Stores { get; }
        public WorkflowActivationCoordinator Coordinator { get; }

        public async Task<WorkflowActivationResult> ActivateAsync(string activationId, string artifactId) =>
            await Coordinator.ActivateAsync(new WorkflowActivationCommand(
                Executable(artifactId),
                Reference(artifactId),
                SlotName,
                activationId,
                WorkflowActivationSource.Publishing,
                await RevisionAsync()));

        public async Task<WorkflowActivationResult> DeactivateAsync(string artifactId) =>
            await Coordinator.DeactivateAsync(new WorkflowDeactivationCommand(
                Executable(artifactId),
                SlotName,
                WorkflowActivationSource.Publishing,
                await RevisionAsync()));

        public Task StartShellAsync() => _shellStart.ExecuteAsync(CancellationToken.None);

        /// <summary>What a concurrent activation has written before it reaches the slot transition.</summary>
        public async Task PrepareCandidateAsync(string activationId, string artifactId)
        {
            await Stores.References.SaveAsync(Reference(artifactId) with
            {
                SourceReferenceId = WorkflowActivationReferenceIdentity.Create(activationId),
                ActivationId = activationId,
                SlotId = SlotId
            });
            await _indexer.PrepareActivationAsync(Executable(artifactId), activationId, SlotId);
        }

        /// <summary>Switches one activation's projections on and its replaced activation's off, in both stores.</summary>
        public async Task SwitchAsync(string activationId, string? replacedActivationId)
        {
            await Stores.Bindings.ActivateAsync(activationId, replacedActivationId);
            await Stores.Schedules.ActivateAsync(activationId, replacedActivationId);
        }

        public async Task AssertSwitchRefusedAsync(string activationId, string replacedActivationId)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Stores.Bindings.ActivateAsync(activationId, replacedActivationId));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Stores.Schedules.ActivateAsync(activationId, replacedActivationId));
        }

        public async Task<string?> SlotActivationAsync() =>
            (await Stores.Authority.FindAsync(DefinitionId, SlotName))?.ActiveActivationId;

        /// <summary>Asserts which activations serve, through the queries the stimulus router and the recurring pump use.</summary>
        public async Task AssertServingAsync(params string[] activationIds)
        {
            var expected = activationIds.Order(StringComparer.Ordinal).ToArray();
            var bindings = await Stores.Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(StimulusType, StimulusHash));
            var schedules = await Stores.Schedules.ListDueAsync(Now, RuntimeStorePageRequest.MaximumLimit);
            Assert.Equal(expected, bindings.Items.Select(binding => binding.ActivationId!).Order(StringComparer.Ordinal));
            Assert.Equal(expected, schedules.Select(schedule => schedule.ActivationId!).Order(StringComparer.Ordinal));
        }

        public async Task AssertRetiredAsync(string activationId) =>
            Assert.Equal(WorkflowActivationCoordinator.ReplacedRetireReason, (await FindReferenceAsync(activationId)).DeletedReason);

        public async Task AssertLiveAsync(string activationId) => Assert.Null((await FindReferenceAsync(activationId)).DeletedAt);

        /// <summary>Asserts one activation's state in both projection stores.</summary>
        public async Task AssertProjectionsAsync(string activationId, WorkflowActivationProjectionState expected)
        {
            Assert.Equal(expected, await Stores.Bindings.FindActivationStateAsync(activationId));
            Assert.Equal(expected, await Stores.Schedules.FindActivationStateAsync(activationId));
        }

        /// <summary>
        /// Asserts that the slot, the projections and the references agree: the slot names <paramref name="activationId"/>,
        /// it alone serves, its reference is live, and each replaced activation is switched off and retired.
        /// </summary>
        public async Task AssertConsistentAsync(string activationId, params string[] replaced)
        {
            Assert.Equal(activationId, await SlotActivationAsync());
            await AssertServingAsync(activationId);
            await AssertLiveAsync(activationId);
            foreach (var other in replaced)
            {
                await AssertProjectionsAsync(other, WorkflowActivationProjectionState.Replaced);
                await AssertRetiredAsync(other);
            }
        }

        public ValueTask DisposeAsync() => Stores.Lifetime?.DisposeAsync() ?? ValueTask.CompletedTask;

        public async Task<WorkflowExecutableSourceReference> FindReferenceAsync(string activationId) =>
            await Stores.References.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId))
            ?? throw new InvalidOperationException($"Activation '{activationId}' has no source reference.");

        private async Task<long> RevisionAsync() => (await Stores.Authority.FindAsync(DefinitionId, SlotName))?.Revision ?? 0;
    }

    /// <summary>Prepares one trigger binding and one recurring schedule per activation, on the same stimulus.</summary>
    private sealed class OneTriggerIndexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore schedules) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            var identity = executable.Identity;
            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(activationId, identity.ArtifactId, NodeId, StimulusHash),
                identity.ArtifactId,
                identity.DefinitionId,
                identity.ArtifactVersion,
                identity.ArtifactHash,
                NodeId,
                StimulusType,
                StimulusHash,
                null,
                new Dictionary<string, string>(),
                Now,
                activationId,
                slotId);
            await bindings.PrepareActivationAsync(activationId, [binding], cancellationToken);
            await schedules.PrepareActivationAsync(
                activationId,
                [
                    new RecurringTriggerSchedule(
                        RecurringTriggerSchedule.BuildId(activationId, identity.ArtifactId, NodeId),
                        identity.ArtifactId,
                        NodeId,
                        StimulusType,
                        StimulusHash,
                        RecurringScheduleKind.Interval,
                        "PT1M",
                        Now,
                        Now,
                        activationId,
                        slotId)
                ],
                cancellationToken);
            return [binding];
        }
    }

    /// <summary>A trigger observer whose first notification fails, failing the activation that sends it.</summary>
    private sealed class FailOnceObserver : IWorkflowTriggerIndexObserver
    {
        private int _calls;

        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _calls) == 1
                ? ValueTask.FromException(new InvalidOperationException("Observer projection failed."))
                : ValueTask.CompletedTask;
    }

    /// <summary>Runs a hook once, just before the coordinator first pages the live references to find the replaced activation.</summary>
    private sealed class ReferenceStoreWithScanHook(IWorkflowExecutableSourceReferenceStore inner, Func<Task> hook) : IWorkflowExecutableSourceReferenceStore
    {
        private Func<Task>? _hook = hook;

        public async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hook, null) is { } hook)
                await hook();
            return await inner.ListPageAsync(query, cancellationToken);
        }

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => inner.FindAsync(sourceReferenceId, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactPageAsync(query, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query, CancellationToken cancellationToken = default) => inner.ListByDefinitionVersionPageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => inner.SaveAsync(reference, cancellationToken);
        public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default) => inner.RetireAsync(sourceReferenceId, deletedAt, reason, cancellationToken);
        public ValueTask<bool> TryRetireAsync(WorkflowExecutableSourceReference expectedLiveReference, WorkflowExecutableSourceReference retiredReference, CancellationToken cancellationToken = default) => inner.TryRetireAsync(expectedLiveReference, retiredReference, cancellationToken);
        public ValueTask<bool> TryRestoreAsync(WorkflowExecutableSourceReference expectedRetiredReference, WorkflowExecutableSourceReference restoredReference, CancellationToken cancellationToken = default) => inner.TryRestoreAsync(expectedRetiredReference, restoredReference, cancellationToken);
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference expectedDoomedReference, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryDeleteDoomedAsync(expectedDoomedReference, now, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.DeleteExpiredOrRetiredAsync(batch, now, cancellationToken);
    }

    /// <summary>
    /// Runs a hook once, just after the first slot read at which <c>when</c> holds. The read still returns what it read
    /// before the hook ran, as if the hook's writes landed just after it.
    /// </summary>
    private sealed class AfterSlotRead(IWorkflowActivationAuthority inner, Func<Task<bool>> when, Func<Task> hook) : IWorkflowActivationAuthority
    {
        private Func<Task>? _hook = hook;

        public async ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
        {
            var slot = await inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);
            if (_hook is { } pending && await when())
            {
                _hook = null;
                await pending();
            }
            return slot;
        }

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);
        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) => inner.TryActivateAsync(request, cancellationToken);
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
    }

    /// <summary>A trigger-binding store whose activation-scoped deletes fail, as one that is unavailable during compensation would.</summary>
    private sealed class BindingsThatCannotDelete(IWorkflowTriggerBindingStore inner) : IWorkflowTriggerBindingStore
    {
        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("The trigger-binding store is unavailable."));

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => inner.SaveAsync(binding, cancellationToken);
        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default) => inner.PrepareActivationAsync(activationId, bindings, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) => inner.ListByActivationAsync(query, cancellationToken);
        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default) => inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(string activationId, CancellationToken cancellationToken = default) => inner.FindActivationStateAsync(activationId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken cancellationToken = default) => inner.ListServingActivationIdsAsync(slotId, cancellationToken);
        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => inner.DeleteByArtifactAsync(artifactId, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) => inner.ListByStimulusAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) => inner.ListByStimulusTypeAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(string stimulusType, CancellationToken cancellationToken = default) => inner.ListActiveStimulusHashesAsync(stimulusType, cancellationToken);
    }

    /// <summary>Root-write leases fence the reference GC, which none of these scenarios runs.</summary>
    private sealed class UnleasedRootWrites : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            write(cancellationToken);
    }
}

/// <summary>One process's view of the durable activation state: a new one per process over the same database.</summary>
internal sealed record ActivationStores(
    IWorkflowActivationAuthority Authority,
    IWorkflowTriggerBindingStore Bindings,
    IRecurringTriggerScheduleStore Schedules,
    IWorkflowExecutableSourceReferenceStore References,
    IAsyncDisposable? Lifetime = null)
{
    /// <summary>In-memory state lives in the store instances, so every process shares one set.</summary>
    public static ActivationStores InMemory() => new(
        new InMemoryWorkflowActivationAuthority(),
        new InMemoryWorkflowTriggerBindingStore(),
        new InMemoryRecurringTriggerScheduleStore(),
        new InMemoryWorkflowExecutableSourceReferenceStore());

    /// <summary>The EF Core stores over <paramref name="context"/>, which the process owns and disposes.</summary>
    public static ActivationStores EntityFramework(RuntimeDbContext context, string scope)
    {
        var access = new FixedAccessor(scope);
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = "ef-runtime-test-recovery-signing-key-32-bytes",
            AllowEphemeralDevelopmentKey = false
        }));
        return new(
            new EfWorkflowActivationAuthority(context, access),
            new EfWorkflowTriggerBindingStore(context, access),
            new EfRecurringTriggerScheduleStore(context, access, codec),
            new EfWorkflowExecutableSourceReferenceStore(context, access, codec),
            context);
    }
}
