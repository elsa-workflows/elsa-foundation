using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Where an activation can stop, and concurrent activations of the same artifact (#2251), now that a slot and its serving
/// projections switch in one commit (#2230). Written once and run against the in-memory stores and EF Core on SQLite
/// (<c>WorkflowActivationCrashRepairContractTests</c>) and on PostgreSQL (the provider tests).
/// </summary>
/// <remarks>
/// A process that stops for good once its switch commits is <see cref="PauseAfterSwitch"/> with nothing to resume it; one
/// that stops before is a switch held at a <see cref="Latch"/> that is never released. Each scenario then works through
/// another process over the same durable state and checks what serves, through the stimulus router's query and the slot's
/// active recurring schedules. The property every scenario holds them to: the slot names the activation that serves, it
/// alone serves, its reference is live, and every activation it replaced is switched off and retired.
/// </remarks>
internal static partial class WorkflowActivationCrashRepairContract
{
    private const string DefinitionId = "definition-crash";
    private const string SlotName = "default";
    private const string NodeId = "node-start";
    private const string StimulusType = "Event";
    private const string StimulusHash = "crash-window";
    internal static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly string SlotId = WorkflowActivationSlotIdentity.Create(DefinitionId, SlotName);

    private static readonly Dictionary<string, Func<Func<ActivationStores>, Task>> Crashes = new()
    {
        ["a-call-that-stops-after-its-switch-leaves-the-activation-whole"] = ACallThatStopsAfterItsSwitchLeavesTheActivationWholeAsync,
        ["a-call-that-stops-before-its-switch-leaves-the-predecessor-serving"] = ACallThatStopsBeforeItsSwitchLeavesThePredecessorServingAsync,
        ["a-switch-whose-projection-cannot-switch-moves-nothing"] = ASwitchWhoseProjectionCannotSwitchMovesNothingAsync,
        ["a-refused-switch-changes-nothing"] = ARefusedSwitchChangesNothingAsync,
        ["a-stale-call-cannot-switch-a-replaced-first-activation-back-on"] = AStaleCallCannotSwitchAReplacedFirstActivationBackOnAsync,
        ["a-revert-restores-the-predecessor-with-a-live-reference"] = ARevertRestoresThePredecessorWithALiveReferenceAsync,
        ["a-revert-refused-after-a-later-writer-moved-the-slot-changes-nothing"] = ARevertRefusedAfterALaterWriterMovedTheSlotChangesNothingAsync,
        ["in-flight-activation-and-a-concurrent-check-that-it-serves-agree"] = InFlightActivationAndAConcurrentCheckThatItServesAgreeAsync,
        ["a-slot-left-half-done-is-repaired-by-the-next-activation-of-its-artifact"] = ASlotLeftHalfDoneIsRepairedByTheNextActivationOfItsArtifactAsync,
        ["a-slot-left-half-done-is-repaired-by-a-check-that-it-serves"] = ASlotLeftHalfDoneIsRepairedByACheckThatItServesAsync,
        ["a-replacement-repairs-a-slot-left-half-done-before-it-replaces-it"] = AReplacementRepairsASlotLeftHalfDoneBeforeItReplacesItAsync,
        ["two-calls-meeting-a-slot-left-half-done-repair-it-once"] = TwoCallsMeetingASlotLeftHalfDoneRepairItOnceAsync,
        ["a-slot-left-half-done-that-cannot-be-repaired-is-reported-not-built-on"] = ASlotLeftHalfDoneThatCannotBeRepairedIsReportedNotBuiltOnAsync,
        ["deactivating-a-slot-left-half-done-turns-off-every-activation-serving-it"] = DeactivatingASlotLeftHalfDoneTurnsOffEveryActivationServingItAsync,
        ["projection-switch-is-a-no-op-once-made"] = ProjectionSwitchIsANoOpOnceMadeAsync,
        ["projection-switch-is-refused-once-the-replaced-activation-is-off"] = ProjectionSwitchIsRefusedOnceTheReplacedActivationIsOffAsync
    };

    // Built on first use: the partial files' static fields initialize in an order the compiler does not promise.
    private static readonly Lazy<Dictionary<string, Func<Func<ActivationStores>, Task>>> AllScenarios = new(() =>
        Crashes.Concat(ConcurrentCalls).ToDictionary(scenario => scenario.Key, scenario => scenario.Value));

    private static Dictionary<string, Func<Func<ActivationStores>, Task>> All => AllScenarios.Value;

    public static TheoryData<string> Scenarios => ToTheoryData(All.Keys);

    public static Task RunAsync(string scenario, Func<ActivationStores> open) => All[scenario](open);

    private static TheoryData<string> ToTheoryData(IEnumerable<string> scenarios)
    {
        var data = new TheoryData<string>();
        foreach (var scenario in scenarios)
            data.Add(scenario);
        return data;
    }

    /// <summary>
    /// The window #2193 had to repair, closed: a process that stops once its switch commits leaves the slot, the
    /// projections and the references agreeing, so nothing is left to complete. Activating the same artifact again, and a
    /// completion, both find it already active.
    /// </summary>
    private static async Task ACallThatStopsAfterItsSwitchLeavesTheActivationWholeAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopAfterSwitchAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());

        await node.AssertConsistentAsync("activation-2", "activation-1");
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await node.Coordinator.EnsureServingAsync(DefinitionId, SlotName)).Outcome);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await node.ActivateAsync("activation-2", "artifact-2")).Outcome);
        await node.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A process that stops before its switch leaves the activation it would have replaced serving, untouched, and its own
    /// projection prepared, never serving. The next activation replaces the predecessor cleanly and leaves it alone.
    /// </summary>
    private static async Task ACallThatStopsBeforeItsSwitchLeavesThePredecessorServingAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await StopBeforeSwitchAsync(open, "activation-2", "artifact-2");
        await using var node = Start(open());
        await node.AssertConsistentAsync("activation-1");
        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Prepared);

        Assert.Equal(WorkflowActivationOutcome.Activated, (await node.ActivateAsync("activation-3", "artifact-3")).Outcome);

        await node.AssertConsistentAsync("activation-3", "activation-1");
        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Prepared);
    }

    /// <summary>
    /// The direction that would look like success: a switch whose recurring schedules were never prepared. The slot must not
    /// move while a projection cannot switch, or it would name an activation that serves only in part. Nothing moves, the
    /// call fails, and the predecessor keeps serving.
    /// </summary>
    private static async Task ASwitchWhoseProjectionCannotSwitchMovesNothingAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        var slot = await node.Stores.Authority.FindAsync(DefinitionId, SlotName);
        await node.PrepareCandidateAsync("activation-2", "artifact-2");
        await node.Stores.Schedules.DeleteByActivationAsync("activation-2");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await node.Stores.Switch.TryActivateAsync(
            new(DefinitionId, SlotName, "activation-2", WorkflowActivationSource.Publishing, slot!.Revision, Now)));

        Assert.Equal(slot, await node.Stores.Authority.FindAsync(DefinitionId, SlotName));
        await node.AssertConsistentAsync("activation-1");
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await node.Stores.Bindings.FindActivationStateAsync("activation-2"));
    }

    /// <summary>
    /// A switch at a revision the slot has moved past is refused, and leaves everything as it found it: the slot, the
    /// candidate's prepared projection, and its reference, which a discard had retired as failed and which a switch that
    /// committed would have resumed.
    /// </summary>
    private static async Task ARefusedSwitchChangesNothingAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        var slot = await node.Stores.Authority.FindAsync(DefinitionId, SlotName);
        await node.PrepareCandidateAsync("activation-2", "artifact-2");
        await node.Stores.References.RetireAsync(WorkflowActivationReferenceIdentity.Create("activation-2"), Now, WorkflowActivationCoordinator.FailedRetireReason);

        var refusal = await node.Stores.Switch.TryActivateAsync(new(DefinitionId, SlotName, "activation-2", WorkflowActivationSource.Publishing, slot!.Revision - 1, Now));

        Assert.Equal((false, WorkflowActivationConflict.RevisionMismatch), (refusal.Succeeded, refusal.Conflict));
        Assert.Equal(slot, await node.Stores.Authority.FindAsync(DefinitionId, SlotName));
        await node.AssertConsistentAsync("activation-1");
        await node.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Prepared);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await node.FindReferenceAsync("activation-2")).DeletedReason);
    }

    /// <summary>
    /// The first window of #2230, closed. A call reads the slot before another process activates the same first
    /// activation and then replaces it. Before #2230 a stale completion could switch that activation back on beside its
    /// successor, where it served for good. Now nothing switches projections without moving the slot from the revision it
    /// read, so the stale call is refused, switches nothing, and only the successor serves.
    /// </summary>
    private static async Task AStaleCallCannotSwitchAReplacedFirstActivationBackOnAsync(Func<ActivationStores> open)
    {
        await using var race = await StartRaceAsync(open, "activation-1", "artifact-1");
        await ActivateAsync(open, "activation-1", "artifact-1");
        await ActivateAsync(open, "activation-2", "artifact-2");

        var result = await race.ReleaseAsync();

        Assert.Equal(WorkflowActivationOutcome.Conflict, result.Outcome);
        Assert.Equal("activation-2", result.Slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await race.Loser.Coordinator.EnsureServingAsync(DefinitionId, SlotName)).Outcome);
        await race.Loser.AssertServingAsync("activation-2");
        Assert.Equal("activation-2", await race.Loser.SlotActivationAsync());
        await race.Loser.AssertRetiredAsync("activation-1");
    }

    /// <summary>
    /// The second window of #2230, closed. An activation fails after its switch, while another process completes the slot
    /// meanwhile. Before #2230 that completion could retire the predecessor's reference just as the failure handed the slot
    /// back to it, leaving it serving with a retired reference. Now completion retires nothing, and the revert restores the
    /// predecessor's reference in the commit that switches it back on.
    /// </summary>
    private static async Task ARevertRestoresThePredecessorWithALiveReferenceAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var stores = open();
        var pause = new PauseAfterSwitch(stores.Switch, resume.Task);
        await using var inFlight = Start(stores with { Switch = pause }, observer: new FailOnceObserver());
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(pause.Paused, await Task.WhenAny(activation, pause.Paused));
        await using var other = Start(open());
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await other.Coordinator.EnsureServingAsync(DefinitionId, SlotName)).Outcome);
        await other.AssertConsistentAsync("activation-2", "activation-1");

        resume.SetResult();
        var result = await activation;

        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.Null(result.CompensationDiagnostic);
        await other.AssertConsistentAsync("activation-1");
        await other.AssertProjectionsAsync("activation-2", WorkflowActivationProjectionState.Missing);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await other.FindReferenceAsync("activation-2")).DeletedReason);
    }

    /// <summary>
    /// A revert undoes only its caller's own transition, and only while the slot stands where that transition left it. An
    /// activation fails after its switch, once another node has activated a later artifact in the slot. Its revert is
    /// refused and changes nothing: the slot, and the projections and references of all three activations, stay as the
    /// later activation left them, and the call reports that its compensation failed.
    /// </summary>
    private static async Task ARevertRefusedAfterALaterWriterMovedTheSlotChangesNothingAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var stores = open();
        var pause = new PauseAfterSwitch(stores.Switch, resume.Task);
        await using var inFlight = Start(stores with { Switch = pause }, observer: new FailOnceObserver());
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(pause.Paused, await Task.WhenAny(activation, pause.Paused));
        await using var other = Start(open());
        Assert.Equal(WorkflowActivationOutcome.Activated, (await other.ActivateAsync("activation-3", "artifact-3")).Outcome);
        var before = await other.SnapshotAsync("activation-1", "activation-2", "activation-3");

        resume.SetResult();
        var result = await activation;

        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.StartsWith("Authority compensation failed", result.CompensationDiagnostic, StringComparison.Ordinal);
        Assert.Equal(before, await other.SnapshotAsync("activation-1", "activation-2", "activation-3"));
        await other.AssertConsistentAsync("activation-3", "activation-2", "activation-1");
    }

    /// <summary>
    /// An activation still running past its switch, and a check on another node that its slot serves, agree: the check
    /// finds the activation already active and writes nothing, and the activation finishes.
    /// </summary>
    private static async Task InFlightActivationAndAConcurrentCheckThatItServesAgreeAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        var resume = new TaskCompletionSource();
        var stores = open();
        var pause = new PauseAfterSwitch(stores.Switch, resume.Task);
        await using var inFlight = Start(stores with { Switch = pause });
        var activation = inFlight.ActivateAsync("activation-2", "artifact-2");
        Assert.Same(pause.Paused, await Task.WhenAny(activation, pause.Paused));
        await using var other = Start(open());

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await other.Coordinator.EnsureServingAsync(DefinitionId, SlotName)).Outcome);
        resume.SetResult();

        Assert.Equal(WorkflowActivationOutcome.Activated, (await activation).Outcome);
        await other.AssertConsistentAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A version before #2230 committed the slot transition before the projection switch, so a process that stopped in
    /// between left the slot naming a prepared activation beside the one it replaced, which still serves. The next
    /// activation of the slot's own artifact repairs it in one commit and finds it already active; the slot itself is not
    /// written.
    /// </summary>
    private static Task ASlotLeftHalfDoneIsRepairedByTheNextActivationOfItsArtifactAsync(Func<ActivationStores> open) =>
        RepairsAHalfDoneSlotAsync(open, node => node.ActivateAsync("activation-2", "artifact-2"));

    /// <summary>A check that the slot serves, which Publishing makes before it publishes and at shell start, repairs it too.</summary>
    private static Task ASlotLeftHalfDoneIsRepairedByACheckThatItServesAsync(Func<ActivationStores> open) =>
        RepairsAHalfDoneSlotAsync(open, node => node.Coordinator.EnsureServingAsync(DefinitionId, SlotName).AsTask());

    private static async Task RepairsAHalfDoneSlotAsync(Func<ActivationStores> open, Func<ActivationNode, Task<WorkflowActivationResult>> call)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        var slot = await node.LeaveHalfDoneAsync("activation-2", "artifact-2");

        var result = await call(node);

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Equal(slot, await node.Stores.Authority.FindAsync(DefinitionId, SlotName));
        await node.AssertRepairedAsync("activation-2", "activation-1");
    }

    /// <summary>
    /// A publish of another artifact to a slot left half done repairs it first, then replaces the slot's activation as it
    /// would any other: it serves alone, and both earlier activations are switched off and retired.
    /// </summary>
    private static async Task AReplacementRepairsASlotLeftHalfDoneBeforeItReplacesItAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        await node.LeaveHalfDoneAsync("activation-2", "artifact-2");

        var result = await node.ActivateAsync("activation-3", "artifact-3");

        Assert.Equal((WorkflowActivationOutcome.Activated, "activation-2"), (result.Outcome, result.ReplacedActivationId));
        await node.AssertConsistentAsync("activation-3", "activation-2");
        await node.AssertProjectionsAsync("activation-1", WorkflowActivationProjectionState.Missing);
        await node.AssertRetiredAsync("activation-1");
    }

    /// <summary>
    /// Two nodes meet one slot left half done at once, as two nodes starting together do. One has decided to repair it when
    /// the other repairs it first; its own repair is then refused, having changed nothing, and it finds the slot's activation
    /// serving. The slot is repaired once, and both report it already active.
    /// </summary>
    private static async Task TwoCallsMeetingASlotLeftHalfDoneRepairItOnceAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        await node.LeaveHalfDoneAsync("activation-2", "artifact-2");
        var latch = new Latch();
        var stores = open();
        await using var held = Start(stores with { Switch = new InterceptedSwitch(stores.Switch) { BeforeRepair = latch.PassAsync } });
        var holding = held.Coordinator.EnsureServingAsync(DefinitionId, SlotName).AsTask();
        Assert.Same(latch.Reached, await Task.WhenAny(holding, latch.Reached));

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await node.ActivateAsync("activation-2", "artifact-2")).Outcome);
        latch.Release();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await holding).Outcome);
        await node.AssertRepairedAsync("activation-2", "activation-1");
        Assert.Single(node.Log.Entries.Concat(held.Log.Entries), entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// The direction that would look like success: a slot left half done whose activation is no longer prepared in every
    /// store cannot be repaired in place. Nothing repairs it, builds on it or reports it active: activating the same
    /// artifact, replacing it, and checking that it serves all fail naming the remedy, and the predecessor keeps serving.
    /// </summary>
    private static async Task ASlotLeftHalfDoneThatCannotBeRepairedIsReportedNotBuiltOnAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        await node.LeaveHalfDoneAsync("activation-2", "artifact-2");
        await node.Stores.Schedules.DeleteByActivationAsync("activation-2");

        WorkflowActivationResult[] results =
        [
            await node.ActivateAsync("activation-2", "artifact-2"),
            await node.ActivateAsync("activation-3", "artifact-3"),
            await node.Coordinator.EnsureServingAsync(DefinitionId, SlotName)
        ];

        Assert.All(results, result =>
        {
            Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
            Assert.Contains("unpublish the slot", result.Diagnostic, StringComparison.Ordinal);
        });
        Assert.Equal("activation-2", await node.SlotActivationAsync());
        await node.AssertServingAsync("activation-1");
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await node.Stores.Bindings.FindActivationStateAsync("activation-2"));
        await node.AssertProjectionsAsync("activation-3", WorkflowActivationProjectionState.Missing);
    }

    /// <summary>
    /// The remedy for a slot left half done: deactivating it turns off its own activation and every other activation that
    /// serves the slot, here one whose reference was retired elsewhere so that only the projection stores still name it.
    /// The slot can then be activated cleanly.
    /// </summary>
    private static async Task DeactivatingASlotLeftHalfDoneTurnsOffEveryActivationServingItAsync(Func<ActivationStores> open)
    {
        await ActivateAsync(open, "activation-1", "artifact-1");
        await using var node = Start(open());
        await node.LeaveHalfDoneAsync("activation-2", "artifact-2");
        await node.Stores.References.RetireAsync(WorkflowActivationReferenceIdentity.Create("activation-1"), Now, "retired-elsewhere");

        var result = await node.DeactivateAsync("artifact-2");

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        await node.AssertNothingServesAsync("activation-1", "activation-2");
        Assert.Equal(WorkflowActivationOutcome.Activated, (await node.ActivateAsync("activation-3", "artifact-3")).Outcome);
        await node.AssertConsistentAsync("activation-3");
    }

    /// <summary>
    /// Every projection store reports an activation's lifecycle, and switching an activation on that already serves, with
    /// its replaced activation already off, is a no-op success.
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
    /// Every projection store, the in-memory ones included, refuses to switch on a candidate whose replaced activation no
    /// longer serves, and a candidate that serves beside its replaced activation. Nothing changes on a refusal.
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

    private static ActivationNode Start(
        ActivationStores stores,
        IWorkflowTriggerIndexObserver? observer = null,
        Func<Task>? beforeSequence = null) =>
        new(stores, observer, beforeSequence);

    private static async Task ActivateAsync(Func<ActivationStores> open, string activationId, string artifactId)
    {
        await using var node = Start(open());
        Assert.Equal(WorkflowActivationOutcome.Activated, (await node.ActivateAsync(activationId, artifactId)).Outcome);
    }

    /// <summary>Runs an activation in a process that stops for good once its switch commits.</summary>
    private static async Task StopAfterSwitchAsync(Func<ActivationStores> open, string activationId, string artifactId)
    {
        var stores = open();
        var pause = new PauseAfterSwitch(stores.Switch);
        await using var node = Start(stores with { Switch = pause });

        var activation = node.ActivateAsync(activationId, artifactId);

        Assert.Same(pause.Paused, await Task.WhenAny(activation, pause.Paused));
    }

    /// <summary>Runs an activation in a process that stops for good just before its switch: it has minted and prepared.</summary>
    private static async Task StopBeforeSwitchAsync(Func<ActivationStores> open, string activationId, string artifactId)
    {
        var latch = new Latch();
        var stores = open();
        await using var node = Start(stores with { Switch = new InterceptedSwitch(stores.Switch) { BeforeActivate = latch.PassAsync } });

        var activation = node.ActivateAsync(activationId, artifactId);

        Assert.Same(latch.Reached, await Task.WhenAny(activation, latch.Reached));
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

    /// <summary>One process: a coordinator over its own view of the durable stores.</summary>
    private sealed class ActivationNode : IAsyncDisposable
    {
        private readonly OneTriggerIndexer _indexer;

        public ActivationNode(ActivationStores stores, IWorkflowTriggerIndexObserver? observer, Func<Task>? beforeSequence)
        {
            Stores = stores;
            _indexer = new(stores.Bindings, stores.Schedules);
            Coordinator = new(
                stores.Authority,
                stores.Switch,
                stores.References,
                new RootWrites(stores, beforeSequence),
                new FixedTimeProvider(Now),
                _indexer,
                stores.Bindings,
                stores.Schedules,
                observer is null ? null : [observer],
                Log);
        }

        public ActivationStores Stores { get; }
        public WorkflowActivationCoordinator Coordinator { get; }
        public RecordingLogger<WorkflowActivationCoordinator> Log { get; } = new();

        public async Task<WorkflowActivationResult> ActivateAsync(string activationId, string artifactId, CancellationToken cancellationToken = default) =>
            await Coordinator.ActivateAsync(
                new WorkflowActivationCommand(
                    Executable(artifactId),
                    Reference(artifactId),
                    SlotName,
                    activationId,
                    WorkflowActivationSource.Publishing,
                    await RevisionAsync()),
                cancellationToken);

        public async Task<WorkflowActivationResult> DeactivateAsync(string artifactId) =>
            await Coordinator.DeactivateAsync(new WorkflowDeactivationCommand(
                Executable(artifactId),
                SlotName,
                WorkflowActivationSource.Publishing,
                await RevisionAsync()));

        /// <summary>Makes this node's next projection preparation fail before it writes anything.</summary>
        public void FailNextPreparation(Exception failure) => _indexer.NextFailure = failure;

        /// <summary>Holds this node's next projection preparation at <paramref name="latch"/>: the call has minted its reference and prepared nothing.</summary>
        public void HoldNextPreparation(Latch latch) => _indexer.BeforeNextPreparation = latch.PassAsync;

        /// <summary>What an activation has written before it reaches its switch.</summary>
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

        /// <summary>
        /// What a version before #2230 left when it stopped between its slot transition and its projection switch: the slot
        /// names a prepared activation, and the one it replaced still serves.
        /// </summary>
        public async Task<WorkflowActivationSlot> LeaveHalfDoneAsync(string activationId, string artifactId)
        {
            await PrepareCandidateAsync(activationId, artifactId);
            var transition = await Stores.Authority.TryActivateAsync(new(DefinitionId, SlotName, activationId, WorkflowActivationSource.Publishing, await RevisionAsync(), Now));
            Assert.True(transition.Succeeded);
            return transition.Slot;
        }

        /// <summary>Switches one activation's projections on and its replaced activation's off, in both stores, as each store's own switch.</summary>
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

        /// <summary>
        /// Asserts which activations serve, through the stimulus router's query and the slot's active schedules. Every
        /// schedule here is due at <see cref="Now"/>, so the active ones are those the recurring pump would claim; reading
        /// them does not claim them (#2198).
        /// </summary>
        public async Task AssertServingAsync(params string[] activationIds)
        {
            var expected = activationIds.Order(StringComparer.Ordinal).ToArray();
            var bindings = await Stores.Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(StimulusType, StimulusHash));
            Assert.Equal(expected, bindings.Items.Select(binding => binding.ActivationId!).Order(StringComparer.Ordinal));
            Assert.Equal(expected, await Stores.Schedules.ListServingActivationIdsAsync(SlotId));
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
        public Task AssertConsistentAsync(string activationId, params string[] replaced) =>
            AssertServesAloneAsync(activationId, WorkflowActivationProjectionState.Replaced, replaced);

        /// <summary>
        /// Asserts that a slot left half done was repaired: the slot names <paramref name="activationId"/>, it alone serves,
        /// its reference is live, and each activation that served in its place is deleted and retired as replaced.
        /// </summary>
        public Task AssertRepairedAsync(string activationId, params string[] servedInItsPlace) =>
            AssertServesAloneAsync(activationId, WorkflowActivationProjectionState.Missing, servedInItsPlace);

        /// <summary>
        /// The slot, and each activation's state in both projection stores and its reference's retirement: what a refused
        /// operation must leave exactly as it was.
        /// </summary>
        public async Task<object?[]> SnapshotAsync(params string[] activationIds)
        {
            var snapshot = new List<object?> { await Stores.Authority.FindAsync(DefinitionId, SlotName) };
            foreach (var activationId in activationIds)
            {
                var reference = await FindReferenceAsync(activationId);
                snapshot.AddRange(
                [
                    activationId,
                    await Stores.Bindings.FindActivationStateAsync(activationId),
                    await Stores.Schedules.FindActivationStateAsync(activationId),
                    reference.DeletedAt,
                    reference.DeletedReason
                ]);
            }

            return snapshot.ToArray();
        }

        /// <summary>Asserts that the slot is empty, nothing serves, and each of <paramref name="activationIds"/> has no projection left.</summary>
        public async Task AssertNothingServesAsync(params string[] activationIds)
        {
            Assert.Null(await SlotActivationAsync());
            await AssertServingAsync();
            foreach (var activationId in activationIds)
                await AssertProjectionsAsync(activationId, WorkflowActivationProjectionState.Missing);
        }

        private async Task AssertServesAloneAsync(string activationId, WorkflowActivationProjectionState othersLeftAs, string[] others)
        {
            Assert.Equal(activationId, await SlotActivationAsync());
            await AssertServingAsync(activationId);
            await AssertLiveAsync(activationId);
            foreach (var other in others)
            {
                await AssertProjectionsAsync(other, othersLeftAs);
                await AssertRetiredAsync(other);
            }
        }

        /// <summary>The activation has no projection and its reference is retired as failed: it was discarded.</summary>
        public async Task AssertDiscardedAsync(string activationId)
        {
            await AssertProjectionsAsync(activationId, WorkflowActivationProjectionState.Missing);
            Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, (await FindReferenceAsync(activationId)).DeletedReason);
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
        public Exception? NextFailure { get; set; }

        public Func<Task>? BeforeNextPreparation { get; set; }

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            if (BeforeNextPreparation is { } hold)
            {
                BeforeNextPreparation = null;
                await hold();
            }

            if (NextFailure is { } failure)
            {
                NextFailure = null;
                throw failure;
            }

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

    /// <summary>A point one call stops at, once, until released, so that a concurrent call can run first.</summary>
    private sealed class Latch
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => _reached.Task;

        public Task PassAsync() => _reached.TrySetResult() ? _released.Task : Task.CompletedTask;

        public void Release() => _released.TrySetResult();
    }

    /// <summary>A held call, and its pending result.</summary>
    private sealed class Race<T>(ActivationNode loser, Task<T> losing, Latch latch) : IAsyncDisposable
    {
        public ActivationNode Loser => loser;

        /// <summary>Lets the held call go on, and returns its result.</summary>
        public Task<T> ReleaseAsync()
        {
            latch.Release();
            return losing;
        }

        public ValueTask DisposeAsync() => loser.DisposeAsync();
    }

    /// <summary>
    /// A switch that runs a hook before an activation's switch, a discard or a repair, once each, and can throw once an
    /// activation's switch has committed, as a provider that loses its answer after committing does.
    /// </summary>
    private sealed class InterceptedSwitch(IWorkflowActivationSwitch inner) : ForwardingActivationSwitch(inner)
    {
        private Func<Task>? _beforeActivate;
        private Func<Task>? _beforeDiscard;
        private Func<Task>? _beforeRepair;
        private Func<Exception>? _afterActivate;

        public Func<Task>? BeforeActivate { init => _beforeActivate = value; }
        public Func<Task>? BeforeDiscard { init => _beforeDiscard = value; }
        public Func<Task>? BeforeRepair { init => _beforeRepair = value; }
        public Func<Exception>? AfterActivate { init => _afterActivate = value; }

        public override async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            await Once(ref _beforeActivate);
            var transition = await base.TryActivateAsync(request, cancellationToken);
            if (Interlocked.Exchange(ref _afterActivate, null) is { } after)
                throw after();
            return transition;
        }

        public override async ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            await Once(ref _beforeDiscard);
            return await base.TryDiscardAsync(reference, cancellationToken);
        }

        public override async ValueTask<bool> TryRepairAsync(WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default)
        {
            await Once(ref _beforeRepair);
            return await base.TryRepairAsync(slot, alsoServing, cancellationToken);
        }

        private static Task Once(ref Func<Task>? hook) => Interlocked.Exchange(ref hook, null)?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>
    /// Root-write leases fence the reference GC, which most of these scenarios do not run, so a node takes them, through the
    /// built-in manager on its own clock, only when its stores carry a <see cref="ActivationStores.LeaseClock"/> (#2274).
    /// The activation sequence runs inside the lease, after the checks that answer a call without one, so
    /// <c>beforeSequence</c> holds a call just there.
    /// </summary>
    private sealed class RootWrites(ActivationStores stores, Func<Task>? beforeSequence) : IWorkflowExecutableRootWriteLeaseManager
    {
        private readonly IWorkflowExecutableRootWriteLeaseManager? _leases = stores.LeaseClock is { } clock
            ? new WorkflowExecutableRootWriteLeaseManager(stores.Executables, Options.Create(GarbageCollectionOptions), clock)
            : null;

        public async ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            if (_leases is null)
                await SequenceAsync(cancellationToken);
            else
                await _leases.ExecuteAsync(artifactId, leaseId, SequenceAsync, cancellationToken);

            async ValueTask SequenceAsync(CancellationToken token)
            {
                if (beforeSequence is not null)
                    await beforeSequence();
                await write(token);
            }
        }
    }
}

