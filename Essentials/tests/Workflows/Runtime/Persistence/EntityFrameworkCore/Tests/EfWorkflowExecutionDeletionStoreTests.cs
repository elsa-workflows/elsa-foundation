using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowExecutionDeletionStoreTests
{
    [Fact]
    public async Task Direct_running_delete_captures_ownership_before_final_control_dependency_read()
    {
        await using var database = await TestDatabase.CreateAsync();
        var probe = new AdvanceOwnershipAfterCapture();
        var services = Compose(database.ConnectionString);
        services.AddScoped<RuntimeDbContext>(_ => new RuntimeSqliteDbContext(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString)
                .AddInterceptors(probe).Options));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        await context.Database.EnsureCreatedAsync();
        var executions = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        await executions.SaveAsync(Execution("running-delete-fence", WorkflowExecutionStatus.Running));
        await scope.ServiceProvider.GetRequiredService<IExecutionLivenessStateStore>().SaveAsync(new ExecutionLivenessState(
            RuntimeExecutionOwnershipStateId.For("running-delete-fence"), "running-delete-fence", null, null, null, null));
        probe.Enabled = true;
        Assert.False(await executions.DeleteAsync("running-delete-fence"));
        probe.Enabled = false;
        Assert.Equal(3, probe.Advances);
        Assert.NotNull(await executions.FindAsync("running-delete-fence"));
        Assert.False(await executions.IsDeletionTombstonedAsync("running-delete-fence"));
        await context.SaveChangesAsync();
        Assert.False(await executions.IsDeletionTombstonedAsync("running-delete-fence"));
    }

    // Mechanism control: advance the persisted provider revision after the deletion stages its snapshot,
    // without changing the workflow row. This is not a concurrent-host/recovery qualification.
    private sealed class AdvanceOwnershipAfterCapture : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int Advances { get; private set; }
        private bool _ownershipRead;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Enabled || !command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return result;
            if (command.CommandText.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.Ordinal)) _ownershipRead = true;
            if (!_ownershipRead || !command.CommandText.Contains(RuntimeWorkflowDispatchEfModule.TableName, StringComparison.Ordinal)) return result;
            _ownershipRead = false;
            await using var advance = command.Connection!.CreateCommand();
            advance.Transaction = command.Transaction;
            advance.CommandText = $"UPDATE {RuntimeOperationalStateEfModule.ExecutionLivenessTableName} SET Revision = Revision + 1";
            Assert.Equal(1, await advance.ExecuteNonQueryAsync(cancellationToken));
            Advances++;
            return result;
        }
    }

    [Fact]
    public async Task Deletion_tombstone_provider_read_failure_retains_context_and_original_cause()
    {
        var cause = new SqliteException("Injected tombstone read failure.", 1);
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(new TombstoneReadFailure(cause)).Options);
        var store = new EfWorkflowExecutionStateStore(context, new FixedAccessor("scope-a"), new NoopContinuationCodec());
        var failure = await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(async () =>
            await store.IsDeletionTombstonedAsync("control-parent"));
        Assert.Same(cause, failure.InnerException);
        Assert.Contains("reading deletion tombstone for", failure.Message, StringComparison.Ordinal);
        Assert.Contains("control-parent", failure.Message, StringComparison.Ordinal);
    }

    private sealed class TombstoneReadFailure(Exception cause) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(cause);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_dependencies_retain_both_histories_until_successful_delivery_and_caller_first_deletion(bool finalFailure)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var provider = Compose(database.ConnectionString).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
        var executions = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var deletion = services.GetRequiredService<IWorkflowExecutionDeletionStore>();
        var dispatches = services.GetRequiredService<IWorkflowDispatchStore>();
        var outbox = services.GetRequiredService<EfRuntimePostCommitOutboxStore>();
        var delivery = (IRuntimePostCommitOutboxStore)outbox;
        var link = ControlLink("control-parent");
        await executions.SaveAsync(Execution("control-parent", WorkflowExecutionStatus.Completed));
        var child = Execution(link.ChildWorkflowExecutionId, WorkflowExecutionStatus.Running) with
        {
            ParentWorkflowExecutionId = "control-parent",
            SystemMetadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ChildControlDispatchId] = link.DispatchId }
        };
        await executions.SaveAsync(child);
        // A child hint without its dispatch authority must retain the caller too.
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained,
            await deletion.TryDeleteTerminalAsync((await deletion.ReadAsync("control-parent"))!));
        await dispatches.SaveAsync(link);
        link = link.TransitionTo(WorkflowDispatchStatus.Started, Now);
        await dispatches.SaveAsync(link);
        var inconsistent = child with { SystemMetadata = new Dictionary<string, string>
            { [RuntimeMetadataKeys.ChildControlDispatchId] = "foreign-dispatch" } };
        await executions.SaveAsync(inconsistent);
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained,
            await deletion.TryDeleteTerminalAsync((await deletion.ReadAsync("control-parent"))!));
        await executions.SaveAsync(child);
        var caller = (await deletion.ReadAsync("control-parent"))!;
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained, await deletion.TryDeleteTerminalAsync(caller));
        child = child with { Status = WorkflowExecutionStatus.Completed, CompletedAt = Now };
        await executions.SaveAsync(child);
        await dispatches.SaveAsync(link.TransitionTo(WorkflowDispatchStatus.Completed, Now));
        var called = (await deletion.ReadAsync(link.ChildWorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained, await deletion.TryDeleteTerminalAsync(called));
        Assert.False(await executions.DeleteAsync(link.ChildWorkflowExecutionId));
        var intent = new RuntimePostCommitIntent("control-source", link.ChildWorkflowExecutionId, "test.control", Now,
            "root", "control-delivery", null, new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = link.DispatchId });
        await outbox.SavePendingAsync(new RuntimePostCommitOutboxItem("control-source-row", intent,
            RuntimePostCommitOutboxStatus.Pending, Now, Now));
        if (finalFailure)
            await delivery.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("control-source-row",
                RuntimePostCommitOutboxStatus.FailedFinal, Now, failureMessage: "Unresolved delivery."));
        Assert.Equal(WorkflowExecutionDeletionResult.DependencyRetained, await deletion.TryDeleteTerminalAsync(caller));
        await services.GetRequiredService<RuntimeDbContext>().SaveChangesAsync();
        Assert.NotNull(await executions.FindAsync("control-parent"));
        Assert.NotNull(await executions.FindAsync(link.ChildWorkflowExecutionId));
        Assert.False(await executions.IsDeletionTombstonedAsync("control-parent"));
        if (finalFailure) return; // A terminal failure is not a successful acknowledgement.
        await delivery.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("control-source-row",
            RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(caller));
        Assert.True(await executions.IsDeletionTombstonedAsync("control-parent"));
        Assert.False(await executions.IsDeletionTombstonedAsync("unknown-parent"));
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(called));
    }

    private static WorkflowDispatchRecord ControlLink(string parent)
    {
        var identity = new WorkflowDispatchIdentity(parent, "call");
        return new WorkflowDispatchRecord(identity.DispatchId, parent, "call", identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1", "hash-child"),
            new WorkflowExecutableSourceProvenance("source", "WorkflowDefinitionVersion", "version-child", "1",
                "definition-child", "version-child", "1", "publication", "slot"),
            WorkflowDispatchMode.WaitForCompletion, WorkflowDispatchStatus.Pending, null, null,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue), WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "test"), [], Now, Now,
            new Dictionary<string, string> { [RuntimeMetadataKeys.ChildControlConsumerKey] = "test.control",
                [RuntimeMetadataKeys.ChildControlConsumerVersion] = "1" });
    }

    private static readonly DateTimeOffset Now = new(2034, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public void Deletion_capability_is_registered_only_for_the_complete_EF_aggregate_and_fails_closed_when_mixed()
    {
        var services = Compose("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);

        var partial = new ServiceCollection().AddWorkflowRuntime();
        partial.AddRuntimeWorkflowExecutionEntityFrameworkCore(new RuntimeWorkflowExecutionEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "ef-run-deletion-recovery-signing-key-32-bytes"
        });
        using var partialProvider = partial.BuildServiceProvider();
        using var partialScope = partialProvider.CreateScope();
        Assert.False(partialScope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().IsSupported);

        var mixedServices = Compose("Data Source=:memory:");
        mixedServices.AddScoped<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>();
        using var mixedProvider = mixedServices.BuildServiceProvider();
        using var mixedScope = mixedProvider.CreateScope();
        Assert.False(mixedScope.ServiceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().IsSupported);
    }

    [Fact]
    public async Task Terminal_delete_removes_only_the_selected_run_graph_and_retains_shared_and_replay_records()
    {
        await using var database = await TestDatabase.CreateAsync();
        var services = Compose(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var context = serviceProvider.GetRequiredService<RuntimeDbContext>();
        await context.Database.EnsureCreatedAsync();

        var deletion = serviceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletion.IsSupported);
        await SeedRunAsync(serviceProvider, context, "run-delete", fullGraph: true);
        await SeedRunAsync(serviceProvider, context, "run-keep", fullGraph: false);
        await serviceProvider.GetRequiredService<IWorkflowHoldStateStore>().SaveAsync(new WorkflowHoldState(
            "global-holds",
            activeHolds:
            [
                new WorkflowHold("hold-deleted-run", WorkflowHoldScope.WorkflowExecution, WorkflowHoldStatus.Active, Now, "test", "deleted run", workflowExecutionId: "run-delete"),
                new WorkflowHold("hold-kept-run", WorkflowHoldScope.WorkflowExecution, WorkflowHoldStatus.Active, Now, "test", "kept run", workflowExecutionId: "run-keep")
            ]));

        await using (var otherScopeContext = database.Open())
            await SeedRunAsync(otherScopeContext, new FixedAccessor("other-scope"), "run-delete", fullGraph: false);

        var checkpointId = CompositeId("default", "run-delete", "commit-delete");
        context.RuntimeCheckpointCommits.Add(new RuntimeCheckpointCommitEntity
        {
            Id = checkpointId,
            ScopeKey = Encode("default"),
            ScopeKeyHash = Hash("default"),
            CommitId = Encode("commit-delete"),
            CommitIdHash = Hash("commit-delete"),
            WorkflowExecutionId = Encode("run-delete"),
            WorkflowExecutionIdHash = Hash("run-delete"),
            WorkflowExecutionIdOrderKey = Order("run-delete"),
            OccurredAtUtcTicks = Now.UtcTicks,
            Fingerprint = "fingerprint",
            ContentJson = "{}",
            PendingPostCommitWorkIdsJson = "[]",
            ConsumedSchedulerWorkItemIdsJson = "[]",
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = 1
        });
        var intent = new RuntimePostCommitIntent("intent-delete", "run-delete", "test", Now, null, null,
            JsonDocument.Parse("{}").RootElement);
        await serviceProvider.GetRequiredService<EfRuntimePostCommitOutboxStore>().SavePendingAsync(
            new RuntimePostCommitOutboxItem("outbox-delete", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now));
        context.WorkflowExecutables.Add(new WorkflowExecutableEntity
        {
            Id = "shared-artifact-row",
            ScopeKey = Encode("default"),
            ScopeKeyHash = Hash("default"),
            ArtifactId = Encode("artifact-shared"),
            ArtifactIdHash = Hash("artifact-shared"),
            ArtifactHash = "artifact-hash",
            ArtifactIdOrderKey = Order("artifact-shared"),
            ContentJson = "{}",
            SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
            IncarnationId = "incarnation-shared"
        });
        await context.SaveChangesAsync();

        var candidate = await deletion.ReadAsync("run-delete");
        Assert.NotNull(candidate);
        Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletion.TryDeleteTerminalAsync(candidate!));

        context.ChangeTracker.Clear();
        Assert.Null(await serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run-delete"));
        Assert.NotNull(await serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run-keep"));
        Assert.NotNull(await new EfWorkflowExecutionStateStore(context, new FixedAccessor("other-scope"), new NoopContinuationCodec(), new FixedTimeProvider(Now))
            .FindAsync("run-delete"));

        var scopeKey = Encode("default");
        var runKey = Encode("run-delete");
        Assert.Equal(0, await context.ActivityExecutionStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.ActivityExecutionInspections.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.ActivityExecutionHierarchies.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.Bookmarks.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.DurableValueStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.SchedulerStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.DurableTimers.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.SchedulerWorkItems.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.IncidentStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.WorkflowRunHealthStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.WorkflowSchedulerPoisonRecords.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));
        Assert.Equal(0, await context.WorkflowHoldStates.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));

        Assert.Equal(1, await context.RuntimeCheckpointCommits.CountAsync(row => row.Id == checkpointId));
        Assert.Equal(1, await context.RuntimePostCommitOutbox.CountAsync(row => row.WorkflowExecutionId == runKey));
        Assert.Equal(1, await context.WorkflowExecutables.CountAsync(row => row.ArtifactId == Encode("artifact-shared")));
        var globalHolds = await serviceProvider.GetRequiredService<IWorkflowHoldStateStore>().FindAsync("global-holds");
        Assert.NotNull(globalHolds);
        Assert.Equal(new[] { "hold-kept-run" }, globalHolds!.ActiveHolds.Select(hold => hold.HoldId));
        var ownership = await serviceProvider.GetRequiredService<IExecutionLivenessStateStore>()
            .FindAsync("run-delete", RuntimeExecutionOwnershipStateId.For("run-delete"));
        Assert.Equal("true", ownership!.Metadata[Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);

        var lateWork = new RuntimeSchedulerWorkItem(
            "late-work-after-delete", "run-delete", "command", WorkflowExecutionCommandKind.ScheduleActivity,
            "late-envelope", "late-idempotency", Now, Now);
        Assert.Same(lateWork, await serviceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(lateWork));
        Assert.Equal(0, await context.SchedulerWorkItems.CountAsync(row => row.ScopeKey == scopeKey && row.WorkflowExecutionId == runKey));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
                .SaveAsync(Execution("run-delete", WorkflowExecutionStatus.Completed)).AsTask());
        var livenessStore = serviceProvider.GetRequiredService<IExecutionLivenessStateStore>();
        var livenessRevision = await livenessStore.FindVersionedAsync("run-delete", RuntimeExecutionOwnershipStateId.For("run-delete"));
        Assert.NotNull(livenessRevision);
        var replacementOwnership = new ExecutionLivenessState(
            RuntimeExecutionOwnershipStateId.For("run-delete"), "run-delete", null, null, null, null,
            metadata: new Dictionary<string, string> { ["owner"] = "replacement" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => livenessStore.SaveAsync(replacementOwnership).AsTask());
        var livenessWrite = await livenessStore.TrySaveAsync(replacementOwnership, livenessRevision!.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, livenessWrite.Status);
    }

    [Fact]
    public async Task Changed_snapshot_and_nonterminal_candidate_leave_execution_untouched()
    {
        await using var database = await TestDatabase.CreateAsync();
        var services = Compose(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        await serviceProvider.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
        var executions = serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        var deletion = serviceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>();

        await executions.SaveAsync(Execution("run-changed", WorkflowExecutionStatus.Completed));
        var stale = (await deletion.ReadAsync("run-changed"))!;
        await executions.SaveAsync(Execution("run-changed", WorkflowExecutionStatus.Completed) with { UpdatedAt = Now.AddSeconds(1) });
        Assert.Equal(WorkflowExecutionDeletionResult.Changed, await deletion.TryDeleteTerminalAsync(stale));
        Assert.NotNull(await executions.FindAsync("run-changed"));

        await executions.SaveAsync(Execution("run-running", WorkflowExecutionStatus.Running));
        var active = (await deletion.ReadAsync("run-running"))!;
        Assert.Equal(WorkflowExecutionDeletionResult.NotTerminal, await deletion.TryDeleteTerminalAsync(active));
        Assert.NotNull(await executions.FindAsync("run-running"));
    }

    [Fact]
    public async Task Cleanup_failure_rolls_back_fence_and_prior_sidecar_deletes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var services = Compose(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var context = serviceProvider.GetRequiredService<RuntimeDbContext>();
        await context.Database.EnsureCreatedAsync();
        await SeedRunAsync(serviceProvider, context, "run-rollback", fullGraph: true);
        await context.Database.ExecuteSqlRawAsync($"CREATE TRIGGER fail_incident_delete BEFORE DELETE ON {RuntimeOperationalStateEfModule.IncidentTableName} BEGIN SELECT RAISE(ABORT, 'expected cleanup failure'); END;");

        var candidate = await serviceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().ReadAsync("run-rollback");
        await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(() =>
            serviceProvider.GetRequiredService<IWorkflowExecutionDeletionStore>().TryDeleteTerminalAsync(candidate!).AsTask());

        context.ChangeTracker.Clear();
        Assert.NotNull(await serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run-rollback"));
        Assert.Equal(1, await context.ActivityExecutionStates.CountAsync(row => row.WorkflowExecutionId == Encode("run-rollback")));
        Assert.Equal(1, await context.IncidentStates.CountAsync(row => row.WorkflowExecutionId == Encode("run-rollback")));
        Assert.Empty(await context.ExecutionLivenessStates.ToArrayAsync());
    }

    private static IServiceCollection Compose(string connectionString) => new ServiceCollection()
        .AddWorkflowRuntime()
        .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = "ef-run-deletion-recovery-signing-key-32-bytes",
            HierarchyCursorSigningKey = "ef-run-deletion-hierarchy-signing-key-32-bytes"
        });

    private static async Task SeedRunAsync(IServiceProvider provider, RuntimeDbContext context, string id, bool fullGraph)
    {
        var executionStore = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        await executionStore.SaveAsync(Execution(id, WorkflowExecutionStatus.Completed));
        if (!fullGraph)
            return;

        var activity = Activity(id);
        var inspection = ActivityExecutionInspectionProjection.FromState(activity, "checkpoint", Now);
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(activity);
        await provider.GetRequiredService<IActivityExecutionInspectionWriter>().SaveAsync(inspection);
        await provider.GetRequiredService<IActivityExecutionHierarchyStore>().SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(inspection));
        await provider.GetRequiredService<IBookmarkStateStore>().SaveAsync(new BookmarkState(
            $"bookmark-{id}", id, "activity", "node", "resume", "test", "stimulus", null,
            new Dictionary<string, string>(), Now, null));
        await provider.GetRequiredService<IDurableValueStateStore>().SaveAsync(new DurableValueState(
            $"value-{id}", id, "input", new RuntimeValueTypeDescriptor("string", null, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            JsonDocument.Parse("\"value\"").RootElement, null, null, Now, new Dictionary<string, string>()));
        await provider.GetRequiredService<ISchedulerStateStore>().SaveAsync(new SchedulerState(id, 1));
        await provider.GetRequiredService<IDurableTimerStore>().SaveAsync(new DurableTimer(
            $"timer-{id}", id, "Delay", "stimulus", Now, Now));
        await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(new RuntimeSchedulerWorkItem(
            $"work-{id}", id, "command", WorkflowExecutionCommandKind.ScheduleActivity, "envelope", "idempotency", Now, Now));
        await provider.GetRequiredService<IIncidentStateStore>().SaveAsync(new IncidentState(
            $"incident-{id}", id, "activity", "node", IncidentSeverity.Error, IncidentStatus.Open, null,
            "test-failure", "test incident", Now, null));
        await provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().RecordAsync(new RuntimeSchedulerPoisonRecord(
            id, $"poison-{id}", WorkflowExecutionCommandKind.ScheduleActivity, "handler",
            new RuntimeFaultInfo("System.InvalidOperationException", "test fault"), 1,
            RuntimeSchedulerPoisonDisposition.Poisoned, Now, Now));
        await provider.GetRequiredService<IWorkflowHoldStateStore>().SaveAsync(new WorkflowHoldState($"hold-{id}", id));

        var scope = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope().Value;
        context.WorkflowRunHealthStates.Add(new WorkflowRunHealthStateEntity
        {
            Id = CompositeId(scope, id),
            ScopeKey = Encode(scope),
            ScopeKeyHash = Hash(scope),
            WorkflowExecutionId = Encode(id),
            WorkflowExecutionIdHash = Hash(id),
            WorkflowExecutionIdOrderKey = Order(id),
            DefinitionId = Encode("definition"),
            DefinitionIdHash = Hash("definition"),
            DefinitionIdOrderKey = Order("definition"),
            RunKind = (int)WorkflowRunKind.Unknown,
            StartedAtUtcTicks = Now.UtcTicks,
            StartedAtOffsetMinutes = 0,
            Status = (int)WorkflowExecutionStatus.Completed,
            IncidentCount = 0,
            IncidentBearingCount = 0,
            ContentJson = "{}",
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = 1
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedRunAsync(RuntimeDbContext context, IPersistenceAccessContextAccessor accessor, string id, bool fullGraph)
    {
        var executionStore = new EfWorkflowExecutionStateStore(context, accessor, new NoopContinuationCodec(), new FixedTimeProvider(Now));
        await executionStore.SaveAsync(Execution(id, WorkflowExecutionStatus.Completed));
        if (fullGraph)
            await new EfActivityExecutionStateStore(context, accessor, RecoveryCodec()).SaveAsync(Activity(id));
    }

    private static WorkflowExecutionState Execution(string id, WorkflowExecutionStatus status) => new(
        id, new WorkflowExecutableIdentity("artifact-shared", "definition", "version", "1", "hash"), status,
        null, Now, Now, Now, status.IsTerminal() ? Now : null, null, null, null, new Dictionary<string, string>());

    private static ActivityExecutionState Activity(string workflowId) => new ActivityExecutionState(
        new ActivityExecution("activity", workflowId, "node", "authored", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed, null, Now, Now, Now, null, null, null, null, null, [], [], 0, 0,
        new Dictionary<string, string>()) with { ExecutionScopeId = "activity-scope" };

    private static HmacRuntimeRecoveryContinuationCodec RecoveryCodec() => new(
        Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = "ef-run-deletion-recovery-signing-key-32-bytes"
        }));

    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));
    private static string CompositeId(params string[] values) => EfRelationalIdentity.HashLengthFramed(values);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString => connectionString;

        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:workflow-deletion-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public RuntimeSqliteDbContext Open() => new(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString).Options);

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
}
