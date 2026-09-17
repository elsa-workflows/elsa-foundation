using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Shared vocabulary for the checkpoint commit contract: the stores it runs against and the model builders its cases use.
/// Adding a store means adding it to <see cref="Stores"/> and to <see cref="RuntimeCheckpointCommitContractBackend"/>.
/// </summary>
internal static class RuntimeCheckpointCommitContract
{
    public const string InMemory = "in-memory";
    public const string EntityFramework = "entity-framework";
    public const string Tenant = "tenant-a";
    public const string WorkflowId = "workflow-a";
    public const string OtherWorkflowId = "workflow-other";
    public const string DispatchActivityId = "activity-dispatch";
    public static readonly DateTimeOffset OccurredAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    public static readonly string[] Stores = [InMemory, EntityFramework];

    public static TheoryData<string> StoreData() => new(Stores);

    public static TheoryData<string, string> StoreCaseData(IEnumerable<string> caseNames)
    {
        var data = new TheoryData<string, string>();
        foreach (var store in Stores)
        foreach (var name in caseNames)
            data.Add(store, name);
        return data;
    }

    public static RuntimeCheckpointPersistenceDecision Immediate() => new(RuntimeCheckpointPersistenceMode.Immediate);

    public static RuntimeCheckpointCommit Commit(
        string workflowExecutionId,
        RuntimeCheckpointStateChangeSet? stateChanges = null,
        IReadOnlyList<RuntimePostCommitIntent>? intents = null,
        string commitId = "commit-contract") => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "ContractCheckpoint", workflowExecutionId, OccurredAt, [], new Dictionary<string, string>()),
        stateChanges ?? new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        intents ?? [],
        new Dictionary<string, string>());

    public static RuntimeStateChange<TState> Change<TState>(
        string stateId,
        TState state,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert) =>
        new(stateId, operation, state, new Dictionary<string, string>());

    public static RuntimeStateChange<WorkflowDispatchRecord> DispatchChange(WorkflowDispatchRecord record) =>
        Change(record.DispatchId, record);

    public static RuntimeStateChange<RuntimePostCommitOutboxItem> OutboxChange(
        string outboxItemId,
        RuntimePostCommitIntent intent,
        RuntimePostCommitOutboxStatus status = RuntimePostCommitOutboxStatus.Pending) =>
        Change(outboxItemId, new RuntimePostCommitOutboxItem(
            outboxItemId,
            intent,
            status,
            OccurredAt,
            OccurredAt,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? OccurredAt : null));

    public static RuntimePostCommitIntent Intent(string intentId, string workflowExecutionId = WorkflowId) =>
        new(intentId, workflowExecutionId, "test.intent", OccurredAt, null, null, null);

    public static WorkflowExecutionState Execution(string workflowExecutionId) => new(
        workflowExecutionId,
        new WorkflowExecutableIdentity($"artifact-{workflowExecutionId}", $"definition-{workflowExecutionId}", "version-1", "1.0.0", "hash-1"),
        WorkflowExecutionStatus.Running,
        null,
        OccurredAt,
        OccurredAt,
        OccurredAt,
        null,
        null,
        null,
        Tenant,
        new Dictionary<string, string>());

    public static ActivitySchedulingProvenance Provenance(
        string? executionScopeId,
        ActivityExecutionAttemptLineage? attempt = null,
        string workflowExecutionId = WorkflowId) =>
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, executionScopeId, "checkpoint", attempt: attempt);

    public static ActivityExecutionState Activity(string id, string workflowExecutionId = WorkflowId) => new(
        new ActivityExecution(id, workflowExecutionId, $"node-{id}", $"authored-{id}", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed, null, 1,
        OccurredAt, OccurredAt, OccurredAt, null, null, null, null,
        Provenance(null, workflowExecutionId: workflowExecutionId),
        null, [], [], 0, 0, new Dictionary<string, string>());

    public static ActivityExecutionInspectionProjection Inspection(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, $"node-{id}", $"authored-{id}", "Test.Activity", "1",
        ActivityExecutionStatus.Completed, null, 1, OccurredAt, OccurredAt, OccurredAt,
        "checkpoint-contract", "checkpoint-contract", OccurredAt,
        Provenance("scope-inspected", workflowExecutionId: workflowExecutionId),
        ["Done"], [], [], [], new Dictionary<string, string>(), "scope-inspected");

    public static IncidentState Incident(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, null, null,
        IncidentSeverity.Error, IncidentStatus.Open, null, "test-failure", "contract incident",
        OccurredAt, null);

    public static IncidentState ResolvedIncident(string id, string actionKind) => new(
        id, WorkflowId, null, null,
        IncidentSeverity.Error, IncidentStatus.Resolved,
        new IncidentResolutionOutcome(actionKind, OccurredAt.AddMinutes(1), strategy: null, systemSource: "contract"),
        "test-failure", "contract incident", OccurredAt, OccurredAt.AddMinutes(1));

    public static DurableValueState DurableValue(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, id, new RuntimeValueTypeDescriptor("json", null, null),
        DurableValueLifecycle.Result, DurableValueStorage.Inline,
        JsonSerializer.SerializeToElement("value"),
        null, null, OccurredAt, new Dictionary<string, string>());

    public static BookmarkState Bookmark(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, "activity-a", "node-activity-a", "resume-a", "stimulus", "stimulus-hash",
        JsonSerializer.SerializeToElement("payload"),
        new Dictionary<string, string>(), OccurredAt, OccurredAt.AddHours(1));

    public static ExecutionLivenessState Liveness(string id, string workflowExecutionId = WorkflowId) =>
        new(id, workflowExecutionId, null, null, null, null);

    public static WorkflowTestScope TestScope(string scopeId, DateTimeOffset? expiresAt = null) =>
        new(scopeId, expiresAt ?? OccurredAt.AddHours(1), Tenant, new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));

    /// <summary>A new test-run execution in <paramref name="scope"/>; a child when <paramref name="parentWorkflowExecutionId"/> is set.</summary>
    public static WorkflowExecutionState TestRunExecution(string workflowExecutionId, WorkflowTestScope scope, string? parentWorkflowExecutionId = null) =>
        Execution(workflowExecutionId) with
        {
            ParentWorkflowExecutionId = parentWorkflowExecutionId,
            RunKind = WorkflowRunKind.TestRun,
            Partition = scope.Partition,
            TestScope = scope
        };

    public static WorkflowDispatchRecord PendingDispatch(
        string parent,
        string activity,
        DateTimeOffset? updatedAt = null,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget,
        WorkflowTestScope? testScope = null)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1", "hash-child"),
            new WorkflowExecutableSourceProvenance("source-child", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            Tenant,
            testScope?.Partition ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            testScope is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            OccurredAt,
            updatedAt ?? OccurredAt,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" },
            testScope: testScope);
    }

    public static WorkflowDispatchCancellationRequest CancellationRequest(string parent, string activity) => new(
        new WorkflowDispatchIdentity(parent, activity).DispatchId,
        parent,
        activity,
        new WorkflowDispatchIdentity(parent, activity).ChildWorkflowExecutionId,
        OccurredAt);

    public static RuntimeSchedulerWorkItem SchedulerWork(string id) => new(
        id, WorkflowId, "command", WorkflowExecutionCommandKind.ScheduleActivity,
        "envelope", $"enqueue-{id}", OccurredAt, OccurredAt, 1);
}