/// <summary>One process's view of the durable activation state: a new one per process over the same database.</summary>
internal sealed record ActivationStores(
    IWorkflowActivationAuthority Authority,
    IWorkflowActivationSwitch Switch,
    IWorkflowTriggerBindingStore Bindings,
    IRecurringTriggerScheduleStore Schedules,
    IWorkflowExecutableSourceReferenceStore References,
    IWorkflowExecutableStore Executables,
    IAsyncDisposable? Lifetime = null)
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(WorkflowActivationCrashRepairContract.Now);

    /// <summary>
    /// The process's clock for root-write leases. When set, its activations take real leases on <see cref="Executables"/>
    /// and renew them as this clock advances; a clock nobody advances is a process that stopped and never renews.
    /// </summary>
    public TimeProvider? LeaseClock { get; init; }

    /// <summary>In-memory state lives in the store instances, so every process shares one set.</summary>
    public static ActivationStores InMemory()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        return new(
            authority,
            new InMemoryWorkflowActivationSwitch(authority, references, Clock, bindings, schedules),
            bindings,
            schedules,
            references,
            new InMemoryWorkflowExecutableStore());
    }

    /// <summary>The EF Core stores over <paramref name="context"/>, which the process owns and disposes.</summary>
    public static ActivationStores EntityFramework(RuntimeDbContext context, string scope)
    {
        var access = new FixedAccessor(scope);
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = "ef-runtime-test-recovery-signing-key-32-bytes",
            AllowEphemeralDevelopmentKey = false
        }));
        var authority = new EfWorkflowActivationAuthority(context, access);
        var bindings = new EfWorkflowTriggerBindingStore(context, access);
        var schedules = new EfRecurringTriggerScheduleStore(context, access, codec);
        var references = new EfWorkflowExecutableSourceReferenceStore(context, access, codec);
        return new(
            authority,
            new EfWorkflowActivationSwitch(authority, bindings, references, access, Clock, schedules),
            bindings,
            schedules,
            references,
            new EfWorkflowExecutableStore(context, access),
            context);
    }
}
