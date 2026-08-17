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
        // empty serving projection, and observing before activation would publish a stale route table.
        Assert.Equal(
            ["prepare:activation-1", "activate:activation-1->"],
            harness.Bindings.Calls);
        Assert.Equal(
            ["prepare:activation-1", "activate:activation-1->"],
            harness.Schedules.Calls);
        Assert.Single(harness.Observer.Snapshots);
        Assert.True(harness.Observer.Snapshots[0].RequiresProjectionRefresh);
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
    public async Task The_same_artifact_no_op_applies_to_a_request_from_any_source()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1", source: WorkflowActivationSource.Publishing);
        harness.ResetCalls();

        // A NON-owning source asking for the artifact already being served is still a no-op, not a
        // ForeignSource rejection: FR-B-006 scopes the loud rejection to a DIFFERENT artifact.
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
    // Harness
    // ---------------------------------------------------------------------------------------------------

    private sealed class Harness
    {
        public Harness()
        {
            Bindings = new(new InMemoryWorkflowTriggerBindingStore());
            Schedules = new(new InMemoryRecurringTriggerScheduleStore());
            Indexer = new(Bindings);
            Coordinator = new WorkflowActivationCoordinator(
                Authority,
                References,
                Lease,
                new FixedTimeProvider(Now),
                Indexer,
                Bindings,
                Schedules,
                [Observer]);
        }

        public FailingActivationAuthority Authority { get; } = new();
        public RecordingReferenceStore References { get; } = new(new InMemoryWorkflowExecutableSourceReferenceStore());
        public RecordingBindingStore Bindings { get; }
        public RecordingScheduleStore Schedules { get; }
        public RecordingTriggerIndexer Indexer { get; }
        public RecordingObserver Observer { get; } = new();
        public FakeRootWriteLeaseManager Lease { get; } = new();
        public WorkflowActivationCoordinator Coordinator { get; }

        public WorkflowActivationCoordinator CreateWithoutTriggerSpine() =>
            new(Authority, References, Lease, new FixedTimeProvider(Now));

        public void ResetCalls()
        {
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
            string? sourceReferenceId = null) =>
            Coordinator.ActivateAsync(Command(activationId, artifactId, source, expectedRevision, sourceReferenceId));

        public WorkflowActivationCommand Command(
            string activationId,
            string artifactId,
            WorkflowActivationSource? source = null,
            long expectedRevision = 0,
            string? sourceReferenceId = null) =>
            new(
                Executable(artifactId),
                Reference(activationId, artifactId, sourceReferenceId),
                "default",
                activationId,
                source ?? WorkflowActivationSource.Publishing,
                expectedRevision);

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

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            _inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            if (Refuse.Contains(request.ActivationId))
                return await RefusedAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
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

        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailOnCall == Calls)
                throw new InvalidOperationException("the route table refused the projection");
            Snapshots.Add(snapshot);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for the real indexer chain: extracts one binding and prepares it, or fails on demand.</summary>
    private sealed class RecordingTriggerIndexer(IWorkflowTriggerBindingStore bindingStore) : IWorkflowTriggerIndexer
    {
        public Exception? Failure { get; set; }

        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The activation lifecycle never uses the artifact-scoped write path.");

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

    private sealed class RecordingBindingStore(IWorkflowTriggerBindingStore inner) : IWorkflowTriggerBindingStore
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(binding, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default)
        {
            Calls.Add($"prepare:{activationId}");
            Throw($"prepare:{activationId}");
            return inner.PrepareActivationAsync(activationId, bindings, cancellationToken);
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByActivationAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"activate:{activationId}->{replacedActivationId}");
            Throw($"activate:{activationId}");
            return inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        }

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{activationId}");
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

    private sealed class RecordingScheduleStore(IRecurringTriggerScheduleStore inner) : IRecurringTriggerScheduleStore
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

        public ValueTask<RecurringTriggerSchedule> SaveAsync(RecurringTriggerSchedule schedule, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(schedule, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<RecurringTriggerSchedule> schedules, CancellationToken cancellationToken = default)
        {
            Calls.Add($"prepare:{activationId}");
            Throw($"prepare:{activationId}");
            return inner.PrepareActivationAsync(activationId, schedules, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByActivationPageAsync(RecurringTriggerScheduleActivationPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByActivationPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<RecurringTriggerSchedule>> ListByArtifactPageAsync(RecurringTriggerScheduleArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"activate:{activationId}->{replacedActivationId}");
            Throw($"activate:{activationId}");
            return inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
        }

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{activationId}");
            Throw($"delete:{activationId}");
            return inner.DeleteByActivationAsync(activationId, cancellationToken);
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
