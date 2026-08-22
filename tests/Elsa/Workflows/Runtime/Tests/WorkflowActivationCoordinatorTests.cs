using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The one activation lifecycle (FR-B-006): the ordered sequence, the same-artifact no-op, and — the
/// branch-heavy part — compensation with a failure injected between each pair of steps.
/// </summary>
public sealed class WorkflowActivationCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonDocument EmptyDescriptor = JsonDocument.Parse("{}");
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    // ---------------------------------------------------------------------------------------------------
    // The sequence
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activating_an_empty_slot_runs_prepare_then_slot_then_activate_then_observe()
    {
        var harness = new Harness();

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Null(result.ReplacedActivationId);

        // Ordering is the invariant, not merely the call set: preparing after the slot flip would expose an
        // empty serving projection, and observing before activation would publish a stale route table. Asserted
        // across both stores in one log, because the recurring-before-bindings half of the order is invisible in
        // either store's own log.
        Assert.Equal(
            [
                "schedules:prepare:activation-1",
                "bindings:prepare:activation-1",
                "bindings:activate:activation-1->",
                "schedules:activate:activation-1->"
            ],
            harness.Sequence);
        Assert.Single(harness.Observer.Snapshots);
        Assert.True(harness.Observer.Snapshots[0].RequiresProjectionRefresh);
    }

    [Fact]
    public async Task Each_projection_is_prepared_by_exactly_one_owned_write_with_no_read_back()
    {
        // FR-B-006 writer census, finding 3. The schedule projection used to be written twice per activation:
        // once by the indexer chain's recurring decorator, then again by a read-back-then-re-prepare the
        // coordinator inherited from PublicationProjectionReconciler. One contract owns each preparation now
        // (T044b), so each projection has exactly one write and the coordinator never reads a projection it is
        // about to write.
        var harness = new Harness();

        await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.Single(harness.Schedules.Calls, call => call.StartsWith("prepare:", StringComparison.Ordinal));
        Assert.Single(harness.Bindings.Calls, call => call.StartsWith("prepare:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Schedules.Calls, call => call.StartsWith("list:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_replacement_activation_prepares_each_projection_exactly_once_too()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.Single(harness.Schedules.Calls, call => call.StartsWith("prepare:", StringComparison.Ordinal));
        Assert.Equal("prepare:activation-2", harness.Schedules.Calls[0]);
        Assert.DoesNotContain(harness.Schedules.Calls, call => call.StartsWith("list:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_whole_sequence_runs_inside_one_root_write_lease()
    {
        var harness = new Harness();

        await harness.ActivateAsync("activation-1", "artifact-1");

        // The lease is what fences reference garbage collection out of the activation window.
        Assert.Equal(["artifact-1/activation:activation-1"], harness.Lease.Leases);
    }

    [Fact]
    public async Task The_coordinator_stamps_reference_identity_so_the_activation_resolves_from_the_slot()
    {
        var harness = new Harness();

        var result = await harness.ActivateAsync("activation-1", "artifact-1", sourceReferenceId: "caller-chosen");

        // The slot carries no ArtifactId, so activation -> reference must be resolvable by id alone.
        var expectedId = WorkflowActivationReferenceIdentity.Create("activation-1");
        Assert.Equal(expectedId, result.Reference!.SourceReferenceId);
        Assert.Equal("activation-1", result.Reference.ActivationId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), result.Reference.SlotId);
        var stored = await harness.References.FindAsync(expectedId);
        Assert.NotNull(stored);
        // Caller provenance rides along untouched.
        Assert.Equal("tenant-a", stored!.TenantId);
    }

    [Fact]
    public async Task Replacing_an_activation_retires_the_predecessor_reference_as_activation_replaced()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");

        var second = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.True(second.Succeeded);
        Assert.Equal("activation-1", second.ReplacedActivationId);
        var retired = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.NotNull(retired!.DeletedAt);
        Assert.Equal("activation-replaced", retired.DeletedReason);
        // The successor's own reference stays live.
        var live = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-2"));
        Assert.Null(live!.DeletedAt);
    }

    // ---------------------------------------------------------------------------------------------------
    // Same-artifact idempotent no-op
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// FR-B-006 as amended 2026-08-22: ownership is decided before, and independently of, whether the projections
    /// change. An explicit takeover of the artifact the slot already serves must still move ownership.
    /// </summary>
    /// <remarks>
    /// Before the amendment the sameness check ran first and swallowed this: the caller got a success, the
    /// incumbent kept the slot, and the later unpublish refused with a foreign-owner conflict. Export an artifact,
    /// import it, then publish the design that compiles to the same artifact and you hit it -- which is a natural
    /// thing to try, so it was reachable by hand long before any test looked for it.
    /// </remarks>
    /// <summary>
    /// T141: deactivation's compensation rebuilds the activation in the same order activation builds it —
    /// projections prepared <i>before</i> the slot goes back, never after.
    /// </summary>
    /// <remarks>
    /// Restoring the slot first reopens a window the activation ordering exists to close: the slot is live and
    /// pointing at an activation whose projections were just deleted, so a stimulus arriving in that window finds
    /// a serving definition with no bindings. The assertion is made <b>at the instant the slot moves</b> rather
    /// than afterwards, because both orders leave the same end state and only the intermediate state differs.
    /// </remarks>
    /// <summary>
    /// T148/A: restores an objective T041's record claimed was preserved by relocation. The deleted
    /// <c>NotifiesObservers_AfterSave_WithNewBindings</c> proved the observer was handed bindings that were
    /// <b>already durable</b> — it queried the store to show it. Its replacement asserted only that a snapshot
    /// arrived carrying the right activation id, which a notification fired <i>before</i> the write would also
    /// satisfy.
    /// </summary>
    [Fact]
    public async Task The_observer_is_handed_bindings_that_are_already_durable_when_it_is_notified()
    {
        var harness = new Harness();

        IReadOnlyCollection<WorkflowTriggerBinding>? durableAtNotification = null;
        harness.Observer.OnNotifying = async snapshot =>
            durableAtNotification ??= await harness.Bindings.ListAllByActivationAsync(
                snapshot.Bindings.Single().ActivationId);

        await harness.ActivateAsync("activation-1", "artifact-1");

        // Notified at all -- otherwise the assertion below would pass vacuously on a null-guard.
        Assert.NotNull(durableAtNotification);
        // The binding the observer was handed was readable from the store at that moment, not merely afterwards.
        var durable = Assert.Single(durableAtNotification!);
        Assert.Equal("activation-1", durable.ActivationId);
        Assert.Equal(
            Assert.Single(harness.Observer.Snapshots).Bindings.Single().ExecutableNodeId,
            durable.ExecutableNodeId);
    }

    [Fact]
    public async Task Deactivation_compensation_prepares_the_projections_before_it_restores_the_slot()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");

        // Fail the deactivation's projection removal -- after the slot flipped -- so compensation runs.
        harness.ResetCalls();
        harness.Bindings.FailOn["delete:activation-1"] = new InvalidOperationException("binding store unavailable");

        string[]? bindingCallsWhenSlotRestored = null;
        harness.Authority.OnActivating = request =>
        {
            // The only TryActivate left is compensation's; the initial activation already happened above.
            bindingCallsWhenSlotRestored ??= harness.Bindings.Calls.ToArray();
            return ValueTask.CompletedTask;
        };

        var result = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.False(result.Succeeded);
        // The compensation ran at all -- otherwise the assertion below would be vacuously true on a null check.
        Assert.NotNull(bindingCallsWhenSlotRestored);
        Assert.Contains(bindingCallsWhenSlotRestored!, call => call.StartsWith("prepare:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_explicit_takeover_of_the_same_artifact_still_transfers_ownership()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("import:prod-drop:1", "artifact-1", source: Importer);
        harness.ResetCalls();

        var takeover = await harness.ActivateAsync(
            "publication-1",
            "artifact-1",
            source: WorkflowActivationSource.Publishing,
            expectedRevision: first.Slot.Revision,
            ownershipIntent: WorkflowActivationOwnershipIntent.TakeOver);

        Assert.True(takeover.Succeeded);
        // Not AlreadyActive: an ownership-only transition is still a transition.
        Assert.NotEqual(WorkflowActivationOutcome.AlreadyActive, takeover.Outcome);
        Assert.Equal(WorkflowActivationSource.PublishingKind, takeover.Slot.Source!.Kind);
        Assert.Equal("publication-1", takeover.Slot.ActiveActivationId);
        // It consumed a revision, so a concurrent writer holding the old one is still correctly refused.
        Assert.True(takeover.Slot.Revision > first.Slot.Revision);
        // The displaced activation's reference is retired rather than left live alongside the new owner's.
        var retired = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("import:prod-drop:1"));
        Assert.NotNull(retired!.DeletedAt);
    }

    [Fact]
    public async Task Requesting_the_artifact_the_slot_already_serves_writes_nothing()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var repeat = await harness.ActivateAsync("activation-2", "artifact-1", expectedRevision: first.Slot.Revision);

        Assert.True(repeat.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, repeat.Outcome);
        // "Writes nothing" is the whole point: no lease, no projection touch, no revision bump.
        Assert.Empty(harness.Lease.Leases);
        Assert.Empty(harness.Bindings.Calls);
        Assert.Empty(harness.Schedules.Calls);
        Assert.Empty(harness.References.Calls);
        Assert.Equal(1, repeat.Slot.Revision);
        Assert.Equal("activation-1", repeat.Slot.ActiveActivationId);
        Assert.Null(await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-2")));
    }

    [Fact]
    public async Task The_same_artifact_no_op_applies_to_any_source_that_does_not_claim_ownership()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1", source: WorkflowActivationSource.Publishing);
        harness.ResetCalls();

        // A NON-owning source asking for the artifact already being served is still a no-op, not a
        // ForeignSource rejection: FR-B-006 scopes the loud rejection to a DIFFERENT artifact. No takeover intent
        // is passed, which is the case that must stay a no-op -- see the takeover test below for the other half.
        var repeat = await harness.ActivateAsync(
            "import:prod-drop:1",
            "artifact-1",
            source: Importer,
            expectedRevision: first.Slot.Revision);

        Assert.True(repeat.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, repeat.Outcome);
        Assert.Equal(WorkflowActivationSource.PublishingKind, repeat.Slot.Source!.Kind);
        Assert.Empty(harness.Bindings.Calls);
    }

    [Fact]
    public async Task A_retired_active_reference_is_not_mistaken_for_the_same_artifact()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        await harness.References.RetireAsync(
            WorkflowActivationReferenceIdentity.Create("activation-1"),
            Now,
            "manual");
        harness.ResetCalls();

        var again = await harness.ActivateAsync("activation-2", "artifact-1", expectedRevision: first.Slot.Revision);

        // Serving provenance is gone, so the slot is no longer genuinely serving that artifact: activate.
        Assert.Equal(WorkflowActivationOutcome.Activated, again.Outcome);
        Assert.Equal("activation-1", again.ReplacedActivationId);
        Assert.NotEmpty(harness.Bindings.Calls);
    }

    // ---------------------------------------------------------------------------------------------------
    // Authority refusals
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_foreign_source_with_a_different_artifact_is_refused_and_leaves_nothing_behind()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1", source: WorkflowActivationSource.Publishing);

        var foreign = await harness.ActivateAsync(
            "import:prod-drop:1",
            "artifact-2",
            source: Importer,
            expectedRevision: first.Slot.Revision);

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Conflict, foreign.Outcome);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        Assert.Contains("publishing", foreign.Diagnostic!, StringComparison.Ordinal);
        // The predecessor is untouched...
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Null(predecessor!.DeletedAt);
        // ...and the refused candidate's own prepared state is rolled back.
        Assert.Contains("delete:import:prod-drop:1", harness.Bindings.Calls);
        var candidateReference = await harness.References.FindAsync(
            WorkflowActivationReferenceIdentity.Create("import:prod-drop:1"));
        Assert.Equal("activation-failed", candidateReference!.DeletedReason);
    }

    /// <summary>
    /// T118: the coordinator passes the caller's takeover intent through to the authority unchanged, so a request
    /// carrying it claims a foreign-owned slot and becomes its owner.
    /// </summary>
    /// <remarks>
    /// Deliberately expressed with the <em>importer</em> as the claimant and publishing as the incumbent — the
    /// reverse of the production asymmetry. The rule under test is that the runtime honours the declared intent
    /// generically; a test that could only pass for publishing would be asserting a coupling this design forbids.
    /// </remarks>
    [Fact]
    public async Task A_takeover_claims_a_foreign_owned_slot_and_becomes_its_owner()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1", source: WorkflowActivationSource.Publishing);

        var takeover = await harness.ActivateAsync(
            "import:prod-drop:1",
            "artifact-2",
            source: Importer,
            expectedRevision: first.Slot.Revision,
            ownershipIntent: WorkflowActivationOwnershipIntent.TakeOver);

        Assert.True(takeover.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Activated, takeover.Outcome);
        Assert.Equal("activation-1", takeover.ReplacedActivationId);

        // One slot, one owner, one serving artifact — the takeover REPLACED, it did not add a second lane.
        var slot = Assert.Single(await harness.Authority.ListByDefinitionAsync("definition-1"));
        Assert.Equal("import:prod-drop:1", slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, slot.Source!.Kind);
        Assert.Equal("prod-drop", slot.Source.SourceId);
        var serving = Assert.Single(await harness.ServingBindingsAsync());
        Assert.Equal("import:prod-drop:1", serving.ActivationId);
        // The displaced activation's reference is retired, exactly as for a same-owner replacement.
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Equal("activation-replaced", predecessor!.DeletedReason);
    }

    /// <summary>
    /// A takeover that fails after the flip must put the predecessor back <b>under its own ownership</b>.
    /// </summary>
    /// <remarks>
    /// The subtle half of T118. Compensation restores the displaced activation id; if it restored it under the
    /// claimant's source the slot would end up serving the predecessor while naming the claimant as owner — and
    /// the predecessor's own next pass would then be refused for a slot nobody actually holds against it. Nothing
    /// else in the engine would report that, which is precisely why it is asserted here.
    /// </remarks>
    [Fact]
    public async Task A_failed_takeover_restores_the_displaced_activation_under_its_original_owner()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1", source: WorkflowActivationSource.Publishing);
        harness.ResetCalls();
        harness.Schedules.FailOn["activate:import:prod-drop:1"] = new InvalidOperationException("schedule store unavailable");

        var result = await harness.ActivateAsync(
            "import:prod-drop:1",
            "artifact-2",
            source: Importer,
            expectedRevision: first.Slot.Revision,
            ownershipIntent: WorkflowActivationOwnershipIntent.TakeOver);

        Assert.False(result.Succeeded);
        Assert.Null(result.CompensationDiagnostic);
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, slot.Source!.Kind);
        Assert.Null(slot.Source.SourceId);
    }

    [Fact]
    public async Task A_stale_revision_is_refused_and_the_slot_does_not_move()
    {
        var harness = new Harness();
        await harness.ActivateAsync("activation-1", "artifact-1");

        // Still presenting revision 0 after the slot advanced to 1.
        var stale = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: 0);

        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Conflict, stale.Outcome);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
        Assert.Equal(1, slot.Revision);
    }

    // ---------------------------------------------------------------------------------------------------
    // Compensation — a failure injected between each pair of steps
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_reference_save_failure_never_reaches_the_projections()
    {
        var harness = new Harness();
        harness.References.FailOn["save"] = new InvalidOperationException("reference store unavailable");

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Contains("reference store unavailable", result.Diagnostic!, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Bindings.Calls, call => call.StartsWith("prepare", StringComparison.Ordinal));
        Assert.Null(await harness.Authority.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task A_prepare_failure_leaves_the_slot_untouched_and_retires_the_candidate_reference()
    {
        var harness = new Harness();
        harness.Indexer.Failure = new InvalidOperationException("trigger extraction failed");

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Contains("trigger extraction failed", result.Diagnostic!, StringComparison.Ordinal);
        // The slot never flipped, so there is no authority compensation to do.
        Assert.Null(await harness.Authority.FindAsync("definition-1", "default"));
        Assert.Contains("delete:activation-1", harness.Bindings.Calls);
        // The recurring projection was already prepared when the indexer failed (the preparer runs first), so
        // compensation must take it back out too — otherwise a failed activation leaves a live-looking schedule.
        Assert.Contains("prepare:activation-1", harness.Schedules.Calls);
        Assert.Contains("delete:activation-1", harness.Schedules.Calls);
        var reference = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Equal("activation-failed", reference!.DeletedReason);
    }

    [Fact]
    public async Task A_projection_activate_failure_restores_the_replaced_activation_and_its_projections()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailOn["activate:activation-2"] = new InvalidOperationException("schedule store unavailable");

        var result = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Contains("schedule store unavailable", result.Diagnostic!, StringComparison.Ordinal);
        // The four compensation invariants, in order.
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
        Assert.Contains("activate:activation-1->activation-2", harness.Bindings.Calls);
        Assert.Contains("delete:activation-2", harness.Bindings.Calls);
        var candidate = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-2"));
        Assert.Equal("activation-failed", candidate!.DeletedReason);
        // The predecessor's reference must NOT be retired — it is serving again.
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Null(predecessor!.DeletedAt);
    }

    [Fact]
    public async Task An_observer_failure_after_the_flip_compensates_the_whole_transition()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        // Fail the activation's notification but let compensation's own notification converge.
        harness.Observer.FailOnCall = 1;

        var result = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Contains("route table refused", result.Diagnostic!, StringComparison.Ordinal);
        Assert.DoesNotContain("Observer compensation failed", result.Diagnostic!, StringComparison.Ordinal);
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Contains("activate:activation-1->activation-2", harness.Bindings.Calls);
        // Observers are reconciled only after BOTH sides reached their final serving state.
        Assert.Contains("delete:activation-2", harness.Bindings.Calls);
        Assert.Equal(2, harness.Observer.Calls);
    }

    [Fact]
    public async Task A_predecessor_retire_failure_compensates_the_whole_transition()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.References.FailOn["retire:" + WorkflowActivationReferenceIdentity.Create("activation-1")] =
            new InvalidOperationException("reference retire failed");

        var result = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Contains("reference retire failed", result.Diagnostic!, StringComparison.Ordinal);
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Contains("activate:activation-1->activation-2", harness.Bindings.Calls);
        var candidate = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-2"));
        Assert.Equal("activation-failed", candidate!.DeletedReason);
    }

    [Fact]
    public async Task A_post_flip_failure_with_no_predecessor_clears_the_slot()
    {
        var harness = new Harness();
        harness.Schedules.FailOn["activate:activation-1"] = new InvalidOperationException("schedule store unavailable");

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.False(result.Succeeded);
        // Nothing to restore, so the slot is deactivated rather than left pointing at a half-activated candidate.
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        Assert.Null(slot!.ActiveActivationId);
        Assert.Null(slot.Source);
        Assert.Equal(2, slot.Revision);
        Assert.Null(result.ReplacedActivationId);
    }

    [Fact]
    public async Task A_compensation_that_does_not_converge_is_reported_alongside_the_original_failure()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailOn["activate:activation-2"] = new InvalidOperationException("schedule store unavailable");
        harness.References.FailOn["retire:" + WorkflowActivationReferenceIdentity.Create("activation-2")] =
            new InvalidOperationException("reference store unavailable");

        var result = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.False(result.Succeeded);
        // The compensation failure must never mask the original one — an operator needs both.
        Assert.Contains("schedule store unavailable", result.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("Reference compensation failed: reference store unavailable", result.Diagnostic!, StringComparison.Ordinal);
        // The steps that CAN converge still do.
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task A_failed_authority_compensation_is_reported_and_does_not_throw()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.Schedules.FailOn["activate:activation-2"] = new InvalidOperationException("schedule store unavailable");
        // Refuse the restore of the predecessor that compensation is about to attempt.
        harness.Authority.Refuse.Add("activation-1");

        var result = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Contains("Authority compensation failed", result.Diagnostic!, StringComparison.Ordinal);
        // The remaining compensation steps still run rather than being abandoned at the first failure.
        Assert.Contains("delete:activation-2", harness.Bindings.Calls);
    }

    [Fact]
    public async Task The_diagnostic_stays_bounded_when_a_failure_message_is_enormous()
    {
        var harness = new Harness();
        harness.Indexer.Failure = new InvalidOperationException(new string('x', 4000));

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.False(result.Succeeded);
        Assert.True(result.Diagnostic!.Length <= 512);
    }

    // ---------------------------------------------------------------------------------------------------
    // Guards (§2.23.5)
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activating_without_the_trigger_spine_throws_before_any_write()
    {
        var harness = new Harness();
        var coordinator = harness.CreateWithoutTriggerSpine();

        var exception = await Assert.ThrowsAsync<WorkflowActivationException>(
            async () => await coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1")));

        Assert.Equal("definition-1", exception.WorkflowDefinitionId);
        Assert.Equal("activation-1", exception.ActivationId);
        // Loud, and before the first write: a half-activated definition no stimulus can start is worse.
        Assert.Empty(harness.References.Calls);
        Assert.Empty(harness.Lease.Leases);
    }

    [Fact]
    public async Task Activating_with_a_recurring_store_but_no_preparer_throws_before_any_write()
    {
        // T044b. A recurring store with nobody to prepare its projection used to fail at step 4 — the store
        // refuses to activate an unprepared projection — which is AFTER the slot CAS, so the activation landed
        // in compensation. The composition is refused up front instead.
        var harness = new Harness();
        var coordinator = harness.CreateWithoutSchedulePreparer();

        var exception = await Assert.ThrowsAsync<WorkflowActivationException>(
            async () => await coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1")));

        Assert.Contains("IRecurringTriggerScheduleProjectionPreparer", exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.References.Calls);
        Assert.Empty(harness.Lease.Leases);
        Assert.Empty(harness.Bindings.Calls);
        Assert.Empty(harness.Schedules.Calls);
        Assert.Null(await harness.Authority.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task The_recurring_projection_is_prepared_before_the_trigger_projection()
    {
        // The ordering the retired decorator provided from the inside, now provided by the coordinator: every
        // recurrence is materialized and validated before a single binding is written, so an invalid or
        // exhausted recurrence fails the activation with the trigger projection untouched.
        var harness = new Harness();
        harness.SchedulePreparer.Failure = new InvalidOperationException("recurring expression is exhausted");

        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationStep.ProjectionPreparation, result.FailedStep);
        Assert.Contains("recurring expression is exhausted", result.Diagnostic!, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Bindings.Calls, call => call.StartsWith("prepare", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Schedules.Calls, call => call.StartsWith("prepare", StringComparison.Ordinal));
        Assert.Null(await harness.Authority.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task An_infrastructure_fault_from_the_lease_manager_is_wrapped_in_a_domain_exception()
    {
        var harness = new Harness();
        harness.Lease.Failure = new IOException("the artifact store is offline");

        var exception = await Assert.ThrowsAsync<WorkflowActivationException>(
            async () => await harness.ActivateAsync("activation-1", "artifact-1"));

        Assert.IsType<IOException>(exception.InnerException);
        Assert.Equal("activation-1", exception.ActivationId);
        Assert.Equal("default", exception.SlotName);
    }

    [Fact]
    public async Task A_reference_pointing_at_a_different_artifact_than_the_executable_is_rejected()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("activation-1", "artifact-1") with
                {
                    Reference = harness.Reference("activation-1", "artifact-9")
                }));
    }

    [Fact]
    public void The_coordinator_resolves_from_a_bare_AddWorkflowRuntime()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        using var scope = provider.CreateScope();

        // The trigger serving spine belongs to WorkflowsRuntimeTriggers, so the coordinator must still be
        // constructible without it — the refusal is at activation time, not at composition time.
        var coordinator = scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>();

        Assert.IsType<WorkflowActivationCoordinator>(coordinator);
    }

    // ---------------------------------------------------------------------------------------------------
    // Deactivation — the retraction half of the lifecycle (T121)
    //
    // These absorb the objectives of the deleted PublicationProjectionReconcilerTests. That type was the SECOND
    // path that prepared, activated and removed serving projections; it had to know the same ordering invariant
    // as this coordinator, and when T044b changed the invariant here and not there, a failed unpublish silently
    // stopped a workflow's timers. The behaviour it was asked to have is now asked of the only path there is.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Deactivating_clears_the_slot_removes_both_projections_and_observes_once()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        Assert.NotEmpty(await harness.ServingBindingsAsync());
        Assert.NotEmpty(await harness.ServingSchedulesAsync());
        harness.ResetCalls();

        var result = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Null(result.Slot.ActiveActivationId);
        Assert.Equal("activation-1", result.ReplacedActivationId);
        // BOTH projections, not just the bindings. A binding-only retraction leaves a definition nothing can
        // start but whose timers keep firing, which is the worse half of the pair to forget.
        Assert.Equal(["delete:activation-1"], harness.Bindings.Calls);
        Assert.Equal(["delete:activation-1"], harness.Schedules.Calls);
        Assert.Empty(await harness.ServingBindingsAsync());
        Assert.Empty(await harness.ServingSchedulesAsync());
        // Once, and after the removal: an observer that refreshed mid-retraction would publish a half-empty
        // route table as if it were the settled one.
        Assert.Equal(1, harness.Observer.Calls);
    }

    [Fact]
    public async Task Deactivating_twice_converges_and_the_second_request_writes_nothing()
    {
        // The idempotency the deleted reconciler bought with a durable per-kind intent ledger. The slot itself
        // supplies it: a slot serving nothing has nothing to retract, so the repeat neither writes projections
        // nor moves the revision — and a revision the repeat had bumped would turn a harmless retry into a CAS
        // conflict for the next writer.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        var first = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);
        harness.ResetCalls();

        var second = await harness.DeactivateAsync("artifact-1", first.Slot.Revision);

        Assert.True(second.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyInactive, second.Outcome);
        Assert.Equal(first.Slot.Revision, second.Slot.Revision);
        Assert.Empty(harness.Bindings.Calls);
        Assert.Empty(harness.Schedules.Calls);
        Assert.Equal(0, harness.Observer.Calls);
    }

    [Fact]
    public async Task A_stale_revision_or_a_foreign_source_cannot_retract_an_activation()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var stale = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision + 5);
        var foreign = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision, Importer);

        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Conflict, stale.Outcome);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        // A refusal is not a partial retraction: the activation is untouched and still serving.
        Assert.Empty(harness.Bindings.Calls);
        Assert.Empty(harness.Schedules.Calls);
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.NotEmpty(await harness.ServingBindingsAsync());
    }

    [Fact]
    public async Task A_partial_removal_restores_the_slot_and_force_replays_both_projections()
    {
        // The objective of the deleted Restore_ForceReplaysDeliveredPrepareAndActivateAfterProjectionRemoval and
        // UnpublishProjectionRemovalFailureRestoresAuthorityAndReplaysServingProjection. The bindings are already
        // deleted when the schedules' removal fails, so "re-activate what is still there" restores nothing —
        // compensation has to RE-PREPARE from the artifact, which is the force-replay expressed as what it is.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");

        var result = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
        Assert.Equal(WorkflowActivationStep.ProjectionRemoval, result.FailedStep);
        Assert.Null(result.CompensationDiagnostic);
        // The slot is back, owned by the same activation.
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
        // And it is genuinely serving again — both projections, not merely the one the removal got to first.
        Assert.NotEmpty(await harness.ServingBindingsAsync());
        Assert.NotEmpty(await harness.ServingSchedulesAsync());
    }

    [Fact]
    public async Task The_compensating_replay_re_prepares_the_recurring_projection_before_the_trigger_projection()
    {
        // The ordering invariant, asserted on the RETRACTION path. It holds here because compensation calls the
        // one PrepareProjectionsAsync activation calls — which is the entire point of T121: there is no second
        // copy of this order to forget to update.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");

        await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        // Asserted across BOTH stores in one log. The per-store logs cannot see this: inverting the order of the
        // two preparations leaves each store's own log byte-identical, which is exactly how a swapped ordering
        // could pass a suite that only counts calls.
        Assert.Equal(
            [
                "bindings:delete:activation-1",
                "schedules:delete:activation-1",
                // The recurring projection is re-prepared BEFORE the first binding is written, exactly as on the
                // activating path: an invalid recurrence must fail the replay with no binding restored against it.
                "schedules:prepare:activation-1",
                "bindings:prepare:activation-1",
                "bindings:activate:activation-1->",
                "schedules:activate:activation-1->"
            ],
            harness.Sequence);
    }

    [Fact]
    public async Task The_compensating_replay_writes_each_projection_exactly_once_and_does_not_erase_the_schedules()
    {
        // The objective of the deleted Preparation_writes_each_projection_exactly_once_and_a_replay_cannot_erase
        // _the_schedules. There it was bought with one delivery record governing both projections; here it is
        // structural — one preparation call per projection, no read-back to re-prepare an emptied projection from.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");

        await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.Single(harness.Schedules.Calls, call => call.StartsWith("prepare:", StringComparison.Ordinal));
        Assert.Single(harness.Bindings.Calls, call => call.StartsWith("prepare:", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Schedules.Calls, call => call.StartsWith("list:", StringComparison.Ordinal));
        Assert.Single(await harness.ServingSchedulesAsync());
    }

    [Fact]
    public async Task The_compensating_replay_notifies_observers_once_after_the_restored_activation_is_serving()
    {
        // The objective of the deleted Compensation_NotifiesObserversOnce_AfterTheFinalAuthorityStateIsVisible.
        // A refresh taken between the removal and the replay would project an empty route table and then be
        // skipped as a repeat by an observer optimization, leaving the restored activation unreachable.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");

        await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.Equal(1, harness.Observer.Calls);
        var snapshot = Assert.Single(harness.Observer.Snapshots);
        Assert.True(snapshot.RequiresProjectionRefresh);
        var binding = Assert.Single(snapshot.Bindings);
        Assert.Equal("activation-1", binding.ActivationId);
    }

    [Fact]
    public async Task A_compensated_deactivation_failure_leaves_a_retry_that_converges()
    {
        // The objective of the deleted FailedPreparationIsPersistedAndRetryConvergesUsingSameIntentIdentity,
        // restated for a coordinator that deliberately carries no delivery-intent ledger: the recovery unit is
        // the caller's own next attempt, and it is safe because a compensated failure left nothing half-done.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");
        var failed = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);
        Assert.False(failed.Succeeded);

        harness.Schedules.FailAfterOn.Clear();
        var slot = await harness.Authority.FindAsync("definition-1", "default");
        var retry = await harness.DeactivateAsync("artifact-1", slot!.Revision);

        Assert.True(retry.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, retry.Outcome);
        Assert.Null(retry.Slot.ActiveActivationId);
        Assert.Empty(await harness.ServingBindingsAsync());
        Assert.Empty(await harness.ServingSchedulesAsync());
    }

    [Fact]
    public async Task A_failed_authority_restore_is_reported_alongside_the_original_failure_and_does_not_throw()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.Schedules.FailAfterOn["delete:activation-1"] = new InvalidOperationException("the schedule store is offline");
        harness.Authority.Refuse.Add("activation-1");

        var result = await harness.DeactivateAsync("artifact-1", activated.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.CompensationDiagnostic);
        Assert.Contains("Authority compensation failed", result.CompensationDiagnostic!, StringComparison.Ordinal);
        // The original cause survives the compensation report — the undo failing must never mask what broke.
        Assert.Contains("the schedule store is offline", result.Diagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivating_with_a_recurring_store_but_no_preparer_is_refused_before_the_first_write()
    {
        // The composition guard, migrated from the deleted reconciler's own copy of it. It bites HARDER on this
        // path: deactivation's compensation re-prepares, so a recurring store with no preparer would restore an
        // activation whose timers never fire again, and report nothing.
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var exception = await Assert.ThrowsAsync<WorkflowActivationException>(
            async () => await harness.DeactivateAsync(
                "artifact-1",
                activated.Slot.Revision,
                coordinator: harness.CreateWithoutSchedulePreparer()));

        Assert.Contains("IRecurringTriggerScheduleProjectionPreparer", exception.Message, StringComparison.Ordinal);
        Assert.Equal("activation-1", exception.ActivationId);
        // Before the first write: the slot has not moved and neither projection was touched.
        Assert.Empty(harness.Bindings.Calls);
        Assert.Empty(harness.Schedules.Calls);
        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.NotEmpty(await harness.ServingBindingsAsync());
    }

    [Fact]
    public async Task Deactivating_without_the_trigger_spine_is_refused_before_the_first_write()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        await Assert.ThrowsAsync<WorkflowActivationException>(
            async () => await harness.DeactivateAsync(
                "artifact-1",
                activated.Slot.Revision,
                coordinator: harness.CreateWithoutTriggerSpine()));

        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    // ---------------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------------

    private sealed class Harness
    {
        public Harness()
        {
            Bindings = new(new InMemoryWorkflowTriggerBindingStore(), Sequence, "bindings");
            Schedules = new(new InMemoryRecurringTriggerScheduleStore(), Sequence, "schedules");
            Indexer = new(Bindings);
            SchedulePreparer = new(Schedules);
            Coordinator = new WorkflowActivationCoordinator(
                Authority,
                References,
                Lease,
                new FixedTimeProvider(Now),
                Indexer,
                Bindings,
                Schedules,
                SchedulePreparer,
                [Observer]);
        }

        /// <summary>
        /// One log across BOTH projection stores, in call order. The per-store logs cannot see the invariant that
        /// matters — "the recurring projection is prepared before the first binding is written" is a statement
        /// about the order of two stores relative to each other, and inverting it leaves each store's own log
        /// unchanged.
        /// </summary>
        public List<string> Sequence { get; } = [];

        public FailingActivationAuthority Authority { get; } = new();
        public RecordingReferenceStore References { get; } = new(new InMemoryWorkflowExecutableSourceReferenceStore());
        public RecordingBindingStore Bindings { get; }
        public RecordingScheduleStore Schedules { get; }
        public RecordingTriggerIndexer Indexer { get; }
        public RecordingSchedulePreparer SchedulePreparer { get; }
        public RecordingObserver Observer { get; } = new();
        public FakeRootWriteLeaseManager Lease { get; } = new();
        public WorkflowActivationCoordinator Coordinator { get; }

        public WorkflowActivationCoordinator CreateWithoutTriggerSpine() =>
            new(Authority, References, Lease, new FixedTimeProvider(Now));

        /// <summary>The trigger spine composed, the recurring store composed, its preparer missing.</summary>
        public WorkflowActivationCoordinator CreateWithoutSchedulePreparer() =>
            new(Authority, References, Lease, new FixedTimeProvider(Now), Indexer, Bindings, Schedules);

        public void ResetCalls()
        {
            Sequence.Clear();
            Bindings.Calls.Clear();
            Schedules.Calls.Clear();
            References.Calls.Clear();
            Lease.Leases.Clear();
            Observer.Snapshots.Clear();
            Observer.Calls = 0;
        }

        public ValueTask<WorkflowActivationResult> ActivateAsync(
            string activationId,
            string artifactId,
            WorkflowActivationSource? source = null,
            long expectedRevision = 0,
            string? sourceReferenceId = null,
            WorkflowActivationOwnershipIntent ownershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
            Coordinator.ActivateAsync(
                Command(activationId, artifactId, source, expectedRevision, sourceReferenceId, ownershipIntent));

        public ValueTask<WorkflowActivationResult> DeactivateAsync(
            string artifactId,
            long expectedRevision,
            WorkflowActivationSource? source = null,
            IWorkflowActivationCoordinator? coordinator = null) =>
            (coordinator ?? Coordinator).DeactivateAsync(DeactivationCommand(artifactId, expectedRevision, source));

        public WorkflowDeactivationCommand DeactivationCommand(
            string artifactId,
            long expectedRevision,
            WorkflowActivationSource? source = null) =>
            new(Executable(artifactId), "default", source ?? WorkflowActivationSource.Publishing, expectedRevision);

        /// <summary>The bindings a stimulus can actually reach — i.e. what is genuinely serving.</summary>
        public async Task<IReadOnlyCollection<WorkflowTriggerBinding>> ServingBindingsAsync() =>
            (await Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("test", "hash-1"))).Items;

        /// <summary>The recurring schedules the pump would actually fire — i.e. what is genuinely serving.</summary>
        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ServingSchedulesAsync() =>
            Schedules.ListDueAsync(Now.AddHours(2), 10);

        public WorkflowActivationCommand Command(
            string activationId,
            string artifactId,
            WorkflowActivationSource? source = null,
            long expectedRevision = 0,
            string? sourceReferenceId = null,
            WorkflowActivationOwnershipIntent ownershipIntent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
            new(
                Executable(artifactId),
                Reference(activationId, artifactId, sourceReferenceId),
                "default",
                activationId,
                source ?? WorkflowActivationSource.Publishing,
                expectedRevision,
                ownershipIntent);

        public WorkflowExecutableSourceReference Reference(
            string activationId,
            string artifactId,
            string? sourceReferenceId = null) =>
            new(
                SourceReferenceId: sourceReferenceId ?? $"reference-{activationId}",
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

        private static WorkflowExecutable Executable(string artifactId) =>
            new(
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Wraps the real authority so a named transition can be made to refuse — the only way to exercise the
    /// "compensation itself did not converge" branch without a durable store.
    /// </summary>
    private sealed class FailingActivationAuthority : IWorkflowActivationAuthority
    {
        private readonly InMemoryWorkflowActivationAuthority _inner = new();

        /// <summary>Activation ids whose <c>TryActivate</c> is refused, and <c>"*"</c> to refuse deactivation.</summary>
        public HashSet<string> Refuse { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Runs at the instant a slot transition is applied. Lets a test observe the rest of the world *as the
        /// slot moves*, which is the only way to assert ordering without a global call log.
        /// </summary>
        public Func<WorkflowActivationSlotRequest, ValueTask>? OnActivating { get; set; }

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            _inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            if (Refuse.Contains(request.ActivationId))
                return await RefusedAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
            if (OnActivating is { } observe)
                await observe(request);
            return await _inner.TryActivateAsync(request, cancellationToken);
        }

        public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (Refuse.Contains("*"))
                return await RefusedAsync(workflowDefinitionId, slotName, cancellationToken);
            return await _inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
        }

        private async ValueTask<WorkflowActivationTransition> RefusedAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken)
        {
            var slot = await _inner.FindAsync(workflowDefinitionId, slotName, cancellationToken)
                ?? new WorkflowActivationSlot(
                    WorkflowActivationSlotIdentity.Create(workflowDefinitionId, slotName),
                    workflowDefinitionId,
                    slotName,
                    null,
                    null,
                    0,
                    Now);
            return new(false, slot, null, WorkflowActivationConflict.RevisionMismatch, "refused by the test authority");
        }
    }

    private sealed class FakeRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public List<string> Leases { get; } = [];
        public Exception? Failure { get; set; }

        public async ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;
            Leases.Add($"{artifactId}/{leaseId}");
            await write(cancellationToken);
        }
    }

    private sealed class RecordingObserver : IWorkflowTriggerIndexObserver
    {
        public List<WorkflowTriggerIndexSnapshot> Snapshots { get; } = [];
        public int Calls { get; set; }
        public int? FailOnCall { get; set; }

        /// <summary>
        /// Runs while the observer is being notified. Lets a test read the world <i>at that instant</i> — the only
        /// way to assert that what the observer was handed is already durable, rather than merely ending up so.
        /// </summary>
        public Func<WorkflowTriggerIndexSnapshot, ValueTask>? OnNotifying { get; set; }

        public async ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailOnCall == Calls)
                throw new InvalidOperationException("the route table refused the projection");
            if (OnNotifying is { } observe)
                await observe(snapshot);
            Snapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Stands in for the real indexer: extracts one binding and prepares it, or fails on demand. It prepares the
    /// trigger projection and <b>nothing else</b> — which is the whole of what <see cref="IWorkflowTriggerIndexer"/>
    /// advertises. Before T044b this stub also had to prepare the recurring projection, because the recurring
    /// decorator wore the indexer's contract; that hidden obligation is exactly what the split removed.
    /// </summary>
    private sealed class RecordingTriggerIndexer(IWorkflowTriggerBindingStore bindingStore) : IWorkflowTriggerIndexer
    {
        public Exception? Failure { get; set; }

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            var artifactId = executable.Identity.ArtifactId;
            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(activationId, artifactId, "node-start", "hash-1"),
                artifactId,
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
            return [binding];
        }
    }

    /// <summary>
    /// Stands in for <c>RecurringTriggerScheduleProjectionPreparer</c>: the coordinator's own collaborator for the
    /// recurring projection, prepared unconditionally (an explicit empty projection when nothing recurs), because
    /// the store refuses to activate an activation that has no prepared projection.
    /// </summary>
    /// <remarks>
    /// It materializes a real schedule rather than an empty set. That matters for the deactivation-compensation
    /// tests, which have to be able to see whether a replay <i>restored</i> the recurring projection or quietly
    /// erased it — an empty projection looks identical either way, which is the shape of blindness that let the
    /// T044b regression through in the first place.
    /// </remarks>
    private sealed class RecordingSchedulePreparer(IRecurringTriggerScheduleStore scheduleStore)
        : IRecurringTriggerScheduleProjectionPreparer
    {
        public Exception? Failure { get; set; }

        public async ValueTask PrepareActivationAsync(
            WorkflowExecutable executable,
            string activationId,
            string slotId,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            var artifactId = executable.Identity.ArtifactId;
            var schedule = new RecurringTriggerSchedule(
                RecurringTriggerSchedule.BuildId(activationId, artifactId, "node-start"),
                artifactId,
                "node-start",
                "test",
                "hash-1",
                RecurringScheduleKind.Interval,
                "PT1H",
                Now.AddHours(1),
                Now,
                ActivationId: activationId,
                SlotId: slotId,
                IsActive: false);
            await scheduleStore.PrepareActivationAsync(activationId, [schedule], cancellationToken);
        }
    }

    private sealed class RecordingReferenceStore(IWorkflowExecutableSourceReferenceStore inner) : IWorkflowExecutableSourceReferenceStore
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(sourceReferenceId, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);

        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            Calls.Add($"save:{reference.SourceReferenceId}");
            Throw("save");
            return inner.SaveAsync(reference, cancellationToken);
        }

        public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
        {
            Calls.Add($"retire:{sourceReferenceId}");
            Throw($"retire:{sourceReferenceId}");
            return inner.RetireAsync(sourceReferenceId, deletedAt, reason, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.DeleteExpiredOrRetiredAsync(batch, now, cancellationToken);

        private void Throw(string key)
        {
            if (FailOn.TryGetValue(key, out var exception))
                throw exception;
        }
    }

    private sealed class RecordingBindingStore(
        IWorkflowTriggerBindingStore inner,
        List<string> sequence,
        string label) : IWorkflowTriggerBindingStore
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

        private void Record(string call)
        {
            Calls.Add(call);
            sequence.Add($"{label}:{call}");
        }

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(binding, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default)
        {
            Record($"prepare:{activationId}");
            Throw($"prepare:{activationId}");
            return inner.PrepareActivationAsync(activationId, bindings, cancellationToken);
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByActivationAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            Record($"activate:{activationId}->{replacedActivationId}");
            Throw($"activate:{activationId}");
            return inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        }

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            Record($"delete:{activationId}");
            Throw($"delete:{activationId}");
            return inner.DeleteByActivationAsync(activationId, cancellationToken);
        }

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByStimulusAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByArtifactAsync(query, cancellationToken);

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByStimulusTypeAsync(query, cancellationToken);

        private void Throw(string key)
        {
            if (FailOn.TryGetValue(key, out var exception))
                throw exception;
        }
    }

    private sealed class RecordingScheduleStore(
        IRecurringTriggerScheduleStore inner,
        List<string> sequence,
        string label) : IRecurringTriggerScheduleStore
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

        /// <summary>Faults raised AFTER the underlying write applied — a genuinely partial removal.</summary>
        public Dictionary<string, Exception> FailAfterOn { get; } = new(StringComparer.Ordinal);

        private void Record(string call)
        {
            Calls.Add(call);
            sequence.Add($"{label}:{call}");
        }

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(schedule, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default)
        {
            Record($"prepare:{activationId}");
            Throw($"prepare:{activationId}");
            return inner.PrepareActivationAsync(activationId, schedules, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(RecurringTriggerScheduleActivationPageQuery query, CancellationToken cancellationToken = default)
        {
            // Recorded so a test can prove the coordinator does NOT read the projection back before writing it.
            Record($"list:{query.ActivationId}");
            return inner.ListByActivationPageAsync(query, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            Record($"activate:{activationId}->{replacedActivationId}");
            Throw($"activate:{activationId}");
            return inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        }

        public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            Record($"delete:{activationId}");
            Throw($"delete:{activationId}");
            await inner.DeleteByActivationAsync(activationId, cancellationToken);
            // FailAfterOn deletes and THEN throws. Refusing before the delete would leave the projection
            // standing, so "it is still serving afterwards" would hold whether or not compensation re-prepared
            // anything. Losing it first is what makes the replay observable rather than assumed.
            if (FailAfterOn.TryGetValue($"delete:{activationId}", out var afterFailure))
                throw afterFailure;
        }

        public ValueTask<IReadOnlyCollection<RecurringTriggerSchedule>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) =>
            inner.ListDueAsync(asOf, limit, cancellationToken);

        public ValueTask<RecurringTriggerSchedule?> FindAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(scheduleId, cancellationToken);

        public ValueTask<bool> TryAdvanceAsync(string scheduleId, DateTimeOffset expectedNextOccurrence, DateTimeOffset newNextOccurrence, CancellationToken cancellationToken = default) =>
            inner.TryAdvanceAsync(scheduleId, expectedNextOccurrence, newNextOccurrence, cancellationToken);

        public ValueTask DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) =>
            inner.DeleteByArtifactAsync(artifactId, cancellationToken);

        public ValueTask DeleteAsync(string scheduleId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(scheduleId, cancellationToken);

        private void Throw(string key)
        {
            if (FailOn.TryGetValue(key, out var exception))
                throw exception;
        }
    }
}
