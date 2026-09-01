using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowActivationCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonDocument EmptyDescriptor = JsonDocument.Parse("{}");
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    [Fact]
    public async Task Activation_runs_the_atomic_logical_sequence_and_stamps_the_reference()
    {
        var harness = new Harness();
        var result = await harness.ActivateAsync("activation-1", "artifact-1");

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationReferenceIdentity.Create("activation-1"), result.Reference!.SourceReferenceId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), result.Reference.SlotId);
        Assert.Equal(
            ["lease", "reference:save", "projection:prepare", "authority:activate", "projection:activate", "observer"],
            harness.Calls);

        var bindings = await harness.Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("test", "hash-1"));
        Assert.Single(bindings.Items);
        Assert.True(bindings.Items[0].IsActive);
    }

    [Fact]
    public async Task Replacement_retires_the_predecessor_and_restores_reference_identity()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var second = await harness.ActivateAsync("activation-2", "artifact-2", expectedRevision: first.Slot.Revision);

        Assert.True(second.Succeeded);
        Assert.Equal("activation-1", second.ReplacedActivationId);
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Equal(WorkflowActivationCoordinator.ReplacedRetireReason, predecessor!.DeletedReason);
        Assert.NotNull(predecessor.DeletedAt);
        Assert.Equal(WorkflowActivationOutcome.Activated, second.Outcome);
    }

    [Fact]
    public async Task Same_artifact_is_idempotent_without_writes_but_takeover_transfers_ownership()
    {
        var harness = new Harness();
        var first = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var noOp = await harness.ActivateAsync("activation-2", "artifact-1", expectedRevision: first.Slot.Revision, source: Importer);
        Assert.True(noOp.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, noOp.Outcome);
        Assert.Empty(harness.Calls);

        var takeover = await harness.ActivateAsync(
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
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");

        var result = await harness.ActivateAsync("candidate", "artifact-2", expectedRevision: 0);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Conflict, result.Outcome);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, result.Conflict);
        Assert.Equal("incumbent", result.Slot.ActiveActivationId);
        Assert.Empty(await harness.BindingsForAsync("candidate"));
        Assert.Single(await harness.BindingsForAsync("incumbent"));
        var candidate = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate"));
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, candidate!.DeletedReason);
        Assert.Equal(incumbent.Slot.Revision, result.Slot.Revision);
    }

    [Fact]
    public async Task Foreign_owner_refusal_does_not_replace_the_incumbent()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("published", "artifact-1", source: WorkflowActivationSource.Publishing);

        var result = await harness.ActivateAsync(
            "imported",
            "artifact-2",
            expectedRevision: incumbent.Slot.Revision,
            source: Importer);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, result.Conflict);
        Assert.Equal("published", result.Slot.ActiveActivationId);
        Assert.Empty(await harness.BindingsForAsync("imported"));
        Assert.Single(await harness.BindingsForAsync("published"));
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("imported")))!.DeletedReason);
    }

    [Fact]
    public async Task Each_failure_after_prepare_is_reported_and_compensated()
    {
        var cases = new (WorkflowActivationStep Step, Action<Harness> Configure)[]
        {
            (WorkflowActivationStep.SlotTransition, harness => harness.Authority.ThrowOnActivate = new InvalidOperationException("authority unavailable")),
            (WorkflowActivationStep.ProjectionActivation, harness => harness.Bindings.ThrowOnActivate = new InvalidOperationException("binding activation unavailable")),
            (WorkflowActivationStep.TriggerObserverNotification, harness => harness.Observer.ThrowOnce = true),
            (WorkflowActivationStep.PredecessorReferenceRetirement, harness => harness.References.ThrowOnRetire = true)
        };

        foreach (var (step, configure) in cases)
        {
            var harness = new Harness();
            var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
            configure(harness);

            var result = await harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

            Assert.False(result.Succeeded);
            Assert.Equal(WorkflowActivationOutcome.Failed, result.Outcome);
            Assert.Equal(step, result.FailedStep);
            Assert.NotNull(result.Diagnostic);
            Assert.Equal("incumbent", result.Slot.ActiveActivationId);
            Assert.Empty(await harness.BindingsForAsync("candidate"));
            Assert.Single(await harness.BindingsForAsync("incumbent"));
            var candidate = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate"));
            Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, candidate!.DeletedReason);
        }
    }

    [Fact]
    public async Task Compensation_failure_is_reported_without_masking_the_original_failure()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        harness.Bindings.ThrowOnActivate = new InvalidOperationException("serving projection failed");
        harness.Authority.RefuseActivationIds.Add("incumbent");

        var result = await harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationStep.ProjectionActivation, result.FailedStep);
        Assert.Contains("serving projection failed", result.Diagnostic!, StringComparison.Ordinal);
        Assert.NotNull(result.CompensationDiagnostic);
        Assert.Contains("Authority compensation failed", result.CompensationDiagnostic!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_predecessor_retirement_reports_fail_closed_restore_compensation()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        harness.References.ThrowAfterRetire = new InvalidOperationException("retirement response failed");
        harness.References.FailRestore = true;

        var result = await harness.ActivateAsync("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationStep.PredecessorReferenceRetirement, result.FailedStep);
        Assert.Contains("Predecessor reference compensation failed", result.CompensationDiagnostic!, StringComparison.Ordinal);
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        Assert.Equal(WorkflowActivationCoordinator.ReplacedRetireReason, predecessor!.DeletedReason);
        Assert.NotNull(predecessor.DeletedAt);
    }

    [Fact]
    public async Task Deactivation_removes_serving_projection_and_is_idempotent()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.ResetCalls();

        var result = await harness.DeactivateAsync(activated.Slot.Revision);

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Null(result.Slot.ActiveActivationId);
        Assert.Empty(await harness.ServingBindingsAsync());
        Assert.Equal(["authority:deactivate", "projection:delete", "observer"], harness.Calls);

        var repeat = await harness.DeactivateAsync(result.Slot.Revision);
        Assert.True(repeat.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.AlreadyInactive, repeat.Outcome);
    }

    [Fact]
    public async Task Deactivation_projection_failure_restores_authority_and_projection()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        harness.Bindings.ThrowOnDelete = new InvalidOperationException("projection removal failed");

        var result = await harness.DeactivateAsync(activated.Slot.Revision);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationStep.ProjectionRemoval, result.FailedStep);
        Assert.Equal("activation-1", result.Slot.ActiveActivationId);
        Assert.NotEmpty(await harness.ServingBindingsAsync());
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_a_compensated_failure()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1"), cancellation.Token));
        Assert.Empty(harness.Calls);
    }

    [Fact]
    public async Task Cancellation_during_preparation_is_rethrown_at_the_boundary()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Indexer.CancelBeforeThrow = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1"), cancellation.Token));
        Assert.Contains("reference:save", harness.Calls);
        Assert.DoesNotContain("authority:activate", harness.Calls);
    }

    [Fact]
    public async Task Cancellation_after_source_reference_save_retires_the_candidate_reference()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.References.CancelAfterSave = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1"), cancellation.Token));

        Assert.Null((await harness.Authority.FindAsync("definition-1", "default"))?.ActiveActivationId);
        var reference = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1"));
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, reference!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_after_projection_preparation_removes_candidate_projection()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Indexer.CancelAfterPrepare = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(harness.Command("activation-1", "artifact-1"), cancellation.Token));

        Assert.Empty(await harness.BindingsForAsync("activation-1"));
        Assert.Null((await harness.Authority.FindAsync("definition-1", "default"))?.ActiveActivationId);
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1")))!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_after_activation_slot_flip_restores_the_predecessor()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Authority.CancelAfterActivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("incumbent", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("incumbent"));
        Assert.Empty(await harness.BindingsForAsync("candidate"));
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate")))!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_during_projection_activation_restores_the_predecessor()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Bindings.CancelAfterActivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("incumbent", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("incumbent"));
        Assert.Empty(await harness.BindingsForAsync("candidate"));
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate")))!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_during_observer_notification_restores_the_predecessor()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Observer.CancelAfterCall = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("incumbent", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("incumbent"));
        Assert.Empty(await harness.BindingsForAsync("candidate"));
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate")))!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_after_predecessor_reference_retirement_restores_the_predecessor_reference()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.References.CancelAfterRetire = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        Assert.Equal("incumbent", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("incumbent"));
        Assert.Empty(await harness.BindingsForAsync("candidate"));
        Assert.Null((await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent")))!.DeletedAt);
        Assert.Equal(
            WorkflowActivationCoordinator.FailedRetireReason,
            (await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate")))!.DeletedReason);
    }

    [Fact]
    public async Task Cancellation_after_predecessor_reference_retirement_does_not_overwrite_a_superseding_reference()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        using var cancellation = new CancellationTokenSource();
        harness.References.ReplaceAfterRetire = predecessor! with
        {
            ArtifactId = "artifact-newer",
            ActivationId = "newer",
            DeletedAt = null,
            DeletedReason = null
        };
        harness.References.CancelAfterRetire = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        var current = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        Assert.Equal("artifact-newer", current!.ArtifactId);
        Assert.Equal("newer", current.ActivationId);
        Assert.Null(current.DeletedAt);
    }

    [Fact]
    public async Task Cancellation_after_predecessor_reference_snapshot_conflict_leaves_the_superseding_reference()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        using var cancellation = new CancellationTokenSource();
        harness.References.ReplaceBeforeRestore = predecessor! with
        {
            ArtifactId = "artifact-raced",
            ActivationId = "raced",
            DeletedAt = null,
            DeletedReason = null
        };
        harness.References.CancelAfterRetire = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        var current = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        Assert.Equal("artifact-raced", current!.ArtifactId);
        Assert.Equal("raced", current.ActivationId);
        Assert.Null(current.DeletedAt);
    }

    [Fact]
    public async Task Cancellation_after_predecessor_reference_snapshot_conflict_leaves_sidecar_only_superseding_reference()
    {
        var harness = new Harness();
        var incumbent = await harness.ActivateAsync("incumbent", "artifact-1");
        var predecessor = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        using var cancellation = new CancellationTokenSource();
        var replacement = predecessor! with
        {
            Layout = [new WorkflowExecutableLayoutRecord("node-start", 42, 43, 120, 80, JsonSerializer.SerializeToElement(new { sidecar = true }))],
            LayoutSidecar = new ExecutableLayoutSidecar([
                new ExecutableLayoutBoundarySegment(
                    "boundary-1",
                    new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.TemplateBoundary, "boundary-1")]),
                    "template-v2",
                    [new ExecutableActivityLayoutRecord(
                        "template-node",
                        "authored-node-start",
                        "node-start",
                        42,
                        43,
                        ActivityType: "test/activity",
                        ActivityTypeVersion: "2.0.0",
                        HasPinnedGeometry: false)],
                    [new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.NestedPlacement, "nested-1")])])
            ]),
            AuthoredInputs = [new WorkflowExecutableAuthoredInputRecord(
                "node-start",
                "input",
                "json",
                JsonSerializer.SerializeToElement(new { value = 1 }))],
            ActivityPresentation = [new WorkflowExecutableActivityPresentationRecord("node-start", "New display", "New description")],
            DeletedAt = null,
            DeletedReason = null
        };
        harness.References.ReplaceBeforeRestore = replacement;
        harness.References.CancelAfterRetire = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.Coordinator.ActivateAsync(
                harness.Command("candidate", "artifact-2", expectedRevision: incumbent.Slot.Revision), cancellation.Token));

        var current = await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("incumbent"));
        Assert.Equal(replacement.Layout, current!.Layout);
        Assert.Equal(replacement.LayoutSidecar, current.LayoutSidecar);
        Assert.Equal(replacement.AuthoredInputs, current.AuthoredInputs);
        Assert.Equal(replacement.ActivityPresentation, current.ActivityPresentation);
        Assert.Null(current.DeletedAt);
    }

    [Fact]
    public async Task Cancellation_after_deactivation_slot_flip_restores_authority_and_projection()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Authority.CancelAfterDeactivate = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.DeactivateAsync(activated.Slot.Revision, cancellation.Token));

        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("activation-1"));
        Assert.Null((await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1")))!.DeletedAt);
    }

    [Fact]
    public async Task Cancellation_during_deactivation_projection_removal_restores_authority_and_projection()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Bindings.CancelAfterDelete = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.DeactivateAsync(activated.Slot.Revision, cancellation.Token));

        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("activation-1"));
        Assert.Null((await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1")))!.DeletedAt);
    }

    [Fact]
    public async Task Cancellation_during_deactivation_observer_notification_restores_authority_and_projection()
    {
        var harness = new Harness();
        var activated = await harness.ActivateAsync("activation-1", "artifact-1");
        using var cancellation = new CancellationTokenSource();
        harness.Observer.CancelAfterCall = cancellation;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await harness.DeactivateAsync(activated.Slot.Revision, cancellation.Token));

        Assert.Equal("activation-1", (await harness.Authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
        Assert.Single(await harness.BindingsForAsync("activation-1"));
        Assert.Null((await harness.References.FindAsync(WorkflowActivationReferenceIdentity.Create("activation-1")))!.DeletedAt);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Bindings = new(new InMemoryWorkflowTriggerBindingStore(), Calls);
            Indexer = new(Bindings, Calls);
            References = new(new InMemoryWorkflowExecutableSourceReferenceStore(), Calls);
            Authority = new(new InMemoryWorkflowActivationAuthority(), Calls);
            Observer = new(Calls);
            Lease = new(Calls);
            Coordinator = new(Authority, References, Lease, new FixedTimeProvider(Now), Indexer, Bindings, triggerObservers: [Observer]);
        }

        public List<string> Calls { get; } = [];
        public RecordingAuthority Authority { get; }
        public RecordingReferenceStore References { get; }
        public RecordingBindingStore Bindings { get; }
        public RecordingIndexer Indexer { get; }
        public RecordingObserver Observer { get; }
        public RecordingLease Lease { get; }
        public WorkflowActivationCoordinator Coordinator { get; }

        public void ResetCalls() => Calls.Clear();

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

        public ValueTask<WorkflowActivationResult> DeactivateAsync(long expectedRevision, CancellationToken cancellationToken = default, WorkflowActivationSource? source = null) =>
            Coordinator.DeactivateAsync(new(Executable("artifact-1"), "default", source ?? WorkflowActivationSource.Publishing, expectedRevision), cancellationToken);

        public async Task<IReadOnlyCollection<WorkflowTriggerBinding>> ServingBindingsAsync() =>
            (await Bindings.ListByStimulusAsync(new WorkflowTriggerBindingPageQuery("test", "hash-1"))).Items;

        public async Task<IReadOnlyCollection<WorkflowTriggerBinding>> BindingsForAsync(string activationId) =>
            (await Bindings.ListByActivationAsync(new WorkflowTriggerBindingActivationPageQuery(activationId))).Items;

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLease(List<string> calls) : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            ExecuteCoreAsync(artifactId, leaseId, write, cancellationToken);

        private async ValueTask ExecuteCoreAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken)
        {
            calls.Add("lease");
            await write(cancellationToken);
        }
    }

    private sealed class RecordingAuthority(IWorkflowActivationAuthority inner, List<string> calls) : IWorkflowActivationAuthority
    {
        public Exception? ThrowOnActivate { get; set; }
        public bool RefuseDeactivation { get; set; }
        public HashSet<string> RefuseActivationIds { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource? CancelAfterActivate { get; set; }
        public CancellationTokenSource? CancelAfterDeactivate { get; set; }

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            calls.Add("authority:activate");
            if (ThrowOnActivate is { } failure)
                throw failure;
            if (RefuseActivationIds.Contains(request.ActivationId))
            {
                var slot = await inner.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken) ??
                    new(WorkflowActivationSlotIdentity.Create(request.WorkflowDefinitionId, request.SlotName), request.WorkflowDefinitionId, request.SlotName, null, null, 0, request.UpdatedAt);
                return new(false, slot, Conflict: WorkflowActivationConflict.ForeignSource, Diagnostic: "authority compensation refused");
            }
            var result = await inner.TryActivateAsync(request, cancellationToken);
            if (CancelAfterActivate is { } source)
            {
                CancelAfterActivate = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            return result;
        }

        public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            calls.Add("authority:deactivate");
            if (RefuseDeactivation)
            {
                var slot = await inner.FindAsync(workflowDefinitionId, slotName, cancellationToken) ??
                    new(WorkflowActivationSlotIdentity.Create(workflowDefinitionId, slotName), workflowDefinitionId, slotName, null, null, 0, updatedAt);
                return new(false, slot, Conflict: WorkflowActivationConflict.RevisionMismatch, Diagnostic: "authority compensation refused");
            }
            var result = await inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
            if (CancelAfterDeactivate is { } cancelSource)
            {
                CancelAfterDeactivate = null;
                cancelSource.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            return result;
        }
    }

    private sealed class RecordingIndexer(RecordingBindingStore bindingStore, List<string> calls) : IWorkflowTriggerIndexer
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
                beforeSource.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            if (Failure is { } failure)
                throw failure;

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
                afterSource.Cancel();
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

        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Calls++;
            calls.Add("observer");
            if (CancelAfterCall is { } source)
            {
                CancelAfterCall = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            if (ThrowOnce)
            {
                ThrowOnce = false;
                throw new InvalidOperationException("observer projection failed");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReferenceStore(IWorkflowExecutableSourceReferenceStore inner, List<string> calls) : IWorkflowExecutableSourceReferenceStore
    {
        public bool ThrowOnRetire { get; set; }
        public Exception? ThrowAfterRetire { get; set; }
        public bool FailRestore { get; set; }
        public CancellationTokenSource? CancelAfterSave { get; set; }
        public CancellationTokenSource? CancelAfterRetire { get; set; }
        public WorkflowExecutableSourceReference? ReplaceAfterRetire { get; set; }
        public WorkflowExecutableSourceReference? ReplaceBeforeRestore { get; set; }

        public async ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            calls.Add("reference:save");
            await inner.SaveAsync(reference, cancellationToken);
            if (CancelAfterSave is { } source)
            {
                CancelAfterSave = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => inner.FindAsync(sourceReferenceId, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactPageAsync(query, cancellationToken);
        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);

        public async ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
        {
            calls.Add("reference:retire");
            if (ThrowOnRetire && reason == WorkflowActivationCoordinator.ReplacedRetireReason)
                throw new InvalidOperationException("predecessor retirement failed");
            var retired = await inner.RetireAsync(sourceReferenceId, deletedAt, reason, cancellationToken);
            if (ReplaceAfterRetire is { } replacement)
            {
                ReplaceAfterRetire = null;
                await inner.SaveAsync(replacement, cancellationToken);
            }
            if (ThrowAfterRetire is { } failure && reason == WorkflowActivationCoordinator.ReplacedRetireReason)
            {
                ThrowAfterRetire = null;
                throw failure;
            }
            if (CancelAfterRetire is { } source)
            {
                CancelAfterRetire = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            return retired;
        }

        public async ValueTask<bool> TryRestoreAsync(
            WorkflowExecutableSourceReference expectedRetiredReference,
            WorkflowExecutableSourceReference restoredReference,
            CancellationToken cancellationToken = default)
        {
            if (FailRestore)
            {
                FailRestore = false;
                return false;
            }
            if (ReplaceBeforeRestore is { } replacement)
            {
                ReplaceBeforeRestore = null;
                await inner.SaveAsync(replacement, cancellationToken);
            }

            return await inner.TryRestoreAsync(expectedRetiredReference, restoredReference, cancellationToken);
        }

        public ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => inner.DeleteAsync(sourceReferenceId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => inner.DeleteExpiredOrRetiredAsync(batch, now, cancellationToken);
    }

    private sealed class RecordingBindingStore(IWorkflowTriggerBindingStore inner, List<string> calls) : IWorkflowTriggerBindingStore
    {
        public Exception? ThrowOnActivate { get; set; }
        public Exception? ThrowOnDelete { get; set; }
        public CancellationTokenSource? CancelAfterActivate { get; set; }
        public CancellationTokenSource? CancelAfterDelete { get; set; }

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => inner.SaveAsync(binding, cancellationToken);

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default) => inner.PrepareActivationAsync(activationId, bindings, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default) => inner.ListByActivationAsync(query, cancellationToken);

        public async ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            calls.Add("projection:activate");
            if (ThrowOnActivate is { } failure)
                throw failure;
            await inner.ActivateAsync(activationId, replacedActivationId, cancellationToken);
            if (CancelAfterActivate is { } source)
            {
                CancelAfterActivate = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public async ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default)
        {
            calls.Add("projection:delete");
            if (ThrowOnDelete is { } failure)
                throw failure;
            await inner.DeleteByActivationAsync(activationId, cancellationToken);
            if (CancelAfterDelete is { } source)
            {
                CancelAfterDelete = null;
                source.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        }

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => inner.DeleteByArtifactAsync(artifactId, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) => inner.ListByStimulusAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) => inner.ListByArtifactAsync(query, cancellationToken);
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) => inner.ListByStimulusTypeAsync(query, cancellationToken);
    }
}
