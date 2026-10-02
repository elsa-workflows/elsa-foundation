using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowActivationCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonDocument EmptyDescriptor = JsonDocument.Parse("{}");
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    private readonly Harness _harness = new();

    [Fact]
    public async Task Activation_runs_the_atomic_logical_sequence_and_stamps_the_reference()
    {
        var result = await _harness.ActivateAsync("activation-1", "artifact-1");

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationReferenceIdentity.Create("activation-1"), result.Reference!.SourceReferenceId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), result.Reference.SlotId);
        Assert.Equal(["lease", "reference:save", "projection:prepare", "switch:activate", "observer"], _harness.Calls);
        Assert.Equal(["activation-1"], await _harness.ServingAsync());
    }

    [Fact]
    public async Task Replacement_retires_the_predecessor_in_the_switch()
    {
        var first = await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.ResetCalls();

        var second = await _harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Activated, second.Outcome);
        Assert.Equal("activation-1", second.ReplacedActivationId);
        Assert.Equal(["lease", "reference:save", "projection:prepare", "switch:activate", "reference:retire", "observer"], _harness.Calls);
        await _harness.AssertRetiredAsync("activation-1", WorkflowActivationCoordinator.ReplacedRetireReason);
        Assert.Equal(["activation-2"], await _harness.ServingAsync());
    }

    [Fact]
    public async Task Same_artifact_is_idempotent_without_writes_but_takeover_transfers_ownership()
    {
        var first = await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.ResetCalls();

        var noOp = await _harness.ActivateAsync("activation-2", "artifact-1", expectedRevision: first.Slot.Revision, source: Importer);
        Assert.True(noOp.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, noOp.Outcome);
        Assert.Empty(_harness.Calls);

        var takeover = await _harness.ActivateAsync(
            "activation-2",
            "artifact-1",
            expectedRevision: first.Slot.Revision,
            source: Importer,
            intent: WorkflowActivationOwnershipIntent.TakeOver);
        Assert.True(takeover.Succeeded);
        Assert.Equal("activation-2", takeover.Slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, takeover.Slot.Source!.Kind);
    }

    [Fact]
    public async Task Stale_slot_revision_cleans_candidate_state_and_returns_conflict()
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");

        var result = await _harness.ActivateAsync("candidate", "artifact-2", expectedRevision: 0);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Conflict, result.Outcome);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, result.Conflict);
        Assert.Equal("incumbent", result.Slot.ActiveActivationId);
        Assert.Equal(incumbent.Slot.Revision, result.Slot.Revision);
        await _harness.AssertDiscardedAsync("candidate", servingInstead: "incumbent");
    }

    [Fact]
    public async Task Foreign_owner_refusal_does_not_replace_the_incumbent()
    {
        var incumbent = await _harness.ActivateAsync("published", "artifact-1", source: WorkflowActivationSource.Publishing);

        var result = await _harness.ActivateAsync("imported", "artifact-2", expectedRevision: incumbent.Slot.Revision, source: Importer);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, result.Conflict);
        Assert.Equal("published", result.Slot.ActiveActivationId);
        await _harness.AssertDiscardedAsync("imported", servingInstead: "published");
    }

    [Theory]
    [InlineData(WorkflowActivationStep.ProjectionPreparation)]
    [InlineData(WorkflowActivationStep.SlotTransition)]
    [InlineData(WorkflowActivationStep.TriggerObserverNotification)]
    public async Task A_failure_is_reported_at_its_step_and_compensated(WorkflowActivationStep step)
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        _harness.FailAt(step);

        var result = await _harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(step, result.FailedStep);
        Assert.NotNull(result.Diagnostic);
        Assert.Null(result.CompensationDiagnostic);
        Assert.Equal("incumbent", result.Slot.ActiveActivationId);
        await _harness.AssertDiscardedAsync("candidate", servingInstead: "incumbent");
        await _harness.AssertLiveAsync("incumbent");
    }

    /// <summary>
    /// The direction that would look like a failure: the switch committed, and only its answer was lost. Discarding the
    /// candidate is refused because it serves, so the call reports what committed, naming the activation it replaced.
    /// </summary>
    [Fact]
    public async Task Switch_that_commits_and_then_throws_is_reported_activated_rather_than_compensated()
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        _harness.Switch.ThrowAfterActivate = new InvalidOperationException("connection lost after commit");
        _harness.ResetCalls();

        var result = await _harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("incumbent", result.ReplacedActivationId);
        Assert.Equal("candidate", result.Slot.ActiveActivationId);
        Assert.Equal(["lease", "reference:save", "projection:prepare", "switch:activate", "reference:retire", "switch:discard", "observer"], _harness.Calls);
        Assert.Equal(["candidate"], await _harness.ServingAsync());
        await _harness.AssertLiveAsync("candidate");
        await _harness.AssertRetiredAsync("incumbent", WorkflowActivationCoordinator.ReplacedRetireReason);
    }

    [Fact]
    public async Task Failed_activation_can_resume_its_exact_retired_reference_on_a_later_attempt()
    {
        _harness.Observer.ThrowOnce = true;

        var failed = await _harness.ActivateAsync("candidate", "artifact-1");
        Assert.False(failed.Succeeded);
        await _harness.AssertRetiredAsync("candidate", WorkflowActivationCoordinator.FailedRetireReason);

        _harness.ResetCalls();
        var retry = _harness.Command("candidate", "artifact-1", expectedRevision: await _harness.RevisionAsync());
        retry = retry with
        {
            Reference = retry.Reference with
            {
                CreatedAt = Now.AddMinutes(5),
                PublishedAt = Now.AddMinutes(5)
            }
        };
        var recovered = await _harness.Coordinator.ActivateAsync(retry);

        Assert.Equal(WorkflowActivationOutcome.Activated, recovered.Outcome);
        Assert.DoesNotContain("reference:save", _harness.Calls);
        var restored = await _harness.FindReferenceAsync("candidate");
        Assert.Null(restored.DeletedAt);
        Assert.Null(restored.DeletedReason);
        Assert.Equal(Now, restored.CreatedAt);
        Assert.Equal(Now, restored.PublishedAt);
    }

    [Fact]
    public async Task Failed_activation_reference_is_not_reused_or_mutated_for_a_different_payload()
    {
        _harness.Observer.ThrowOnce = true;
        _ = await _harness.ActivateAsync("candidate", "artifact-1");

        var conflict = await _harness.ActivateAsync("candidate", "artifact-2");

        Assert.False(conflict.Succeeded);
        Assert.Equal(WorkflowActivationStep.SourceReferenceMint, conflict.FailedStep);
        Assert.Contains("different activation payload", conflict.Diagnostic!, StringComparison.Ordinal);
        var original = await _harness.FindReferenceAsync("candidate");
        Assert.Equal("artifact-1", original.ArtifactId);
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, original.DeletedReason);
    }

    [Fact]
    public async Task Duplicate_create_race_does_not_retire_a_different_winning_reference()
    {
        var winner = _harness.ActivationReference("candidate", "artifact-2");
        _harness.References.ConcurrentCreateWinner = winner;

        var conflict = await _harness.ActivateAsync("candidate", "artifact-1");

        Assert.False(conflict.Succeeded);
        Assert.Equal(WorkflowActivationStep.SourceReferenceMint, conflict.FailedStep);
        Assert.Contains("different activation payload", conflict.Diagnostic!, StringComparison.Ordinal);
        var current = await _harness.FindReferenceAsync("candidate");
        Assert.Equal("artifact-2", current.ArtifactId);
        Assert.Null(current.DeletedAt);
        Assert.DoesNotContain("reference:retire", _harness.Calls);
    }

    [Fact]
    public async Task Duplicate_create_race_reuses_the_same_winning_reference()
    {
        var winner = _harness.ActivationReference("candidate", "artifact-1");
        _harness.References.ConcurrentCreateWinner = winner;

        var activated = await _harness.ActivateAsync("candidate", "artifact-1");

        Assert.Equal(WorkflowActivationOutcome.Activated, activated.Outcome);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(winner, await _harness.FindReferenceAsync("candidate")));
    }

    /// <summary>
    /// Another payload's reference took the candidate's id after the revert read it. The revert reads it again, finds it is
    /// not the candidate's own, and leaves it live, as the EF switch's transaction does when it loses that race.
    /// </summary>
    [Fact]
    public async Task Compensation_does_not_retire_a_reference_replaced_after_ownership_check()
    {
        _harness.Observer.ThrowOnce = true;
        var winner = _harness.ActivationReference("candidate", "artifact-2");
        _harness.References.ReplaceBeforeTryRetire = winner;

        var failed = await _harness.ActivateAsync("candidate", "artifact-1");

        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, failed.FailedStep);
        Assert.Null(failed.CompensationDiagnostic);
        var current = await _harness.FindReferenceAsync("candidate");
        Assert.Equal("artifact-2", current.ArtifactId);
        Assert.Null(current.DeletedAt);
    }

    /// <summary>
    /// A trigger observer fails after the switch, and the slot has moved on before the revert: the revert is refused and
    /// changes nothing, and the failure says so rather than claiming the slot was handed back.
    /// </summary>
    [Fact]
    public async Task Compensation_failure_is_reported_without_masking_the_original_failure()
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        _harness.Observer.ThrowOnce = true;
        _harness.Switch.RefuseRevert = true;

        var result = await _harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.Contains("observer projection failed", result.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("Authority compensation failed", result.CompensationDiagnostic!, StringComparison.Ordinal);
        Assert.Equal(["candidate"], await _harness.ServingAsync());
    }

    [Fact]
    public async Task Deactivation_removes_serving_projection_and_is_idempotent()
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.ResetCalls();

        var result = await _harness.DeactivateAsync(activated.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Null(result.Slot.ActiveActivationId);
        Assert.Empty(await _harness.ServingAsync());
        Assert.Equal(["switch:deactivate", "observer"], _harness.Calls);

        var repeat = await _harness.DeactivateAsync(result.Slot.Revision);
        Assert.Equal(WorkflowActivationOutcome.AlreadyInactive, repeat.Outcome);
    }

    [Fact]
    public async Task Deactivation_whose_switch_fails_changes_nothing()
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.Switch.ThrowOnDeactivate = new InvalidOperationException("projection removal failed");

        var result = await _harness.DeactivateAsync(activated.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(WorkflowActivationStep.SlotTransition, result.FailedStep);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.Equal(["activation-1"], await _harness.ServingAsync());
    }

    [Fact]
    public async Task Deactivation_whose_observer_fails_restores_the_activation()
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.Observer.ThrowOnce = true;

        var result = await _harness.DeactivateAsync(activated.Slot.Revision);

        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.Null(result.CompensationDiagnostic);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.Equal(["activation-1"], await _harness.ServingAsync());
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_a_compensated_failure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("activation-1", "artifact-1"), cancellation.Token));
        Assert.Empty(_harness.Calls);
    }

    [Fact]
    public async Task Cancellation_during_preparation_is_rethrown_at_the_boundary()
    {
        using var cancellation = new CancellationTokenSource();
        _harness.Indexer.CancelBeforeThrow = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("activation-1", "artifact-1"), cancellation.Token));
        Assert.Contains("reference:save", _harness.Calls);
        Assert.DoesNotContain("switch:activate", _harness.Calls);
    }

    [Fact]
    public async Task Cancellation_after_source_reference_save_retires_the_candidate_reference()
    {
        using var cancellation = new CancellationTokenSource();
        _harness.References.CancelAfterSave = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("activation-1", "artifact-1"), cancellation.Token));

        Assert.Null(await _harness.SlotActivationAsync());
        await _harness.AssertRetiredAsync("activation-1", WorkflowActivationCoordinator.FailedRetireReason);
    }

    [Fact]
    public async Task Cancellation_after_projection_preparation_removes_candidate_projection()
    {
        using var cancellation = new CancellationTokenSource();
        _harness.Indexer.CancelAfterPrepare = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("activation-1", "artifact-1"), cancellation.Token));

        Assert.Null(await _harness.SlotActivationAsync());
        await _harness.AssertDiscardedAsync("activation-1");
    }

    /// <summary>
    /// A switch cancelled before it commits changes nothing, so the candidate is discarded and the cancellation rethrown,
    /// not reported as a failure.
    /// </summary>
    [Fact]
    public async Task Cancellation_inside_a_switch_that_did_not_commit_discards_the_candidate()
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        _harness.Switch.CancelBeforeActivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("incumbent", await _harness.SlotActivationAsync());
        await _harness.AssertDiscardedAsync("candidate", servingInstead: "incumbent");
    }

    /// <summary>
    /// Once the switch commits, the activation stands: a cancellation is still rethrown, and nothing hands the slot back,
    /// as nothing would if the process stopped there. Handing it back is what left another node's activation not serving.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_after_the_switch_commits_leaves_the_activation_activated(bool duringObserverNotification)
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        if (duringObserverNotification)
            _harness.Observer.CancelAfterCall = cancellation;
        else
            _harness.Switch.CancelAfterActivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("candidate", await _harness.SlotActivationAsync());
        Assert.Equal(["candidate"], await _harness.ServingAsync());
        await _harness.AssertLiveAsync("candidate");
        await _harness.AssertRetiredAsync("incumbent", WorkflowActivationCoordinator.ReplacedRetireReason);
        Assert.DoesNotContain("switch:revert", _harness.Calls);
    }

    /// <summary>
    /// A refusal is known not to have committed, so it is answered as one, discarding the candidate, before a cancellation
    /// that arrived meanwhile is honoured (#2274).
    /// </summary>
    [Fact]
    public async Task A_refused_switch_is_compensated_before_a_cancellation_is_honoured()
    {
        var incumbent = await _harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        _harness.Switch.CancelAfterActivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.Coordinator.ActivateAsync(_harness.Command("candidate", "artifact-2", expectedRevision: 0), cancellation.Token));

        Assert.Equal(incumbent.Slot.Revision, await _harness.RevisionAsync());
        await _harness.AssertDiscardedAsync("candidate", servingInstead: "incumbent");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_after_the_deactivation_commits_leaves_the_slot_empty(bool duringObserverNotification)
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        if (duringObserverNotification)
            _harness.Observer.CancelAfterCall = cancellation;
        else
            _harness.Switch.CancelAfterDeactivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.DeactivateAsync(activated.Slot.Revision, cancellation.Token));

        Assert.Null(await _harness.SlotActivationAsync());
        Assert.Empty(await _harness.ServingAsync());
    }

    [Fact]
    public async Task Cancellation_inside_a_deactivation_that_did_not_commit_changes_nothing()
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        _harness.Switch.CancelBeforeDeactivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _harness.DeactivateAsync(activated.Slot.Revision, cancellation.Token));

        Assert.Equal("activation-1", await _harness.SlotActivationAsync());
        Assert.Equal(["activation-1"], await _harness.ServingAsync());
        await _harness.AssertLiveAsync("activation-1");
    }

    /// <summary>
    /// A stray serving beside the slot's activation, which only a version before #2230 could leave, is turned off with it.
    /// Its reference cannot be retired, which is logged as an error and leaves it live rather than failing the deactivation.
    /// </summary>
    [Fact]
    public async Task Deactivation_turns_off_a_stray_and_logs_a_reference_it_cannot_retire()
    {
        var activated = await _harness.ActivateAsync("activation-1", "artifact-1");
        await _harness.ServeStrayAsync("activation-stray", "artifact-stray");
        _harness.References.RefuseRetireOf.Add(WorkflowActivationReferenceIdentity.Create("activation-stray"));

        var result = await _harness.DeactivateAsync(activated.Slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Empty(await _harness.ServingAsync());
        await _harness.AssertLiveAsync("activation-stray");
        var error = Assert.Single(_harness.Logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("activation-stray", error.Fields["ActivationId"]);
    }

    public static TheoryData<string> RepairingCalls => ["same-artifact activation", "ensure serving"];

    /// <summary>
    /// A slot a version before #2230 left half done names a prepared activation while the one it replaced still serves.
    /// The next same-artifact activation, or a check that the slot serves, repairs it in one switch commit: the slot's
    /// activation serves, the other is switched off and retired, and the slot itself is not written.
    /// </summary>
    [Theory]
    [MemberData(nameof(RepairingCalls))]
    public async Task A_slot_left_half_done_is_repaired_by_the_next_call_that_meets_it(string call)
    {
        var first = await _harness.ActivateAsync("activation-1", "artifact-1");
        var slot = await _harness.LeaveHalfDoneAsync("activation-2", "artifact-2", first.Slot.Revision);
        _harness.ResetCalls();

        var result = call == "ensure serving"
            ? await _harness.Coordinator.EnsureServingAsync("definition-1", "default")
            : await _harness.ActivateAsync("activation-2", "artifact-2", slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Equal(slot, result.Slot);
        Assert.Equal(["activation-2"], await _harness.ServingAsync());
        await _harness.AssertRetiredAsync("activation-1", WorkflowActivationCoordinator.ReplacedRetireReason);
        await _harness.AssertLiveAsync("activation-2");
        Assert.Contains("switch:repair", _harness.Calls);
        Assert.Single(_harness.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_replacement_repairs_a_slot_left_half_done_before_it_replaces_the_slots_activation()
    {
        var first = await _harness.ActivateAsync("activation-1", "artifact-1");
        var slot = await _harness.LeaveHalfDoneAsync("activation-2", "artifact-2", first.Slot.Revision);

        var replacement = await _harness.ActivateAsync("activation-3", "artifact-3", slot.Revision);

        Assert.Equal(WorkflowActivationOutcome.Activated, replacement.Outcome);
        Assert.Equal("activation-2", replacement.ReplacedActivationId);
        Assert.Equal(["activation-3"], await _harness.ServingAsync());
        await _harness.AssertRetiredAsync("activation-1", WorkflowActivationCoordinator.ReplacedRetireReason);
        await _harness.AssertRetiredAsync("activation-2", WorkflowActivationCoordinator.ReplacedRetireReason);
    }

    /// <summary>
    /// A slot left half done whose activation's projections are no longer all prepared cannot be repaired in place. Nothing
    /// builds on it: the same-artifact request, a replacement and a check that it serves all fail naming the remedy, and
    /// nothing is repaired. Deactivating, the remedy, turns every activation serving the slot off.
    /// </summary>
    [Fact]
    public async Task A_slot_left_half_done_that_cannot_be_repaired_is_reported_not_built_on()
    {
        var first = await _harness.ActivateAsync("activation-1", "artifact-1");
        var slot = await _harness.LeaveHalfDoneAsync("activation-2", "artifact-2", first.Slot.Revision);
        await _harness.Bindings.DeleteByActivationAsync("activation-2");
        _harness.ResetCalls();

        var same = await _harness.ActivateAsync("activation-2", "artifact-2", slot.Revision);
        var replacement = await _harness.ActivateAsync("activation-3", "artifact-3", slot.Revision);
        var ensured = await _harness.Coordinator.EnsureServingAsync("definition-1", "default");

        Assert.All([same, replacement, ensured], result =>
        {
            Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
            Assert.Contains("unpublish the slot", result.Diagnostic, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("switch:repair", _harness.Calls);
        Assert.Equal(["activation-1"], await _harness.ServingAsync());

        Assert.Equal(WorkflowActivationOutcome.Deactivated, (await _harness.DeactivateAsync(slot.Revision)).Outcome);
        Assert.Empty(await _harness.ServingAsync());
    }

    [Fact]
    public async Task Ensuring_a_serving_activation_serves_reports_it_already_active_and_writes_nothing()
    {
        await _harness.ActivateAsync("activation-1", "artifact-1");
        _harness.ResetCalls();

        var ensured = await _harness.Coordinator.EnsureServingAsync("definition-1", "default");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, ensured.Outcome);
        Assert.Null(ensured.ReplacedActivationId);
        Assert.DoesNotContain(_harness.Calls, call => call.StartsWith("switch:", StringComparison.Ordinal));
    }

    private sealed class Harness
    {
        public Harness()
        {
            References = new(new InMemoryWorkflowExecutableSourceReferenceStore(), Calls);
            Indexer = new(Bindings, Calls);
            Observer = new(Calls);
            Switch = new(new InMemoryWorkflowActivationSwitch(Authority, References, new FakeTimeProvider(Now), Bindings), Calls);
            Coordinator = new(Authority, Switch, References, new RecordingLease(Calls), new FakeTimeProvider(Now), Indexer, Bindings, triggerObservers: [Observer], logger: Logger);
        }

        public RecordingLogger<WorkflowActivationCoordinator> Logger { get; } = new();
        public List<string> Calls { get; } = [];
        public InMemoryWorkflowActivationAuthority Authority { get; } = new();
        public InMemoryWorkflowTriggerBindingStore Bindings { get; } = new();
        public RecordingReferenceStore References { get; }
        public RecordingIndexer Indexer { get; }
        public RecordingObserver Observer { get; }
        public RecordingSwitch Switch { get; }
        public WorkflowActivationCoordinator Coordinator { get; }

        public void ResetCalls() => Calls.Clear();

        /// <summary>Makes the next activation fail at <paramref name="step"/>, before or after its switch commits.</summary>
        public void FailAt(WorkflowActivationStep step)
        {
            switch (step)
            {
                case WorkflowActivationStep.ProjectionPreparation: Indexer.Failure = new InvalidOperationException("indexer unavailable"); break;
                case WorkflowActivationStep.SlotTransition: Switch.ThrowOnActivate = new InvalidOperationException("switch unavailable"); break;
                case WorkflowActivationStep.TriggerObserverNotification: Observer.ThrowOnce = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        public WorkflowExecutableSourceReference ActivationReference(string activationId, string artifactId) =>
            Reference(artifactId) with
            {
                SourceReferenceId = WorkflowActivationReferenceIdentity.Create(activationId),
                ActivationId = activationId,
                SlotId = WorkflowActivationSlotIdentity.Create("definition-1", "default")
            };

        public WorkflowActivationCommand Command(
            string activationId,
            string artifactId,
            WorkflowActivationSource? source = null,
            long expectedRevision = 0,
            WorkflowActivationOwnershipIntent intent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
            new(
                Executable(artifactId),
                Reference(artifactId),
                "default",
                activationId,
                source ?? WorkflowActivationSource.Publishing,
                expectedRevision,
                intent);

        public ValueTask<WorkflowActivationResult> ActivateAsync(
            string activationId,
            string artifactId,
            long expectedRevision = 0,
            WorkflowActivationSource? source = null,
            WorkflowActivationOwnershipIntent intent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
            Coordinator.ActivateAsync(Command(activationId, artifactId, source, expectedRevision, intent));

        public ValueTask<WorkflowActivationResult> DeactivateAsync(long expectedRevision, CancellationToken cancellationToken = default) =>
            Coordinator.DeactivateAsync(new(Executable("artifact-1"), "default", WorkflowActivationSource.Publishing, expectedRevision), cancellationToken);

        public async Task<string[]> ServingAsync() =>
            (await Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("test", "hash-1"))).Items.Select(binding => binding.ActivationId!).Order(StringComparer.Ordinal).ToArray();

        public async Task<string?> SlotActivationAsync() => (await Authority.FindAsync("definition-1", "default"))?.ActiveActivationId;

        public async Task<long> RevisionAsync() => (await Authority.FindAsync("definition-1", "default"))?.Revision ?? 0;

        public async Task<WorkflowExecutableSourceReference> FindReferenceAsync(string activationId) =>
            await References.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId)) ??
            throw new InvalidOperationException($"Activation '{activationId}' has no source reference.");

        public async Task AssertLiveAsync(string activationId) => Assert.Null((await FindReferenceAsync(activationId)).DeletedAt);

        public async Task AssertRetiredAsync(string activationId, string reason) => Assert.Equal(reason, (await FindReferenceAsync(activationId)).DeletedReason);

        /// <summary>The activation has no projection left and its reference is retired as failed; <paramref name="servingInstead"/> serves alone.</summary>
        public async Task AssertDiscardedAsync(string activationId, string? servingInstead = null)
        {
            Assert.Equal(WorkflowActivationProjectionState.Missing, await Bindings.FindActivationStateAsync(activationId));
            await AssertRetiredAsync(activationId, WorkflowActivationCoordinator.FailedRetireReason);
            Assert.Equal(servingInstead is null ? [] : [servingInstead], await ServingAsync());
        }

        /// <summary>Switches an activation on beside the slot's own, as only a version before #2230 could leave one.</summary>
        public async Task ServeStrayAsync(string activationId, string artifactId)
        {
            var reference = ActivationReference(activationId, artifactId);
            await References.SaveAsync(reference);
            await Indexer.PrepareActivationAsync(Executable(artifactId), activationId, reference.SlotId!);
            await Bindings.ActivateAsync(activationId, null);
        }

        /// <summary>What a version before #2230 left when it stopped between its slot transition and its projection switch.</summary>
        public async Task<WorkflowActivationSlot> LeaveHalfDoneAsync(string activationId, string artifactId, long expectedRevision)
        {
            var reference = ActivationReference(activationId, artifactId);
            await References.SaveAsync(reference);
            await Indexer.PrepareActivationAsync(Executable(artifactId), activationId, reference.SlotId!);
            var transition = await Authority.TryActivateAsync(new("definition-1", "default", activationId, WorkflowActivationSource.Publishing, expectedRevision, Now));
            Assert.True(transition.Succeeded);
            return transition.Slot;
        }

        private static WorkflowExecutableSourceReference Reference(string artifactId) => new(
            SourceReferenceId: "caller-reference",
            ArtifactId: artifactId,
            SourceKind: "workflow-definition-version",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            TenantId: "tenant-a");

        private static WorkflowExecutable Executable(string artifactId) => new(
            new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:" + artifactId),
            new ExecutableNode(
                "node-start",
                "authored-node-start",
                "test/activity",
                "1.0.0",
                "test",
                EmptyDescriptor.RootElement,
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class RecordingLease(List<string> calls) : IWorkflowExecutableRootWriteLeaseManager
    {
        public async ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            calls.Add("lease");
            await write(cancellationToken);
        }
    }

    /// <summary>The in-memory switch, recording each operation and failing or cancelling where a test asks.</summary>
    private sealed class RecordingSwitch(IWorkflowActivationSwitch inner, List<string> calls) : ForwardingActivationSwitch(inner)
    {
        public Exception? ThrowOnActivate { get; set; }
        public Exception? ThrowAfterActivate { get; set; }
        public CancellationTokenSource? CancelBeforeActivate { get; set; }
        public CancellationTokenSource? CancelAfterActivate { get; set; }
        public bool RefuseRevert { get; set; }
        public Exception? ThrowOnDeactivate { get; set; }
        public CancellationTokenSource? CancelBeforeDeactivate { get; set; }
        public CancellationTokenSource? CancelAfterDeactivate { get; set; }

        public override async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            calls.Add("switch:activate");
            if (Take(ThrowOnActivate, value => ThrowOnActivate = value) is { } failure)
                throw failure;
            Cancel(CancelBeforeActivate, value => CancelBeforeActivate = value, cancellationToken);
            var transition = await base.TryActivateAsync(request, cancellationToken);
            if (Take(ThrowAfterActivate, value => ThrowAfterActivate = value) is { } committedFailure)
                throw committedFailure;
            if (Take(CancelAfterActivate, value => CancelAfterActivate = value) is { } cancellation)
                await cancellation.CancelAsync();
            return transition;
        }

        public override ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default)
        {
            calls.Add("switch:revert");
            return RefuseRevert ? ValueTask.FromResult(false) : base.TryRevertAsync(revert, cancellationToken);
        }

        public override async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(WorkflowDeactivationSlotRequest request, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default)
        {
            calls.Add("switch:deactivate");
            if (Take(ThrowOnDeactivate, value => ThrowOnDeactivate = value) is { } failure)
                throw failure;
            Cancel(CancelBeforeDeactivate, value => CancelBeforeDeactivate = value, cancellationToken);
            var transition = await base.TryDeactivateAsync(request, alsoServing, cancellationToken);
            Cancel(CancelAfterDeactivate, value => CancelAfterDeactivate = value, cancellationToken);
            return transition;
        }

        public override ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            calls.Add("switch:discard");
            return base.TryDiscardAsync(reference, cancellationToken);
        }

        public override ValueTask<bool> TryRepairAsync(WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default)
        {
            calls.Add("switch:repair");
            return base.TryRepairAsync(slot, alsoServing, cancellationToken);
        }

        private static T? Take<T>(T? value, Action<T?> clear) where T : class
        {
            clear(null);
            return value;
        }

        /// <summary>Cancels <paramref name="source"/>, once, and throws the cancellation the call then observes.</summary>
        private static void Cancel(CancellationTokenSource? source, Action<CancellationTokenSource?> clear, CancellationToken cancellationToken)
        {
            if (Take(source, clear) is not { } cancellation)
                return;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class RecordingIndexer(IWorkflowTriggerBindingStore bindingStore, List<string> calls) : IWorkflowTriggerIndexer
    {
        public Exception? Failure { get; set; }
        public CancellationTokenSource? CancelBeforeThrow { get; set; }
        public CancellationTokenSource? CancelAfterPrepare { get; set; }

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>([]);

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable, string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            calls.Add("projection:prepare");
            if (CancelBeforeThrow is { } beforeSource)
            {
                await beforeSource.CancelAsync();
                throw new OperationCanceledException(cancellationToken);
            }
            if (Failure is { } failure)
            {
                Failure = null;
                throw failure;
            }

            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(activationId, executable.Identity.ArtifactId, "node-start", "hash-1"),
                executable.Identity.ArtifactId,
                executable.Identity.DefinitionId,
                executable.Identity.ArtifactVersion,
                executable.Identity.ArtifactHash,
                "node-start",
                "test",
                "hash-1",
                null,
                new Dictionary<string, string>(),
                Now,
                activationId,
                slotId);
            await bindingStore.PrepareActivationAsync(activationId, [binding], cancellationToken);
            if (CancelAfterPrepare is { } afterSource)
            {
                CancelAfterPrepare = null;
                await afterSource.CancelAsync();
                throw new OperationCanceledException(cancellationToken);
            }
            return [binding];
        }
    }

    private sealed class RecordingObserver(List<string> calls) : IWorkflowTriggerIndexObserver
    {
        public bool ThrowOnce { get; set; }
        public int Calls { get; private set; }
        public CancellationTokenSource? CancelAfterCall { get; set; }

        public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++;
            calls.Add("observer");
            if (CancelAfterCall is { } source)
            {
                CancelAfterCall = null;
                await source.CancelAsync();
                throw new OperationCanceledException(cancellationToken);
            }
            if (ThrowOnce)
            {
                ThrowOnce = false;
                throw new InvalidOperationException("observer projection failed");
            }
        }
    }

    private sealed class RecordingReferenceStore(IWorkflowExecutableSourceReferenceStore inner, List<string> calls) : IWorkflowExecutableSourceReferenceStore
    {
        public HashSet<string> RefuseRetireOf { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource? CancelAfterSave { get; set; }
        public WorkflowExecutableSourceReference? ConcurrentCreateWinner { get; set; }
        public WorkflowExecutableSourceReference? ReplaceBeforeTryRetire { get; set; }

        public async ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            calls.Add("reference:save");
            if (ConcurrentCreateWinner is { } winner)
            {
                ConcurrentCreateWinner = null;
                await inner.SaveAsync(winner, cancellationToken);
                throw new InvalidOperationException("A concurrent source-reference create won.");
            }
            await inner.SaveAsync(reference, cancellationToken);
            if (CancelAfterSave is { } source)
            {
                CancelAfterSave = null;
                await source.CancelAsync();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => inner.FindAsync(sourceReferenceId, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactPageAsync(query, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);
        public ValueTask<bool> TryRestoreAsync(WorkflowExecutableSourceReference expectedRetiredReference, WorkflowExecutableSourceReference restoredReference, CancellationToken cancellationToken = default) => inner.TryRestoreAsync(expectedRetiredReference, restoredReference, cancellationToken);
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference expectedDoomedReference, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.TryDeleteDoomedAsync(expectedDoomedReference, now, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.DeleteExpiredOrRetiredAsync(batch, now, cancellationToken);

        public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
        {
            calls.Add("reference:retire");
            if (RefuseRetireOf.Contains(sourceReferenceId))
                throw new InvalidOperationException("reference retirement failed");
            return inner.RetireAsync(sourceReferenceId, deletedAt, reason, cancellationToken);
        }

        public async ValueTask<bool> TryRetireAsync(
            WorkflowExecutableSourceReference expectedLiveReference,
            WorkflowExecutableSourceReference retiredReference,
            CancellationToken cancellationToken = default)
        {
            calls.Add("reference:retire");
            if (ReplaceBeforeTryRetire is { } replacement)
            {
                ReplaceBeforeTryRetire = null;
                await inner.SaveAsync(replacement, cancellationToken);
            }

            return await inner.TryRetireAsync(expectedLiveReference, retiredReference, cancellationToken);
        }
    }
}