/// <summary>A commit that is valid on every store, carrying one change of each kind the structural rules inspect.</summary>
internal sealed class CommitParts
{
    public RuntimeStateChange<WorkflowExecutionState>? WorkflowExecution { get; set; } =
        Change(WorkflowId, Execution(WorkflowId));
    public RuntimeStateChange<SchedulerState>? Scheduler { get; set; } =
        Change(WorkflowId, new SchedulerState(WorkflowId, 1));
    public List<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions { get; } =
        [Change("activity-a", Activity("activity-a"))];
    public List<RuntimeStateChange<ActivityExecutionInspectionProjection>> Inspections { get; } =
        [Change("activity-a", Inspection("activity-a"))];
    public List<RuntimeStateChange<IncidentState>> Incidents { get; } =
        [Change("incident-a", Incident("incident-a"), RuntimeStateChangeOperation.Append)];
    public List<RuntimeStateChange<DurableValueState>> DurableValues { get; } =
        [Change("value-a", DurableValue("value-a"))];
    public List<RuntimeStateChange<BookmarkState>> Bookmarks { get; } =
        [Change("bookmark-a", Bookmark("bookmark-a"))];
    public List<RuntimeStateChange<ExecutionLivenessState>> Operational { get; } =
        [Change("operational-a", Liveness("operational-a"))];
    public List<RuntimeStateChange<WorkflowDispatchRecord>> Dispatches { get; } =
        [DispatchChange(PendingDispatch(WorkflowId, DispatchActivityId))];
    public List<RuntimeStateChange<RuntimePostCommitOutboxItem>> PostCommitOutbox { get; } = [];
    public List<ActivityScopeCleanupRequest> Cleanups { get; } =
        [new(WorkflowId, "scope-a", ["scope-a"], ["bookmark-cleanup"], ["timer-cleanup"], ["work-cleanup"])];
    public List<WorkflowDispatchCancellationRequest> Cancellations { get; } = [];
    public List<ConsumedSchedulerWorkItem> Consumed { get; } = [];
    public WorkflowAlterationJobTerminalChange? AlterationTerminal { get; set; }
    public List<RuntimePostCommitIntent> Intents { get; } = [Intent("intent-a")];

