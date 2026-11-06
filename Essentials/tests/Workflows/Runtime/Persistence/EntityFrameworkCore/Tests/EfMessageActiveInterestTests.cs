using System.Text.Json;
using System.Data.Common;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Authored SQLite physical component controls only; uncompiled and unrun.</summary>
public sealed class EfMessageActiveInterestTests
{
    [Fact]
    public async Task Real_started_handler_persists_seed_native_root_and_marker_in_one_checkpoint()
    {
        await using var f = await Fixture.Create(); await f.StartThroughHandler(false);
        Assert.NotNull(await f.Executions.FindAsync("run")); Assert.NotEmpty(await f.Context.DurableValueStates.AsNoTracking().ToArrayAsync());
        var root = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        Assert.Equal("definition", root.DefinitionId); Assert.Null(root.Owner.RootVariableFrame); Assert.Empty(root.Owner.SystemMetadata);
        Assert.Equal(1, (await f.Active.ObserveAsync(f.Scope)).Epoch); Assert.Single(await f.Context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        Assert.Throws<NotSupportedException>(() => f.Active.RequireCompleteAcceptanceCoverage());
    }
    [Fact]
    public async Task Actual_coalesced_start_overlay_folds_into_first_physical_suspended_upsert()
    {
        await using var f = await Fixture.Create(); var queue = new Elsa.Workflows.Runtime.Services.Scheduler.InMemoryWorkflowSchedulerWorkQueue();
        var session = new RuntimeCoalescingSession("run", queue, new PersistenceModeOptions(), f.Writer,
            new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false));
        using var current = f.Sessions.Push(session); await f.StartThroughHandler(true);
        Assert.Null(await f.Executions.FindAsync("run")); Assert.NotNull(await f.Selected.FindAsync("run"));
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
        var state = (await f.Selected.FindAsync("run"))! with { Status = WorkflowExecutionStatus.Suspended };
        var folded = new CoalescingRuntimeCheckpointCommitStore(new(f.Writer), f.Sessions);
        await folded.CommitAsync(f.Commit(state, "folded", RuntimeCheckpointNames.WorkflowSuspended), Fixture.Immediate);
        Assert.Equal(WorkflowExecutionStatus.Suspended, (await f.Executions.FindAsync("run"))!.Status);
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots); Assert.NotEmpty(await f.Context.DurableValueStates.AsNoTracking().ToArrayAsync());
    }
    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed, RuntimeCheckpointNames.WorkflowCompleted)]
    [InlineData(WorkflowExecutionStatus.Faulted, "failed-boundary")]
    [InlineData(WorkflowExecutionStatus.Cancelled, RuntimeCheckpointNames.ActivityCancelled)]
    [InlineData(WorkflowExecutionStatus.Cancelled, RuntimeCheckpointNames.RuntimeAlterationJob)]
    public async Task Actual_terminal_status_closes_and_retains_control_regardless_of_checkpoint_name(WorkflowExecutionStatus status, string name)
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start");
        await f.Write(f.State() with { Status = status }, "terminal", name);
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots); Assert.Equal(2, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        var row = await f.Context.MessageActiveInterestsRows.AsNoTracking().SingleAsync(); var root = MessageActiveInterestDocument.Read(row, Fixture.ScopeKey, "run");
        Assert.Equal(status, root.TerminalStatus); Assert.Equal(2, root.ClosedAtEpoch);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Write(f.State(), "reopen"));
    }
    [Fact]
    public async Task Terminal_first_write_creates_only_tombstone_and_misleading_terminal_name_does_not_close_a_live_run()
    {
        await using var f = await Fixture.Create(); await f.Write(f.State() with { Status = WorkflowExecutionStatus.Faulted }, "first-terminal");
        Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots); Assert.Equal(0, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        await f.Own("other"); await f.Write(f.State("other"), "other", RuntimeCheckpointNames.ActivityCancelled);
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }
    [Fact]
    public async Task Retired_source_before_first_fold_and_after_start_does_not_require_current_publication_liveness()
    {
        await using var f = await Fixture.Create(); await f.References.RetireAsync(f.Reference.SourceReferenceId, Fixture.Now, "unpublished");
        await f.Write(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "folded");
        var before = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        var replacement = f.Executable("replacement"); await f.Material.SaveAsync(replacement);
        await f.References.SaveAsync(f.Reference with { SourceReferenceId = "replacement", ArtifactId = replacement.Identity.ArtifactId });
        await f.Write(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "again");
        var after = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        Assert.Equal(before.Incarnation, after.Incarnation); Assert.Equal(before.MaterialIncarnation, after.MaterialIncarnation);
    }
    [Fact]
    public async Task Shared_material_uses_canonical_pinned_source_owner_and_preserves_it_immutably()
    {
        await using var f = await Fixture.Create(); var source = f.Reference with { DefinitionId = "other\ud800", DefinitionVersionId = "other-version", SlotId = WorkflowActivationSlotIdentity.Create("other\ud800", "default") };
        await f.References.SaveAsync(source with { SourceReferenceId = WorkflowActivationReferenceIdentity.Create("other"), ActivationId = "other" });
        source = source with { SourceReferenceId = WorkflowActivationReferenceIdentity.Create("other"), ActivationId = "other" };
        await f.Write(f.State() with { PinnedSource = WorkflowExecutableSourceProvenance.From(source) }, "canonical");
        Assert.Equal("definition", f.Executable("artifact").Identity.DefinitionId);
        Assert.Equal("other\ud800", Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots).DefinitionId);
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("pin")]
    [InlineData("partition")]
    public async Task Existing_control_refuses_immutable_owner_or_pin_changes(string change)
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start"); var before = await f.Snapshot();
        var state = change switch { "owner" => f.State() with { Authority = new("different", "caller") },
            "partition" => f.State() with { Partition = new("different") }, _ => f.State() with { PinnedSource = f.State().PinnedSource! with { DefinitionId = "different" } } };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Write(state, "changed")); Assert.Equal(before, await f.Snapshot());
    }
    [Theory]
    [InlineData("save")]
    [InlineData("marker")]
    [InlineData("cancel")]
    public async Task Actual_checkpoint_failure_rolls_back_canonical_root_scope_material_source_ownership_and_marker(string failure)
    {
        await using var f = await Fixture.Create(); var before = await f.Snapshot(); f.Probe.Mode = failure;
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Write(f.State(), "cancelled", token: f.Probe.Stop.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write(f.State(), "failed"));
        f.Probe.Mode = null; Assert.Equal(before, await f.Snapshot()); Assert.Empty((await f.Active.ObserveAsync(f.Scope)).Roots);
    }
    [Fact]
    public async Task Current_direct_save_and_unbound_context_cannot_bypass_persisted_profile_but_independent_copy_database_can()
    {
        await using var f = await Fixture.Create(); await Assert.ThrowsAsync<NotSupportedException>(() => f.Executions.SaveAsync(f.State()).AsTask());
        await using var other = f.Open(); var access = new Access(); var codec = Fixture.Codec();
        await Assert.ThrowsAsync<NotSupportedException>(() => new EfWorkflowExecutionStateStore(other, access, codec, f.Clock).SaveAsync(f.State()).AsTask());
        var unbound = new EfRuntimeCheckpointCommitStore(other, access, f.Clock, new WorkflowExecutableRootWriteLeaseManager(new EfWorkflowExecutableStore(other, access), Options.Create(new WorkflowExecutableGarbageCollectionOptions()), f.Clock));
        await Assert.ThrowsAsync<NotSupportedException>(() => unbound.CommitAsync(f.Commit(f.State(), "unbound"), Fixture.Immediate).AsTask());
        await using var copy = await Fixture.Create(bind: false); await copy.Executions.SaveAsync(copy.State()); Assert.NotNull(await copy.Executions.FindAsync("run"));
    }
    [Fact]
    public async Task Actual_terminal_deletion_retains_control_and_fences_recreation_while_live_delete_refuses()
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start"); await Assert.ThrowsAsync<NotSupportedException>(() => f.Executions.DeleteAsync("run").AsTask());
        await f.Write(f.State() with { Status = WorkflowExecutionStatus.Completed }, "terminal");
        var root = await f.Context.MessageActiveInterestsRows.AsNoTracking().SingleAsync();
        var candidate = (await f.Executions.ReadDeletionCandidateAsync("run"))!; Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await f.Executions.TryDeleteTerminalAsync(candidate));
        Assert.Null(await f.Executions.FindAsync("run")); Assert.Equal(root.ContentJson, (await f.Context.MessageActiveInterestsRows.AsNoTracking().SingleAsync()).ContentJson);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => f.Write(f.State(), "recreate"));
    }
    [Fact]
    public async Task Existing_supported_run_can_lazily_enroll_without_termination_but_bounded_assessment_never_certifies_coverage()
    {
        await using var f = await Fixture.Create(bind: false); await f.Executions.SaveAsync(f.State()); await f.Bind();
        var before = await f.Active.AssessCoverageAsync(1); Assert.Equal(1, before.MissingControls); Assert.False(before.CompleteCoverage);
        await f.Write(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "continued"); Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
        var after = await f.Active.AssessCoverageAsync(1); Assert.Equal(0, after.MissingControls); Assert.False(after.CompleteCoverage);
    }
    [Theory]
    [InlineData("context")]
    [InlineData("clock")]
    [InlineData("access")]
    [InlineData("lease")]
    public async Task Mixed_actual_tuple_refuses_before_installing_persisted_profile(string alias)
    {
        await using var f = await Fixture.Create(bind: false); await using var other = f.Open(); var access = alias == "access" ? new Access() : f.Access;
        var clock = alias == "clock" ? new Clock() : f.Clock;
        var material = alias == "lease" ? new EfWorkflowExecutableStore(other, f.Access) : f.Material;
        var writer = new EfRuntimeCheckpointCommitStore(f.Context, f.Access, f.Clock, new WorkflowExecutableRootWriteLeaseManager(material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), f.Clock));
        await Assert.ThrowsAsync<NotSupportedException>(() => EfMessageActiveInterestStore.BindAsync(f.Context, writer,
            alias == "context" ? new EfWorkflowExecutionStateStore(other, f.Access, Fixture.Codec(), f.Clock) : f.Executions,
            f.Liveness, f.References, f.Material, f.Publication, access, clock).AsTask());
        Assert.Empty(await f.Context.MessageActiveInterestOwners.AsNoTracking().ToArrayAsync());
    }
    [Theory]
    [InlineData("test")]
    [InlineData("child")]
    [InlineData("unfenced")]
    public async Task Unsupported_shape_refuses_all_checkpoint_effects(string shape)
    {
        await using var f = await Fixture.Create(); var commit = f.Commit(shape == "test" ? f.State() with { RunKind = WorkflowRunKind.TestRun } :
            shape == "child" ? f.State() with { ParentWorkflowExecutionId = "parent" } : f.State(), "unsupported");
        if (shape == "unfenced") commit = commit with { ExpectedFence = null };
        var before = await f.Snapshot(); await Assert.ThrowsAsync<NotSupportedException>(() => f.Writer.CommitAsync(commit, Fixture.Immediate).AsTask()); Assert.Equal(before, await f.Snapshot());
    }
    [Fact]
    public async Task Replay_keeps_incarnation_and_shared_publication_epoch_changes_independently()
    {
        await using var f = await Fixture.Create(); var commit = f.Commit(f.State(), "same"); await f.Writer.CommitAsync(commit, Fixture.Immediate);
        var root = Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots); await f.Writer.CommitAsync(commit, Fixture.Immediate);
        Assert.Equal(root.Incarnation, Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots).Incarnation);
        await f.Bindings.PrepareActivationAsync("activation", []);
        await f.Publication.PrepareAsync(f.Executable("artifact"), "activation", f.Reference.SlotId!, "default", default);
        var activated = await f.Switch.TryActivateAsync(new("definition", "default", "activation", WorkflowActivationSource.Publishing, 0, Fixture.Now)); Assert.True(activated.Succeeded);
        Assert.Equal(2, (await f.Active.ObserveAsync(f.Scope)).Epoch);
        Assert.True((await f.Switch.TryDeactivateAsync(new("definition", "default", WorkflowActivationSource.Publishing, activated.Slot.Revision, Fixture.Now), [])).Succeeded);
        Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots); Assert.Equal(3, (await f.Active.ObserveAsync(f.Scope)).Epoch);
    }
    [Fact]
    public async Task Changed_physical_material_incarnation_refuses_without_replacing_retained_root()
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start");
        Assert.True(await f.Material.DeleteAsync("artifact")); await f.Material.SaveAsync(f.Executable("artifact")); var before = await f.Snapshot();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Write(f.State() with { Status = WorkflowExecutionStatus.Suspended }, "reinserted"));
        Assert.Equal(before, await f.Snapshot()); await Assert.ThrowsAsync<InvalidDataException>(() => f.Active.ObserveAsync(f.Scope).AsTask());
    }
    [Fact]
    public async Task Scope_epoch_overflow_and_matching_fingerprint_owner_corruption_refuse()
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start");
        var scope = await f.Context.MessageInterestScopeControls.SingleAsync(); scope.Epoch = long.MaxValue;
        scope.ContentJson = RuntimeArtifactJson.Serialize(new MessageInterestControlDocument(1, f.Scope, long.MaxValue));
        await f.Context.SaveChangesAsync(); var before = await f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write(f.State() with { Status = WorkflowExecutionStatus.Completed }, "overflow")); Assert.Equal(before, await f.Snapshot());
        var row = await f.Context.MessageActiveInterestsRows.AsNoTracking().SingleAsync(); var root = MessageActiveInterestDocument.Read(row, Fixture.ScopeKey, "run");
        var changed = root with { Owner = root.Owner with { PinnedSource = root.Owner.PinnedSource! with { DefinitionId = "wrong-owner" } } };
        row.ContentJson = RuntimeArtifactJson.Serialize(changed); row.DescriptorFingerprint = MessageActiveInterestDocument.Fingerprint(changed);
        Assert.Throws<InvalidDataException>(() => MessageActiveInterestDocument.Read(row, Fixture.ScopeKey, "run"));
    }
    [Theory]
    [InlineData("checkpoint")]
    [InlineData("delete")]
    public async Task Actual_SQL_scope_write_precedes_transactional_execution_and_ownership_commands(string operation)
    {
        await using var f = await Fixture.Create();
        if (operation == "delete")
        { await f.Write(f.State(), "start"); await f.Write(f.State() with { Status = WorkflowExecutionStatus.Completed }, "terminal"); }
        f.Order.Enabled = true;
        if (operation == "delete") Assert.True(await f.Executions.DeleteAsync("run"));
        else await f.Write(f.State(), "start");
        f.Order.Enabled = false;
        var firstScopeWrite = f.Order.Events.FindIndex(item => item.InTransaction && item.Scope && item.Write);
        Assert.True(firstScopeWrite >= 0);
        Assert.DoesNotContain(f.Order.Events.Take(firstScopeWrite), item => item.InTransaction && item.Protected);
        Assert.Contains(f.Order.Events.Skip(firstScopeWrite + 1), item => item.InTransaction && item.Protected);
        if (operation == "delete") Assert.Contains(f.Order.Events, item => !item.InTransaction && item.ActiveControl);
        // These are actual SQLite command-order controls, not a claim that SQLite proves SQL Server contention.
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_liveness_DTO_cannot_mint_a_managed_deletion_tombstone(bool conditional)
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start"); var before = await f.Snapshot();
        var current = (await f.Liveness.FindAsync("run", RuntimeExecutionOwnershipStateId.For("run")))!;
        var forged = new ExecutionLivenessState(current.ExecutionLivenessStateId, current.WorkflowExecutionId, null, current.Heartbeat,
            current.Drain, current.InterruptedExecution, current.PendingPostCommitIntentIds,
            new Dictionary<string, string> { [RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone] = "true" });
        if (conditional) await Assert.ThrowsAsync<NotSupportedException>(() => f.Liveness.TrySaveAsync(forged, 2).AsTask());
        else await Assert.ThrowsAsync<NotSupportedException>(() => f.Liveness.SaveAsync(forged).AsTask());
        Assert.Equal(before, await f.Snapshot()); Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }
    [Fact]
    public async Task Discovery_hint_cannot_authorize_deletion_after_the_canonical_control_incarnation_changes()
    {
        await using var f = await Fixture.Create(); await f.Write(f.State() with { Status = WorkflowExecutionStatus.Completed }, "terminal");
        var hint = await EfMessageActiveInterestStore.ReadDeletionScopeHintAsync(f.Context, Fixture.ScopeKey, "run", default);
        var row = await f.Context.MessageActiveInterestsRows.SingleAsync(); var root = MessageActiveInterestDocument.Read(row, Fixture.ScopeKey, "run");
        var changed = root with { Incarnation = Guid.NewGuid().ToString("N") };
        row.Incarnation = changed.Incarnation; row.ContentJson = RuntimeArtifactJson.Serialize(changed); row.DescriptorFingerprint = MessageActiveInterestDocument.Fingerprint(changed);
        await f.Context.SaveChangesAsync(); var before = await f.Snapshot();
        await using (var transaction = await f.Context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => EfMessageActiveInterestStore.StageDeletionScopeAsync(f.Context, Fixture.ScopeKey, "run", hint, default).AsTask());
            await transaction.RollbackAsync(); f.Context.ChangeTracker.Clear();
        }
        Assert.Equal(before, await f.Snapshot()); Assert.NotNull(await f.Executions.FindAsync("run"));
    }
    [Fact]
    public async Task Owner_selection_refuses_string_version_in_an_otherwise_coherent_document()
    {
        await using var f = await Fixture.Create(); var row = await f.Context.MessageActiveInterestOwners.AsNoTracking().SingleAsync();
        var document = System.Text.Json.Nodes.JsonNode.Parse(row.ContentJson)!.AsObject(); document["version"] = "1";
        row.ContentJson = document.ToJsonString();
        Assert.Throws<InvalidDataException>(() => MessageActiveInterestOwnerDocument.Read(row, Fixture.ScopeKey));
    }
    [Theory]
    [InlineData("observe")]
    [InlineData("coverage")]
    [InlineData("truncated")]
    public async Task Late_scope_drift_after_actual_read_transaction_commit_cannot_return_old_scope_results(string operation)
    {
        await using var f = await Fixture.Create(); await f.Write(f.State(), "start");
        if (operation == "truncated")
        {
            var scope = new MessageScope(new(Fixture.ScopeKey));
            f.Context.MessageInterestScopeControls.Add(new() { Id = MessagePublicationInterestDocument.ScopeId(scope),
                ScopeKey = EfRelationalIdentity.Encode(Fixture.ScopeKey), ScopeKeyHash = EfRelationalIdentity.Hash(Fixture.ScopeKey),
                FullIdentity = MessagePublicationInterestDocument.ScopeIdentity(scope), Epoch = 0, Revision = 1,
                SchemaVersion = RuntimeMessageInterestEfModule.SchemaVersion, ContentJson = RuntimeArtifactJson.Serialize(new MessageInterestControlDocument(1, scope, 0)) });
            await f.Context.SaveChangesAsync(); f.Context.ChangeTracker.Clear();
        }
        f.Drift.Change = () => f.Access.Current = PersistenceAccessContext.Scoped(new("changed-scope")); f.Drift.Enabled = true;
        if (operation == "observe") await Assert.ThrowsAsync<NotSupportedException>(() => f.Active.ObserveAsync(f.Scope).AsTask());
        else await Assert.ThrowsAsync<NotSupportedException>(() => f.Active.AssessCoverageAsync(1).AsTask());
        Assert.Equal(1, f.Drift.Changes); f.Drift.Enabled = false;
        f.Access.Current = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey)); Assert.Single((await f.Active.ObserveAsync(f.Scope)).Roots);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly DateTimeOffset Now = new(2026, 11, 6, 5, 0, 0, TimeSpan.Zero);
        internal const string ScopeKey = "tenant-a";
        internal static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate, "fixture");
        private readonly TemporarySqliteDatabase database = new("message-active");
        internal readonly Probe Probe = new(); internal readonly OrderProbe Order = new(); internal readonly DriftProbe Drift = new(); internal readonly Access Access = new(); internal readonly Clock Clock = new();
        internal readonly AsyncLocalRuntimeCoalescingSessionAccessor Sessions = new();
        internal RuntimeSqliteDbContext Context = null!; internal EfWorkflowExecutableStore Material = null!;
        internal EfWorkflowExecutableSourceReferenceStore References = null!; internal EfWorkflowExecutionStateStore Executions = null!;
        internal EfExecutionLivenessStateStore Liveness = null!; internal EfRuntimeCheckpointCommitStore Writer = null!;
        internal EfMessagePublicationInterestStore Publication = null!; internal EfMessageActiveInterestStore Active = null!;
        internal EfWorkflowTriggerBindingStore Bindings = null!; internal EfWorkflowActivationSwitch Switch = null!;
        internal WorkflowExecutableSourceReference Reference = null!; internal IWorkflowExecutionStateStore Selected = null!;
        internal MessageScope Scope => new(new(ScopeKey), ScopeKey);
        internal static HmacRuntimeRecoveryContinuationCodec Codec() => new(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "message-active-fixture-key-32-characters", AllowEphemeralDevelopmentKey = false }));
        internal RuntimeSqliteDbContext Open() => new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString).AddInterceptors(Probe, Order, Drift).Options);
        internal static async Task<Fixture> Create(bool bind = true)
        {
            var f = new Fixture(); f.Context = f.Open(); await f.Context.Database.EnsureCreatedAsync(); var codec = Codec();
            f.Material = new(f.Context, f.Access); f.References = new(f.Context, f.Access, codec);
            f.Executions = new(f.Context, f.Access, codec, f.Clock); f.Liveness = new(f.Context, f.Access, codec);
            f.Selected = new CoalescingWorkflowExecutionStateStore(new(f.Executions), f.Sessions);
            f.Writer = new(f.Context, f.Access, f.Clock, new WorkflowExecutableRootWriteLeaseManager(f.Material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), f.Clock));
            var authority = new EfWorkflowActivationAuthority(f.Context, f.Access); f.Bindings = new(f.Context, f.Access);
            f.Publication = new(f.Context, authority, f.References, f.Material, f.Access, [new Provider()], f.Clock);
            f.Switch = new(f.Publication, authority, f.Bindings, f.References, f.Access, f.Clock);
            var executable = f.Executable("artifact"); await f.Material.SaveAsync(executable);
            f.Reference = new(WorkflowActivationReferenceIdentity.Create("activation"), executable.Identity.ArtifactId, "WorkflowDefinitionVersion", "version", "1", "definition", "version", "1", Now, Now,
                WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: WorkflowActivationSlotIdentity.Create("definition", "default"), TenantId: ScopeKey);
            await f.References.SaveAsync(f.Reference); await f.Own("run"); if (bind) await f.Bind(); return f;
        }
        internal async Task Bind() => Active = await EfMessageActiveInterestStore.BindAsync(Context, Writer, Executions, Liveness, References, Material, Publication, Access, Clock);
        internal async Task Own(string id) => await Liveness.SaveAsync(new(RuntimeExecutionOwnershipStateId.For(id), id, new("lease-" + id, id, "owner", Now, Now.AddHours(1), 1), null, null, null));
        internal WorkflowExecutable Executable(string id) => new(new(id, "definition", "version", "1", "sha256:" + id),
            new ExecutableNode("receive", "receive", "Fixture.Receive", "1", "fixture", JsonSerializer.SerializeToElement(new { name = "Payment" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()), new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        internal WorkflowExecutionState State(string id = "run") => new(id, Executable("artifact").Identity, WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, ScopeKey, new Dictionary<string, string>())
            { RunKind = WorkflowRunKind.PublishedRun, PinnedSource = WorkflowExecutableSourceProvenance.From(Reference), Partition = new("partition"), Authority = new("runtime", "caller") };
        internal RuntimeCheckpointCommit Commit(WorkflowExecutionState state, string id, string name = RuntimeCheckpointNames.WorkflowStarted) => new(id,
            new(id, name, state.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
            new(new(state.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>())
            { ExpectedFence = new("lease-" + state.WorkflowExecutionId, "owner", 1) };
        internal async Task Write(WorkflowExecutionState state, string id, string name = RuntimeCheckpointNames.WorkflowSuspended, CancellationToken token = default) => await Writer.CommitAsync(Commit(state, id, name), Immediate, token);
        internal async Task StartThroughHandler(bool folded)
        {
            var ownership = new AsyncLocalRuntimeExecutionOwnershipContextAccessor(); var lease = (await Liveness.FindAsync("run", RuntimeExecutionOwnershipStateId.For("run")))!.ExecutionLease;
            using var current = ownership.Push(lease);
            IRuntimeCheckpointCommitStore writer = folded ? new CoalescingRuntimeCheckpointCommitStore(new(Writer), Sessions) : Writer;
            IRuntimeCheckpointPersistencePolicy policy = folded ? new CoalescingRuntimeCheckpointPersistencePolicy() : new ImmediateRuntimeCheckpointPersistencePolicy();
            var handler = new WorkflowCheckpointSchedulerWorkHandler(new EfActivityExecutionStateStore(Context, Access, Codec()), new RuntimeCheckpointCommitter(policy, writer, ownership, [], [], coalescingSessionAccessor: Sessions), null, Clock, Selected, Material, Sessions);
            var state = State(); var payload = new RuntimeCheckpointCommandPayload(state.PinnedExecutable, RuntimeCheckpointNames.WorkflowStarted, [], RuntimeCheckpointCommandPayload.WorkflowStartReason,
                [], null, new Dictionary<string, JsonElement> { ["input"] = JsonSerializer.SerializeToElement("seed") }, null, null, state.RunKind, state.PinnedSource, null, null, state.TenantId, state.Partition, state.Authority);
            await handler.HandleAsync(new("actual-start", "run", "command", WorkflowExecutionCommandKind.Checkpoint, "envelope", "key", Now, Now, payload: JsonSerializer.SerializeToElement(payload)));
        }
        internal async Task<string> Snapshot()
        {
            Context.ChangeTracker.Clear(); return RuntimeArtifactJson.Serialize(new {
                Owners = await Context.MessageActiveInterestOwners.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(), Roots = await Context.MessageActiveInterestsRows.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Epochs = await Context.MessageInterestScopeControls.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(), Workflow = await Context.WorkflowExecutionStates.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Material = await Context.WorkflowExecutableCoordinations.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(), Sources = await Context.WorkflowExecutableSourceReferences.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Ownership = await Context.ExecutionLivenessStates.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(), Markers = await Context.RuntimeCheckpointCommits.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync() });
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await database.DisposeAsync(); Probe.Stop.Dispose(); }
    }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Fixture.Now; }
    private sealed class Provider : IMessageMaterialInterestProvider
    {
        public string ProviderId => "fixture.native"; public RuntimeExecutionProtocol? ModelProtocol => null;
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material) => MessageMaterialInterestResult.Recognized([new("receive", ["Payment"])]);
    }
    private sealed class DriftProbe : DbTransactionInterceptor
    {
        internal bool Enabled; internal Action? Change; internal int Changes;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        { if (Enabled) { Changes++; Change!(); } return Task.CompletedTask; }
    }
    private sealed class OrderProbe : DbCommandInterceptor
    {
        internal sealed record Event(bool InTransaction, bool Scope, bool Protected, bool ActiveControl, bool Write);
        internal bool Enabled; internal readonly List<Event> Events = [];
        private void Record(DbCommand command)
        {
            if (!Enabled) return;
            var sql = command.CommandText.TrimStart();
            var scope = sql.Contains(RuntimeMessageInterestEfModule.ControlTable, StringComparison.Ordinal);
            var protectedRow = sql.Contains(RuntimeWorkflowExecutionEfModule.TableName, StringComparison.Ordinal) || sql.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.Ordinal);
            var active = sql.Contains(RuntimeMessageInterestEfModule.ActiveTable, StringComparison.Ordinal);
            if (scope || protectedRow || active) Events.Add(new(command.Transaction is not null, scope, protectedRow, active,
                sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || sql.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)));
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
    }
    private sealed class Probe : SaveChangesInterceptor
    {
        internal string? Mode; internal readonly CancellationTokenSource Stop = new();
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var active = data.Context!.ChangeTracker.Entries<MessageActiveInterestEntity>().Any(e => e.State is EntityState.Added or EntityState.Modified);
            var marker = data.Context.ChangeTracker.Entries<RuntimeCheckpointCommitEntity>().Any(e => e.State == EntityState.Added);
            if (Mode == "save" && active || Mode == "marker" && marker) throw new InvalidOperationException("Injected native checkpoint failure.");
            if (Mode == "cancel" && marker) { Stop.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(result);
        }
    }
}
