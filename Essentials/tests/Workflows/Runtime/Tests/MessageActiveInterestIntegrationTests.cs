using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Actual volatile owner writes only; no cohort acceptance, model coverage or rollback qualification.</summary>
public sealed class MessageActiveInterestIntegrationTests
{
    [Fact]
    public async Task Real_started_handler_co_stages_native_root_and_seed_state_under_current_ownership()
    {
        var f = await Fixture.Create();
        await f.StartThroughActualHandler();
        var actual = Assert.IsType<WorkflowExecutionState>(await f.Executions.FindAsync("run"));
        Assert.Equal(WorkflowExecutionStatus.Running, actual.Status);
        Assert.NotEmpty((await f.Values.ListPageAsync(new("run"))).Items);
        var observed = await f.Active.ObserveAsync(f.Scope);
        var root = Assert.Single(observed.Roots);
        Assert.Equal(1, observed.Epoch); Assert.Equal("definition", root.Definition.DefinitionId);
        Assert.Equal(["Payment"], Assert.Single(root.Declarations).MessageNames);
        Assert.Null(root.Owner.RootVariableFrame); Assert.Empty(root.Owner.SystemMetadata);
        Assert.Throws<NotSupportedException>(() => f.Publication.RequireCompleteAcceptanceCoverage());
    }