    public void UseOutbox(params RuntimeStateChange<RuntimePostCommitOutboxItem>[] changes)
    {
        Intents.Clear();
        PostCommitOutbox.AddRange(changes);
    }

    public RuntimeCheckpointCommit Build() => Commit(
        WorkflowId,
        new RuntimeCheckpointStateChangeSet(
            WorkflowExecution,
            Scheduler,
            ActivityExecutions,
            Bookmarks,
            DurableValues,
            Incidents,
            Operational,
            Dispatches,
            Inspections,
            PostCommitOutbox,
            Cleanups,
            Cancellations,
            Consumed,
            AlterationTerminal),
        Intents);
}

/// <summary>
/// One checkpoint store behind the application-layer committer, with the backing stores its stateful rules read. The
/// store is wrapped so a case can prove whether the committer reached it at all.
/// </summary>
internal sealed class RuntimeCheckpointCommitContractBackend : IAsyncDisposable
{
    private readonly Func<Task<int>> _countMarkers;
    private readonly Func<RuntimePostCommitOutboxItem, Task> _seedOutbox;
    private readonly IAsyncDisposable? _resources;

    private RuntimeCheckpointCommitContractBackend(
        IRuntimeCheckpointCommitStore store,
        Func<Task<int>> countMarkers,
        Func<RuntimePostCommitOutboxItem, Task> seedOutbox,
        IPostCommitOutboxLookupStore outbox,
        IIncidentStateStore incidents,
        IWorkflowDispatchStore dispatches,
        IWorkflowAlterationStore alterations,
        IExecutionLivenessStateStore liveness,
        IWorkflowSchedulerWorkQueue queue,
        IWorkflowTestScopeStore scopes,
        IWorkflowDispatchAdmissionStore admissions,
        IWorkflowExecutionStateStore executions,
        IWorkflowTestScopeCleanupStore cleanup,
        IRuntimePostCommitOutboxStore delivery,
        IRuntimePostCommitOutboxClaimStore claims,
        IAsyncDisposable? resources = null)
    {
        Store = new CallCountingStore(store);
        Committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            Store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            [],
            []);
        Ownership = new RuntimeExecutionOwnershipService(
            liveness,
            Clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-contract", LeaseDuration = TimeSpan.FromMinutes(5) });
        _countMarkers = countMarkers;
        _seedOutbox = seedOutbox;
        Outbox = outbox;
        Incidents = incidents;
        Dispatches = dispatches;
        Alterations = alterations;
        Liveness = liveness;
        Queue = queue;
        Scopes = scopes;
        Admissions = admissions;
        Executions = executions;
        Cleanup = cleanup;
        Delivery = delivery;
        Claims = claims;
        _resources = resources;
    }

    public const string AlterationPlanId = "plan-contract";

    public static TimeProvider Clock { get; } = new FixedTimeProvider(OccurredAt);

    public CallCountingStore Store { get; }
    public RuntimeCheckpointCommitter Committer { get; }
    public RuntimeExecutionOwnershipService Ownership { get; }
    public IPostCommitOutboxLookupStore Outbox { get; }
    public IIncidentStateStore Incidents { get; }
    public IWorkflowDispatchStore Dispatches { get; }
    public IWorkflowAlterationStore Alterations { get; }
    public IExecutionLivenessStateStore Liveness { get; }
    public IWorkflowSchedulerWorkQueue Queue { get; }
    public IWorkflowTestScopeStore Scopes { get; }
    public IWorkflowDispatchAdmissionStore Admissions { get; }
    public IWorkflowExecutionStateStore Executions { get; }
    public IWorkflowTestScopeCleanupStore Cleanup { get; }
    public IRuntimePostCommitOutboxStore Delivery { get; }
    public IRuntimePostCommitOutboxClaimStore Claims { get; }

    public Task<int> CountMarkersAsync() => _countMarkers();

    public Task SeedPendingOutboxItemAsync(RuntimePostCommitOutboxItem item) => _seedOutbox(item);

    /// <summary>Admits, captures, seals, and claims one alteration job for <paramref name="workflowExecutionId"/>.</summary>
    public async Task<WorkflowAlterationJobState> ClaimAlterationJobAsync(string workflowExecutionId)
    {
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            AlterationPlanId,
            new(Tenant, "system", "root"),
            new("subject", "correlation"),
            "idempotency-contract",
            "canonical-contract",
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds([workflowExecutionId]),
            OccurredAt);
        await Alterations.AdmitAsync(plan);
        await Alterations.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget(workflowExecutionId, Tenant)], null);
        await Alterations.SealAsync(plan.PlanId, 1, OccurredAt.AddSeconds(1));
        return (await Alterations.ClaimNextAsync(plan.PlanId, "worker", OccurredAt.AddSeconds(2), TimeSpan.FromMinutes(5)))!;
    }

    public static async Task<RuntimeCheckpointCommitContractBackend> CreateAsync(string store) => store switch
    {
        InMemory => CreateInMemory(),
        EntityFramework => await CreateEntityFrameworkAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(store), store, null)
    };

    private static RuntimeCheckpointCommitContractBackend CreateInMemory()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var bookmarks = new InMemoryBookmarkStateStore();
        var incidents = new InMemoryIncidentStateStore();
        var liveness = new InMemoryExecutionLivenessStateStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var dispatches = new InMemoryWorkflowDispatchStore(state);
        var alterations = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        var scopes = new InMemoryWorkflowTestScopeStore(state);
        var store = new InMemoryRuntimeCheckpointCommitStore(
            executions,
            new InMemoryActivityExecutionStateStore(),
            bookmarks,
            new InMemoryDurableValueStateStore(),
            incidents,
            liveness,
            new InMemorySchedulerStateStore(),
            new InMemoryActivityExecutionInspectionStore(),
            new PassThroughRootWriteLeaseManager(),
            state,
            Clock,
            new ActivityScopeCleanupStore(bookmarks, new InMemoryDurableTimerStore(), queue),
            workflowDispatchStore: dispatches,
            schedulerWorkQueue: queue,
            alterationStore: alterations);
        return new RuntimeCheckpointCommitContractBackend(
            store,
            () => Task.FromResult(store.ListCommits().Count),
            item => store.AddPendingForTestingAsync(item).AsTask(),
            store,
            incidents,
            dispatches,
            alterations,
            liveness,
            queue,
            scopes,
            dispatches,
            executions,
            scopes,
            store,
            store);
    }

    private static async Task<RuntimeCheckpointCommitContractBackend> CreateEntityFrameworkAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new BookmarkStateSqliteDbContext(
            new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var access = new FixedAccessor(Tenant);
        var codec = new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "runtime-checkpoint-contract-signing-key-32" }));
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var store = new EfRuntimeCheckpointCommitStore(context, access, Clock, new PassThroughRootWriteLeaseManager());
        return new RuntimeCheckpointCommitContractBackend(
            store,
            () => context.RuntimeCheckpointCommits.AsNoTracking().CountAsync(),
            item => outbox.SavePendingAsync(item).AsTask(),
            outbox,
            new EfIncidentStateStore(context, access),
            dispatches,
            new EfWorkflowAlterationStore(context, access, codec),
            new EfExecutionLivenessStateStore(context, access, codec),
            new EfSchedulerWorkQueueStore(context, access, codec),
            new EfWorkflowTestScopeStore(context, access, codec),
            dispatches,
            new EfWorkflowExecutionStateStore(context, access, codec),
            new EfWorkflowTestScopeCleanupStore(context, access, codec),
            outbox,
            outbox,
            new SqliteResources(context, connection));
    }

    public ValueTask DisposeAsync() => _resources?.DisposeAsync() ?? ValueTask.CompletedTask;

    internal sealed class CallCountingStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        public int Calls { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.CommitAsync(commit, decision, cancellationToken);
        }
    }

    private sealed class SqliteResources(DbContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}