    [Fact]
    public async Task First_physical_suspended_upsert_enrolls_even_when_start_was_folded()
    {
        var f = await Fixture.Create(coalescing: true);
        var session = new RuntimeCoalescingSession("run", f.Queue, new PersistenceModeOptions(), f.Checkpoints,
            new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false));
        using var current = f.Sessions.Push(session);
        await f.StartThroughActualHandler(fold: true);
        Assert.Null(await f.Executions.FindAsync("run"));
        Assert.NotNull(await f.SelectedExecutions.FindAsync("run"));
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        var suspended = (await f.SelectedExecutions.FindAsync("run"))! with { Status = WorkflowExecutionStatus.Suspended };
        var forwarder = new CoalescingRuntimeCheckpointCommitStore(new(f.Checkpoints), f.Sessions);
        await forwarder.CommitAsync(f.NewCommit(suspended, "folded", RuntimeCheckpointNames.WorkflowSuspended), Fixture.Immediate);
        Assert.Equal(["Payment"], Names(await f.Active.ObserveAsync(f.Scope)));
        Assert.Equal(WorkflowExecutionStatus.Suspended, (await f.Executions.FindAsync("run"))!.Status);
        Assert.NotEmpty((await f.Values.ListPageAsync(new("run"))).Items);
    }

    [Fact]
    public async Task Active_root_retains_old_material_and_source_after_retirement_and_replacement()
    {
        var f = await Fixture.Create(); await f.StartThroughActualHandler();
        var before = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        await f.References.RetireAsync(f.Reference.SourceReferenceId, Fixture.Now, "replacement");
        var replacement = f.Executable("replacement", "Shipping"); await f.Material.SaveAsync(replacement);
        await f.References.SaveAsync(f.Reference with { SourceReferenceId = "replacement-ref", ArtifactId = "replacement", DefinitionVersionId = "v2" });
        await f.Commit((await f.Executions.FindAsync("run"))! with { Status = WorkflowExecutionStatus.Suspended }, "suspend");
        var after = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        Assert.Equal(before.Incarnation, after.Incarnation); Assert.Equal(before.MaterialIncarnation, after.MaterialIncarnation);
        Assert.Equal(["Payment"], Names(await f.Active.ObserveAsync(f.Scope)));
    }

    [Fact]
    public async Task Actual_publication_switch_and_active_lifecycle_share_one_scope_epoch_but_independent_lifetimes()
    {
        var f = await Fixture.Create(); var shared = f.Checkpoints.State.MessagePublicationInterests!;
        var bindings = new InMemoryWorkflowTriggerBindingStore(); await bindings.PrepareActivationAsync("activation", []);
        var executable = f.Material.ReadNativeMaterialForMessageInterest("artifact")!.NativeWorkflow!;
        await f.Publication.PrepareAsync(executable, "activation", f.Reference.SlotId!, "default", CancellationToken.None);
        var activation = new InMemoryWorkflowActivationSwitch(f.Publication, shared.Authority, f.References, f.Clock, bindings);
        var started = await activation.TryActivateAsync(new("definition", "default", "activation", WorkflowActivationSource.Publishing, 0, Fixture.Now));
        Assert.True(started.Succeeded); Assert.Equal(1, (await f.Publication.ObservePublicationAsync(f.Scope)).Epoch);
        await f.Commit(f.State(), "actual-start"); Assert.Equal(2, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        var closed = await activation.TryDeactivateAsync(new("definition", "default", WorkflowActivationSource.Publishing, started.Slot.Revision, Fixture.Now), []);
        Assert.True(closed.Succeeded);
        var publication = await f.Publication.ObservePublicationAsync(f.Scope); var active = await f.Active.ObserveAsync(f.Scope);
        Assert.Equal(3, publication.Epoch); Assert.Equal(publication.Epoch, active.Epoch); Assert.Empty(publication.Names);
        Assert.Equal(["Payment"], Names(active));
        await f.Commit(f.State() with { Status = WorkflowExecutionStatus.Completed }, "terminal");
        Assert.Equal(4, (await f.Publication.ObservePublicationAsync(f.Scope)).Epoch); Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task A_retired_reference_can_supply_the_first_coalesced_physical_start_capture()
    {
        var f = await Fixture.Create(); await f.References.RetireAsync(f.Reference.SourceReferenceId, Fixture.Now, "unpublished");
        await f.Commit(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "first-physical");
        Assert.Equal(["Payment"], Names(await f.Active.ObserveAsync(f.Scope)));
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed, RuntimeCheckpointNames.WorkflowCompleted)]
    [InlineData(WorkflowExecutionStatus.Faulted, "failure-boundary")]
    [InlineData(WorkflowExecutionStatus.Cancelled, RuntimeCheckpointNames.ActivityCancelled)]
    [InlineData(WorkflowExecutionStatus.Cancelled, RuntimeCheckpointNames.RuntimeAlterationJob)]
    public async Task Actual_terminal_status_closes_independently_of_checkpoint_name(WorkflowExecutionStatus status, string name)
    {
        var f = await Fixture.Create(); await f.Commit(f.State(), "start");
        await f.Commit((await f.Executions.FindAsync("run"))! with { Status = status, CompletedAt = Fixture.Now }, "terminal", name);
        var observed = await f.Active.ObserveAsync(f.Scope);
        Assert.Equal(2, observed.Epoch); Assert.Empty(observed.Roots);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Commit(f.State(), "reopen"));
        Assert.Equal(status, (await f.Executions.FindAsync("run"))!.Status);
    }

    [Fact]
    public async Task A_terminal_named_boundary_with_nonterminal_state_does_not_close_interest()
    {
        var f = await Fixture.Create(); await f.Commit(f.State(), "start");
        await f.Commit(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "not-terminal", RuntimeCheckpointNames.ActivityCancelled);
        Assert.Equal(1, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task Terminal_first_capture_never_opens_eligibility_and_deleted_history_cannot_reopen()
    {
        var f = await Fixture.Create(); await f.Commit(f.State() with { Status = WorkflowExecutionStatus.Faulted }, "terminal-first");
        Assert.Equal(0, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        Assert.True(await f.Executions.DeleteAsync("run"));
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Commit(f.State(), "recreated"));
        Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Fact]
    public async Task Real_terminal_deletion_preserves_interest_closure_and_fences_recreation()
    {
        var f = await Fixture.Create(); await f.Commit(f.State(), "start");
        var deletion = f.Deletion(); Assert.True(deletion.IsSupported);
        var active = (await deletion.ReadAsync("run"))!;
        Assert.Equal(WorkflowExecutionDeletionResult.NotTerminal, await deletion.TryDeleteTerminalAsync(active));
        Assert.False(await f.Executions.IsDeletionTombstonedAsync("run"));
        await f.Commit(f.State() with { Status = WorkflowExecutionStatus.Completed }, "terminal");
        Assert.Equal(WorkflowExecutionDeletionResult.Changed, await deletion.TryDeleteTerminalAsync(active));
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync((await deletion.ReadAsync("run"))!));
        Assert.True(await f.Executions.IsDeletionTombstonedAsync("run"));
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Commit(f.State(), "recreate"));
        Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Fact]
    public async Task Direct_managed_saves_and_nonterminal_deletes_refuse_while_read_model_copies_still_work()
    {
        var f = await Fixture.Create();
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Executions.SaveAsync(f.State()).AsTask());
        Assert.Null(await f.Executions.FindAsync("run"));
        await f.Commit(f.State(), "start");
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Executions.DeleteAsync("run").AsTask());
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        var readModel = new InMemoryWorkflowExecutionStateStore();
        await readModel.SaveAsync((await f.Executions.FindAsync("run"))!);
        Assert.NotNull(await readModel.FindAsync("run"));
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task Coalescing_forwarder_uses_exact_selected_physical_store_and_cannot_bypass_managed_save()
    {
        var f = await Fixture.Create(coalescing: true);
        await f.StartThroughActualHandler();
        Assert.NotNull(await f.Executions.FindAsync("run")); Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        await Assert.ThrowsAsync<NotSupportedException>(() => f.SelectedExecutions.SaveAsync(f.State()).AsTask());
    }

    [Fact]
    public async Task Canonical_pinned_source_wins_over_shared_materials_historical_definition_carrier()
    {
        var f = await Fixture.Create();
        var another = f.Reference with { SourceReferenceId = WorkflowActivationReferenceIdentity.Create("other-activation"), ActivationId = "other-activation",
            SlotId = WorkflowActivationSlotIdentity.Create("other\ud800", "default"), DefinitionId = "other\ud800", DefinitionVersionId = "other-version" };
        await f.References.SaveAsync(another);
        await f.Commit(f.State() with { PinnedSource = WorkflowExecutableSourceProvenance.From(another) }, "canonical");
        Assert.Equal("definition", f.Material.ReadNativeMaterialForMessageInterest("artifact")!.Identity.DefinitionId);
        Assert.Equal("other\ud800", Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots).Definition.DefinitionId);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("tenant")]
    [InlineData("authority")]
    [InlineData("partition")]
    public async Task Subsequent_upsert_cannot_retarget_the_create_only_owner(string changed)
    {
        var f = await Fixture.Create(); await f.Commit(f.State(), "start");
        var state = f.State();
        state = changed switch
        {
            "definition" => state with { PinnedSource = state.PinnedSource! with { DefinitionId = "other" } },
            "tenant" => state with { TenantId = "other" },
            "authority" => state with { Authority = new("other", "caller") },
            _ => state with { Partition = new("other") }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Commit(state, "changed"));
        Assert.Equal("definition", Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots).Definition.DefinitionId);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("epoch")]
    public async Task Provider_and_epoch_failures_precede_actual_scheduler_consumption_and_physical_state(string failure)
    {
        var f = await Fixture.Create(noProvider: failure == "provider");
        var work = new RuntimeSchedulerWorkItem("work", "run", "command", WorkflowExecutionCommandKind.Checkpoint, "envelope", "idempotency", Fixture.Now, Fixture.Now);
        await f.Queue.EnqueueAsync(work);
        var claim = (await f.Queue.ClaimAsync(new("run", "worker", Fixture.Now, TimeSpan.FromMinutes(1))))!;
        if (failure == "epoch") f.Checkpoints.State.MessagePublicationInterests!.Epochs[f.Scope] = long.MaxValue;
        var commit = f.NewCommit(f.State(), "rejected") with
        { StateChanges = f.NewCommit(f.State(), "ignored").StateChanges.WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)]) };
        if (failure == "provider")
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.Checkpoints.CommitAsync(commit, Fixture.Immediate).AsTask());
        else
            await Assert.ThrowsAsync<OverflowException>(() => f.Checkpoints.CommitAsync(commit, Fixture.Immediate).AsTask());
        Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery("run"))); Assert.Null(await f.Executions.FindAsync("run"));
        Assert.False(f.Checkpoints.State.Commits.ContainsKey("rejected"));
        // Preparation refusal did not poison otherwise coherent (still empty) physical history.
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task Actual_single_queue_claim_loss_proves_no_writes_without_poisoning_the_projection()
    {
        var f = await Fixture.Create();
        await f.Queue.EnqueueAsync(new("work", "run", "command", WorkflowExecutionCommandKind.Checkpoint, "envelope", "key", Fixture.Now, Fixture.Now));
        var first = (await f.Queue.ClaimAsync(new("run", "first", Fixture.Now, TimeSpan.FromSeconds(1))))!;
        var successor = (await f.Queue.ClaimAsync(new("run", "second", Fixture.Now.AddSeconds(2), TimeSpan.FromMinutes(1))))!;
        var commit = f.NewCommit(f.State(), "stale");
        commit = commit with { StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(first)]) };
        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() => f.Checkpoints.CommitAsync(commit, Fixture.Immediate).AsTask());
        Assert.Null(await f.Executions.FindAsync("run")); Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        Assert.Single(await f.Queue.ListAllAsync("run"));
        Assert.True((await f.Queue.RenewClaimAsync(successor, Fixture.Now.AddSeconds(3), TimeSpan.FromMinutes(1))).Succeeded);
        await f.Commit(f.State(), "fresh"); Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task Actual_two_item_consume_latches_uncertainty_after_first_removal_and_second_stale_refusal()
    {
        var f = await Fixture.Create();
        // Obtain real historical evidence for the second, now-stale claim. FIFO permits only one live head.
        await f.Queue.EnqueueAsync(new("z-stale", "run", "old-command", WorkflowExecutionCommandKind.Checkpoint, "old-envelope", "old-key", Fixture.Now, Fixture.Now));
        var stale = (await f.Queue.ClaimAsync(new("run", "old-owner", Fixture.Now, TimeSpan.FromMinutes(1))))!;
        Assert.True((await f.Queue.CompleteClaimAsync(stale)).Succeeded);
        await f.Queue.EnqueueAsync(new("a-current", "run", "command", WorkflowExecutionCommandKind.Checkpoint, "envelope", "key", Fixture.Now, Fixture.Now));
        var current = (await f.Queue.ClaimAsync(new("run", "current-owner", Fixture.Now, TimeSpan.FromMinutes(1))))!;
        Assert.Equal("a-current", Assert.Single(await f.Queue.ListAllAsync("run")).WorkItemId);
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        var commit = f.NewCommit(f.State(), "partial-consume");
        commit = commit with { StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems(
            [ConsumedSchedulerWorkItem.FromClaim(current), ConsumedSchedulerWorkItem.FromClaim(stale)]) };
        RuntimeCheckpointCommitValidator.Validate(commit);
        Assert.Equal(["a-current", "z-stale"], commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId));
        var rejected = await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() => f.Checkpoints.CommitAsync(commit, Fixture.Immediate).AsTask());
        Assert.Equal("z-stale", rejected.WorkItemId);
        Assert.Empty(await f.Queue.ListAllAsync("run")); // The first actual consume happened; this is not a no-write failure.
        Assert.Null(await f.Executions.FindAsync("run"));
        Assert.False(f.Checkpoints.State.Commits.ContainsKey("partial-consume"));
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Active.ObserveAsync(f.Scope).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Publication.ObservePublicationAsync(f.Scope).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Commit(f.State(), "later"));
    }

    [Fact]
    public async Task Later_participant_failure_latches_both_active_and_publication_inspection_unavailable()
    {
        var f = await Fixture.Create(throwingScheduler: true);
        var commit = f.NewCommit(f.State(), "partial");
        var changes = new RuntimeCheckpointStateChangeSet(commit.StateChanges.WorkflowExecution,
            new("run", RuntimeStateChangeOperation.Upsert, new SchedulerState("run", 1), new Dictionary<string, string>()), [], [], [], [], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Checkpoints.CommitAsync(commit with { StateChanges = changes }, Fixture.Immediate).AsTask());
        Assert.NotNull(await f.Executions.FindAsync("run")); // Existing in-memory writer has no participant rollback.
        Assert.False(f.Checkpoints.State.Commits.ContainsKey("partial"));
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Active.ObserveAsync(f.Scope).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Publication.ObservePublicationAsync(f.Scope).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Commit(f.State(), "next"));
    }

    [Fact]
    public async Task Enrollment_rejects_existing_history_and_mixed_actual_tuple_without_projecting_it()
    {
        var f = await Fixture.Create(enroll: false); await f.Executions.SaveAsync(f.State());
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Enroll());
        Assert.Null(f.Checkpoints.State.MessageActiveInterests);
        var clean = await Fixture.Create(enroll: false);
        await Assert.ThrowsAsync<NotSupportedException>(() => InMemoryMessageActiveInterestStore.CreateAsync(clean.Checkpoints,
            new InMemoryWorkflowExecutionStateStore(), clean.Liveness, clean.References, clean.Material, clean.Access, clean.Extractor, clean.Clock).AsTask());
        Assert.Null(clean.Checkpoints.State.MessageActiveInterests);
    }

    [Theory]
    [InlineData("forwarder")]
    [InlineData("lease")]
    [InlineData("clock")]
    public async Task Matching_holder_or_interface_alone_cannot_certify_the_actual_physical_tuple(string changed)
    {
        var f = await Fixture.Create(enroll: false);
        IWorkflowExecutionStateStore selected = changed == "forwarder"
            ? new CoalescingWorkflowExecutionStateStore(new(new InMemoryWorkflowExecutionStateStore(f.Checkpoints.State, f.Liveness)), f.Sessions)
            : f.Executions;
        var lease = new WorkflowExecutableRootWriteLeaseManager(changed == "lease" ? new InMemoryWorkflowExecutableStore() : f.Material,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), f.Clock);
        var selectedClock = changed == "clock" ? new FixedClock() : f.Clock;
        var checkpoints = new InMemoryRuntimeCheckpointCommitStore(f.Material, f.References, selected,
            operationalStateStore: f.Liveness, rootWriteLeaseManager: lease, state: f.Checkpoints.State,
            timeProvider: selectedClock, persistenceAccessContextAccessor: f.Access, physicalWorkflowExecutionStore: new(f.Executions));
        await Assert.ThrowsAsync<NotSupportedException>(() => InMemoryMessageActiveInterestStore.CreateAsync(checkpoints,
            f.Executions, f.Liveness, f.References, f.Material, f.Access, f.Extractor, f.Clock).AsTask());
        Assert.Null(f.Checkpoints.State.MessageActiveInterests);
        Assert.Null(await f.Executions.FindAsync("run"));
    }

    [Theory]
    [InlineData("unfenced")]
    [InlineData("test")]
    [InlineData("worker")]
    public async Task Unsupported_admission_shapes_refuse_before_physical_state_or_interest(string shape)
    {
        var f = await Fixture.Create(); var commit = f.NewCommit(f.State(), "unsupported");
        if (shape == "unfenced") commit = commit with { ExpectedFence = null };
        else
        {
            var state = shape == "test" ? f.State() with { RunKind = WorkflowRunKind.TestRun } : f.State() with { ParentWorkflowExecutionId = "parent" };
            commit = f.NewCommit(state, "unsupported");
        }
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Checkpoints.CommitAsync(commit, Fixture.Immediate).AsTask());
        Assert.Null(await f.Executions.FindAsync("run")); Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
    }

    [Fact]
    public async Task Changed_material_incarnation_refuses_without_replacing_the_existing_root()
    {
        var f = await Fixture.Create(); await f.Commit(f.State(), "start");
        await f.Material.DeleteAsync("artifact"); await f.Material.SaveAsync(f.Executable("artifact", "Payment"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Commit(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "changed-material"));
        Assert.Equal(WorkflowExecutionStatus.Running, (await f.Executions.FindAsync("run"))!.Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Active.ObserveAsync(f.Scope).AsTask());
    }

    [Fact]
    public async Task Replay_does_not_change_incarnation_or_epoch_and_different_runs_do_not_share_incarnations()
    {
        var f = await Fixture.Create(); var commit = f.NewCommit(f.State(), "same");
        await f.Checkpoints.CommitAsync(commit, Fixture.Immediate);
        var first = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        await f.Checkpoints.CommitAsync(commit, Fixture.Immediate);
        Assert.Equal(first.Incarnation, Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots).Incarnation);
        await f.Own("second"); await f.Commit(f.State("second"), "second");
        var observed = await f.Active.ObserveAsync(f.Scope);
        Assert.Equal(2, observed.Epoch); Assert.Equal(2, observed.Roots.Select(root => root.Incarnation).Distinct().Count());
        Assert.DoesNotContain("definition", first.ToString()); Assert.DoesNotContain("run", first.ToString());
    }

    private static string[] Names((long Epoch, IReadOnlyList<InMemoryMessageActiveInterestStore.ActiveRoot> Roots) observation) =>
        observation.Roots.SelectMany(root => root.Declarations).SelectMany(item => item.MessageNames).Distinct(StringComparer.Ordinal).ToArray();

    private sealed class Fixture
    {
        internal static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        internal static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
        internal readonly TimeProvider Clock = new FixedClock();
        internal readonly Access Access = new();
        internal readonly InMemoryWorkflowExecutableStore Material = new();
        internal readonly AsyncLocalRuntimeCoalescingSessionAccessor Sessions = new();
        internal readonly InMemoryExecutionLivenessStateStore Liveness = new();
        internal readonly InMemoryWorkflowExecutionStateStore Executions;
        internal readonly IWorkflowExecutionStateStore SelectedExecutions;
        internal readonly InMemoryActivityExecutionStateStore Activities = new();
        internal readonly InMemoryDurableValueStateStore Values = new();
        internal readonly InMemoryBookmarkStateStore Bookmarks = new();
        internal readonly InMemoryActivityExecutionInspectionStore Inspections = new();
        internal readonly RuntimeInMemoryActivityExecutionHierarchyStore Hierarchy = new(new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions())));
        internal readonly InMemorySchedulerStateStore Scheduler = new();
        internal readonly InMemoryIncidentStateStore Incidents = new();
        internal readonly InMemoryWorkflowSchedulerWorkQueue Queue = new();
        internal readonly InMemoryWorkflowExecutableSourceReferenceStore References;
        internal readonly InMemoryRuntimeCheckpointCommitStore Checkpoints;
        internal readonly InMemoryMessagePublicationInterestStore Publication;
        internal readonly MessageMaterialInterestExtractor Extractor;
        internal InMemoryMessageActiveInterestStore Active = null!;
        internal readonly WorkflowExecutableSourceReference Reference;
        internal MessageScope Scope => new(new("tenant"), "tenant");

        private Fixture(bool noProvider, bool throwingScheduler, bool coalescing)
        {
            var physical = new InMemoryRuntimeCheckpointStoreState(); Executions = new(physical, Liveness);
            SelectedExecutions = coalescing
                ? new CoalescingWorkflowExecutionStateStore(new(Executions), Sessions) : Executions;
            References = new(Material, Clock); Extractor = new(noProvider ? [] : [new Provider()]);
            Checkpoints = new(Material, References, SelectedExecutions, Activities, Bookmarks, Values, Incidents,
                operationalStateStore: Liveness, schedulerStateStore: throwingScheduler ? new ThrowingScheduler() : Scheduler,
                activityExecutionInspectionWriter: Inspections, activityExecutionHierarchyWriter: Hierarchy,
                rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(Material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock),
                state: physical, timeProvider: Clock, schedulerWorkQueue: Queue, persistenceAccessContextAccessor: Access,
                physicalWorkflowExecutionStore: new(Executions));
            Publication = new(Checkpoints, new(), References, Material, Access, Extractor, Clock);
            Reference = new(WorkflowActivationReferenceIdentity.Create("activation"), "artifact", "WorkflowDefinitionVersion", "version", "1", "definition", "version", "1",
                Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: WorkflowActivationSlotIdentity.Create("definition", "default"), TenantId: "tenant");
        }

        internal static async Task<Fixture> Create(bool noProvider = false, bool throwingScheduler = false, bool enroll = true, bool coalescing = false)
        {
            var f = new Fixture(noProvider, throwingScheduler, coalescing);
            await f.Material.SaveAsync(f.Executable("artifact", "Payment")); await f.References.SaveAsync(f.Reference); await f.Own("run");
            if (enroll) await f.Enroll();
            return f;
        }
        internal InMemoryWorkflowExecutionDeletionStore Deletion() => new(Checkpoints.State, Checkpoints, Inspections, Hierarchy,
            Executions, Liveness, Activities, Inspections, Hierarchy, Bookmarks, Values, Scheduler,
            new InMemoryDurableTimerStore(), Queue, Incidents, new InMemoryWorkflowSchedulerPoisonStore(), new InMemoryWorkflowHoldStateStore());
        internal async Task Enroll() => Active = await InMemoryMessageActiveInterestStore.CreateAsync(Checkpoints, Executions,
            Liveness, References, Material, Access, Extractor, Clock);
        internal async Task Own(string id) => await Liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For(id), id,
            new("lease-" + id, id, "owner", Now, Now.AddHours(1), 1), null, null, null));
        internal WorkflowExecutable Executable(string id, string name) => new(new(id, "definition", "version", "1", "sha256:" + id),
            new ExecutableNode("receive", "receive", "Fixture.Receive", "1", "fixture", JsonSerializer.SerializeToElement(new { name }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        internal WorkflowExecutionState State(string id = "run") => new(id, Material.ReadNativeMaterialForMessageInterest("artifact")!.Identity,
            WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, "tenant", new Dictionary<string, string>())
        { RunKind = WorkflowRunKind.PublishedRun, PinnedSource = WorkflowExecutableSourceProvenance.From(Reference), Partition = new("partition"), Authority = new("runtime", "caller") };
        internal RuntimeCheckpointCommit NewCommit(WorkflowExecutionState state, string id, string name = RuntimeCheckpointNames.WorkflowStarted) => new(id,
            new(id, name, state.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
            new(new(state.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>())
        { ExpectedFence = new("lease-" + state.WorkflowExecutionId, "owner", 1) };
        internal async Task Commit(WorkflowExecutionState state, string id, string name = RuntimeCheckpointNames.WorkflowSuspended) =>
            await Checkpoints.CommitAsync(NewCommit(state, id, name), Immediate);
        internal async Task StartThroughActualHandler(bool fold = false)
        {
            var ownership = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
            var lease = (await Liveness.FindAsync("run", RuntimeExecutionOwnershipStateId.For("run")))!.ExecutionLease;
            using var current = ownership.Push(lease);
            IRuntimeCheckpointCommitStore writer = fold ? new CoalescingRuntimeCheckpointCommitStore(new(Checkpoints), Sessions) : Checkpoints;
            IRuntimeCheckpointPersistencePolicy policy = fold ? new CoalescingRuntimeCheckpointPersistencePolicy() : new ImmediateRuntimeCheckpointPersistencePolicy();
            var handler = new WorkflowCheckpointSchedulerWorkHandler(Activities,
                new RuntimeCheckpointCommitter(policy, writer, ownership, [], [], coalescingSessionAccessor: Sessions),
                null, Clock, SelectedExecutions, Material);
            var state = State();
            var payload = new RuntimeCheckpointCommandPayload(state.PinnedExecutable, RuntimeCheckpointNames.WorkflowStarted, [],
                RuntimeCheckpointCommandPayload.WorkflowStartReason, [], null,
                new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement("seed") }, null, null,
                state.RunKind, state.PinnedSource, null, null, state.TenantId, state.Partition, state.Authority);
            await handler.HandleAsync(new RuntimeSchedulerWorkItem("actual-start", "run", "command", WorkflowExecutionCommandKind.Checkpoint,
                "envelope", "idempotency", Now, Now, payload: JsonSerializer.SerializeToElement(payload)));
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Fixture.Now; }
    private sealed class Provider : IMessageMaterialInterestProvider
    {
        public string ProviderId => "fixture.native";
        public RuntimeExecutionProtocol? ModelProtocol => null;
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material) => MessageMaterialInterestResult.Recognized(
            [new("receive", [material.NativeWorkflow!.RootActivity.DescriptorPayload.GetProperty("name").GetString()!])]);
    }
    private sealed class ThrowingScheduler : ISchedulerStateStore
    {
        public ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected later participant failure.");
        public ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<SchedulerState?>(null);
        public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<SchedulerState>>([]);
    }
}
