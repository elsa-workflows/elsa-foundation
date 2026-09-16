using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The checkpoint validation contract, run identically against every <see cref="IRuntimeCheckpointCommitStore"/>
/// implementation. The application layer (<see cref="RuntimeCheckpointCommitter"/>) owns the structural rules, so an
/// invalid commit must be rejected with the same exception and message whichever store is registered, and the store must
/// never be reached. A valid commit carrying every validated state kind must commit on every store, so the rules cannot
/// quietly reject what a store accepts either.
/// </summary>
/// <remarks>
/// Adding a store means adding it to <see cref="Stores"/>; adding a structural rule means adding a case to
/// <see cref="InvalidCases"/>. Rules that read persisted state are not structural and are not part of this contract.
/// </remarks>
public sealed class RuntimeCheckpointCommitValidationContractTests
{
    private const string InMemory = "in-memory";
    private const string EntityFramework = "entity-framework";
    private const string Tenant = "tenant-a";
    private const string WorkflowId = "workflow-a";
    private const string OtherWorkflowId = "workflow-other";
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Stores = [InMemory, EntityFramework];
    private const string DispatchActivityId = "activity-dispatch";

    public static TheoryData<string> StoreData => new(Stores);

    public static TheoryData<string, string> InvalidCommitData
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var store in Stores)
            foreach (var name in InvalidCases.Keys)
                data.Add(store, name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_valid_commit_carrying_every_validated_kind_commits(string store)
    {
        await using var backend = await Backend.CreateAsync(store);

        var result = await backend.Committer.CommitAsync(new CommitParts().Build());

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.PendingPostCommitWorkIds.Count);
        Assert.Equal(1, backend.Store.Calls);
        Assert.Equal(1, await backend.CountMarkersAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidCommitData))]
    public async Task An_invalid_commit_is_rejected_before_the_store_with_the_same_error(string store, string caseName)
    {
        var invalid = InvalidCases[caseName];
        var parts = new CommitParts();
        invalid.Mutate(parts);
        var commit = parts.Build();
        await using var backend = await Backend.CreateAsync(store);

        var exception = await Assert.ThrowsAsync(invalid.ExceptionType, () => backend.Committer.CommitAsync(commit).AsTask());

        Assert.Equal(invalid.Message, exception.Message);
        Assert.Equal(0, backend.Store.Calls);
        Assert.Equal(0, await backend.CountMarkersAsync());
    }

    private sealed record InvalidCase(Action<CommitParts> Mutate, Type ExceptionType, string Message);

    private static InvalidCase Invalid(Action<CommitParts> mutate, string message) =>
        new(mutate, typeof(InvalidOperationException), message);

    private static string OperationMessage(string kind, string allowed, RuntimeStateChangeOperation actual) =>
        $"A checkpoint commit can only carry {kind} {allowed} changes, not '{actual}'.";

    private static string WorkflowMessage(string subject) =>
        $"{subject} WorkflowExecutionId '{OtherWorkflowId}' must match the checkpoint workflow execution ID '{WorkflowId}'.";

    private static readonly IReadOnlyDictionary<string, InvalidCase> InvalidCases = new Dictionary<string, InvalidCase>
    {
        ["workflow-execution-delete"] = Invalid(
            parts => parts.WorkflowExecution = parts.WorkflowExecution! with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("workflow execution", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["workflow-execution-state-id"] = Invalid(
            parts => parts.WorkflowExecution = parts.WorkflowExecution! with { StateId = "workflow-stale" },
            "Workflow execution state change StateId must match WorkflowExecutionState.WorkflowExecutionId."),
        ["workflow-execution-other-workflow"] = Invalid(
            parts => parts.WorkflowExecution = Change(OtherWorkflowId, Execution(OtherWorkflowId)),
            WorkflowMessage("Workflow execution state change")),

        ["scheduler-delete"] = Invalid(
            parts => parts.Scheduler = parts.Scheduler! with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("scheduler", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["scheduler-state-id"] = Invalid(
            parts => parts.Scheduler = parts.Scheduler! with { StateId = "workflow-stale" },
            "Scheduler state change StateId must match SchedulerState.WorkflowExecutionId."),
        ["scheduler-other-workflow"] = Invalid(
            parts => parts.Scheduler = Change(OtherWorkflowId, new SchedulerState(OtherWorkflowId, 1)),
            WorkflowMessage("Scheduler state change")),

        ["activity-execution-append"] = Invalid(
            parts => parts.ActivityExecutions[0] = parts.ActivityExecutions[0] with { Operation = RuntimeStateChangeOperation.Append },
            OperationMessage("activity execution", "'Upsert'", RuntimeStateChangeOperation.Append)),
        ["activity-execution-other-workflow"] = Invalid(
            parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a", OtherWorkflowId)),
            WorkflowMessage("Activity execution state change")),
        ["activity-execution-value-flow-version"] = Invalid(
            parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                DocumentVersion = ActivityExecutionValueFlowDocumentVersions.Current + 1
            }),
            $"Activity invocation 'activity-a' carries value-flow document version {ActivityExecutionValueFlowDocumentVersions.Current + 1}; this runtime requires version {ActivityExecutionValueFlowDocumentVersions.Current}."),
        ["activity-execution-supersession"] = Invalid(
            parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with { Status = ActivityExecutionStatus.Superseded }),
            "A superseded activity execution must record both its successor ID and supersession time."),
        ["activity-execution-scope-provenance"] = Invalid(
            parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                ExecutionScopeId = "scope-own",
                Provenance = Provenance("scope-scheduled")
            }),
            "Activity execution ExecutionScopeId must match its scheduling provenance when both are present."),
        ["activity-execution-attempt-provenance"] = Invalid(
            parts => parts.ActivityExecutions[0] = Change("activity-a", Activity("activity-a") with
            {
                Attempt = new ActivityExecutionAttemptLineage(2, "activity-first", "activity-first"),
                Provenance = Provenance(null, new ActivityExecutionAttemptLineage(1, "activity-first", null))
            }),
            "Activity execution Attempt must match its scheduling provenance when both are present."),

        ["inspection-delete"] = Invalid(
            parts => parts.Inspections[0] = parts.Inspections[0] with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("activity execution inspection", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["inspection-other-workflow"] = Invalid(
            parts => parts.Inspections[0] = Change("activity-a", Inspection("activity-a", OtherWorkflowId)),
            WorkflowMessage("Activity execution inspection state change")),
        ["inspection-scope-provenance"] = Invalid(
            parts => parts.Inspections[0] = Change("activity-a", Inspection("activity-a") with { ExecutionScopeId = "scope-own" }),
            "Activity execution inspection ExecutionScopeId must match its scheduling provenance when both are present."),

        ["incident-delete"] = Invalid(
            parts => parts.Incidents[0] = parts.Incidents[0] with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("incident", "'Append' or 'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["incident-other-workflow"] = Invalid(
            parts => parts.Incidents[0] = Change("incident-a", Incident("incident-a", OtherWorkflowId), RuntimeStateChangeOperation.Append),
            WorkflowMessage("Incident state change")),
        ["incident-duplicate-id"] = Invalid(
            parts => parts.Incidents.Add(Change("incident-a", Incident("incident-a"), RuntimeStateChangeOperation.Upsert)),
            "Incident 'incident-a' occurs more than once in one checkpoint commit."),

        ["durable-value-append"] = Invalid(
            parts => parts.DurableValues[0] = parts.DurableValues[0] with { Operation = RuntimeStateChangeOperation.Append },
            OperationMessage("durable value", "'Upsert' or 'Delete'", RuntimeStateChangeOperation.Append)),
        ["durable-value-other-workflow"] = Invalid(
            parts => parts.DurableValues[0] = Change("value-a", DurableValue("value-a", OtherWorkflowId)),
            WorkflowMessage("Durable value state change")),

        ["bookmark-append"] = Invalid(
            parts => parts.Bookmarks[0] = parts.Bookmarks[0] with { Operation = RuntimeStateChangeOperation.Append },
            OperationMessage("bookmark", "'Upsert' or 'Delete'", RuntimeStateChangeOperation.Append)),
        ["bookmark-other-workflow"] = Invalid(
            parts => parts.Bookmarks[0] = Change("bookmark-a", Bookmark("bookmark-a", OtherWorkflowId)),
            WorkflowMessage("Bookmark state change")),

        ["operational-delete"] = Invalid(
            parts => parts.Operational[0] = parts.Operational[0] with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("operational", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["operational-other-workflow"] = Invalid(
            parts => parts.Operational[0] = Change("operational-a", Liveness("operational-a", OtherWorkflowId)),
            WorkflowMessage("Operational state change")),
        ["operational-reserved-ownership"] = Invalid(
            parts => parts.Operational.Add(Change("ownership:workflow-a", Liveness("ownership:workflow-a"))),
            "Checkpoint operational changes cannot overwrite the reserved execution-ownership state."),

        // Outbox items reach a store folded from the commit's intents by the committer, so these cases carry the items
        // directly and no intents for the committer to fold over them.
        ["outbox-not-pending"] = Invalid(
            parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a"), RuntimePostCommitOutboxStatus.Delivered)),
            "Only pending post-commit outbox items can be saved as pending."),
        ["outbox-duplicate-conflict"] = Invalid(
            parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a")), OutboxChange("outbox-a", Intent("intent-b"))),
            "Post-commit outbox item 'outbox-a' occurs more than once with conflicting content."),
        ["outbox-delete"] = Invalid(
            parts => parts.UseOutbox(OutboxChange("outbox-a", Intent("intent-a")) with { Operation = RuntimeStateChangeOperation.Delete }),
            OperationMessage("post-commit outbox", "'Upsert'", RuntimeStateChangeOperation.Delete)),

        ["cleanup-other-workflow"] = Invalid(
            parts => parts.Cleanups[0] = parts.Cleanups[0] with { WorkflowExecutionId = OtherWorkflowId },
            WorkflowMessage("Activity-scope cleanup")),
        ["cleanup-without-outer-scope"] = Invalid(
            parts => parts.Cleanups[0] = parts.Cleanups[0] with { ActivityExecutionIds = ["activity-inner"] },
            "Activity scope cleanup must include its outer execution scope."),
        ["cleanup-blank-scope"] = new(
            parts => parts.Cleanups[0] = parts.Cleanups[0] with { ExecutionScopeId = " ", ActivityExecutionIds = [" "] },
            typeof(ArgumentException),
            BlankArgumentMessage(" ", "cleanup.ExecutionScopeId")),
        ["cleanup-bookmark-also-changed"] = Invalid(
            parts => parts.Cleanups[0] = parts.Cleanups[0] with { BookmarkIds = ["bookmark-a"] },
            "Bookmark 'bookmark-a' cannot be both changed and deleted by activity-scope cleanup in one checkpoint commit."),
        ["cleanup-work-item-also-consumed"] = Invalid(
            parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(WorkflowId, "work-cleanup", "owner-a", 1)),
            "Scheduler work item 'work-cleanup' cannot be both consumed and deleted by activity-scope cleanup in one checkpoint commit."),

        ["dispatch-delete"] = Invalid(
            parts => parts.Dispatches[0] = parts.Dispatches[0] with { Operation = RuntimeStateChangeOperation.Delete },
            OperationMessage("workflow dispatch", "'Upsert'", RuntimeStateChangeOperation.Delete)),
        ["dispatch-wrong-owner"] = Invalid(
            parts => parts.Dispatches[0] = DispatchChange(PendingDispatch(OtherWorkflowId, DispatchActivityId)),
            $"Workflow dispatch '{new WorkflowDispatchIdentity(OtherWorkflowId, DispatchActivityId).DispatchId}' status 'Pending' must be committed by its parent workflow execution."),
        ["dispatch-duplicate-conflict"] = Invalid(
            parts => parts.Dispatches.Add(DispatchChange(PendingDispatch(WorkflowId, DispatchActivityId, OccurredAt.AddSeconds(1)))),
            $"Workflow dispatch '{new WorkflowDispatchIdentity(WorkflowId, DispatchActivityId).DispatchId}' occurs more than once with conflicting state."),
        ["dispatch-cancellation-other-parent"] = Invalid(
            parts => parts.Cancellations.Add(CancellationRequest(OtherWorkflowId, "activity-cancel")),
            $"Workflow dispatch cancellation request '{new WorkflowDispatchIdentity(OtherWorkflowId, "activity-cancel").DispatchId}' must be committed by its parent workflow execution."),

        ["consumed-work-other-workflow"] = Invalid(
            parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(OtherWorkflowId, "work-consumed", "owner-a", 1)),
            WorkflowMessage("Consumed scheduler work item")),
        ["consumed-work-non-positive-token"] = Invalid(
            parts => parts.Consumed.Add(new ConsumedSchedulerWorkItem(WorkflowId, "work-consumed", "owner-a", 0)),
            "Consumed scheduler work item 'work-consumed' requires a positive fencing token."),

        ["alteration-terminal-other-commit"] = Invalid(
            parts => parts.AlterationTerminal = new WorkflowAlterationJobTerminalChange(
                "job-a", "claim-a", WorkflowAlterationJobStatus.Succeeded, [], "commit-other", OccurredAt),
            "Workflow alteration terminal evidence must reference its checkpoint commit ID."),
    };

    /// <summary>A commit that is valid on every store, carrying one change of each kind the structural rules inspect.</summary>
    private sealed class CommitParts
    {
        public RuntimeStateChange<WorkflowExecutionState>? WorkflowExecution { get; set; } = Change(WorkflowId, Execution(WorkflowId));
        public RuntimeStateChange<SchedulerState>? Scheduler { get; set; } = Change(WorkflowId, new SchedulerState(WorkflowId, 1));
        public List<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions { get; } = [Change("activity-a", Activity("activity-a"))];
        public List<RuntimeStateChange<ActivityExecutionInspectionProjection>> Inspections { get; } = [Change("activity-a", Inspection("activity-a"))];
        public List<RuntimeStateChange<IncidentState>> Incidents { get; } = [Change("incident-a", Incident("incident-a"), RuntimeStateChangeOperation.Append)];
        public List<RuntimeStateChange<DurableValueState>> DurableValues { get; } = [Change("value-a", DurableValue("value-a"))];
        public List<RuntimeStateChange<BookmarkState>> Bookmarks { get; } = [Change("bookmark-a", Bookmark("bookmark-a"))];
        public List<RuntimeStateChange<ExecutionLivenessState>> Operational { get; } = [Change("operational-a", Liveness("operational-a"))];
        public List<RuntimeStateChange<WorkflowDispatchRecord>> Dispatches { get; } = [DispatchChange(PendingDispatch(WorkflowId, DispatchActivityId))];
        public List<RuntimeStateChange<RuntimePostCommitOutboxItem>> PostCommitOutbox { get; } = [];
        public List<ActivityScopeCleanupRequest> Cleanups { get; } =
            [new(WorkflowId, "scope-a", ["scope-a"], ["bookmark-cleanup"], ["timer-cleanup"], ["work-cleanup"])];
        public List<WorkflowDispatchCancellationRequest> Cancellations { get; } = [];
        public List<ConsumedSchedulerWorkItem> Consumed { get; } = [];
        public WorkflowAlterationJobTerminalChange? AlterationTerminal { get; set; }
        // An intent names the execution its work is delivered to, which need not be the checkpoint's own: a waited
        // child's terminal checkpoint carries its parent's resume intent.
        public List<RuntimePostCommitIntent> Intents { get; } = [Intent("intent-a"), Intent("intent-parent-resume", OtherWorkflowId)];

        public void UseOutbox(params RuntimeStateChange<RuntimePostCommitOutboxItem>[] changes)
        {
            Intents.Clear();
            PostCommitOutbox.AddRange(changes);
        }

        public RuntimeCheckpointCommit Build() => new(
            "commit-contract",
            new RuntimeCheckpoint("checkpoint-contract", "ContractCheckpoint", WorkflowId, OccurredAt, [], new Dictionary<string, string>()),
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
            Intents,
            new Dictionary<string, string>());
    }

    /// <summary>A store behind the application-layer committer, with a probe for durable commit markers.</summary>
    private sealed class Backend : IAsyncDisposable
    {
        private readonly Func<Task<int>> _countMarkers;
        private readonly IAsyncDisposable? _resources;

        private Backend(IRuntimeCheckpointCommitStore store, Func<Task<int>> countMarkers, IAsyncDisposable? resources = null)
        {
            Store = new CallCountingStore(store);
            Committer = new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                Store,
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
                [],
                []);
            _countMarkers = countMarkers;
            _resources = resources;
        }

        public CallCountingStore Store { get; }
        public RuntimeCheckpointCommitter Committer { get; }

        public Task<int> CountMarkersAsync() => _countMarkers();

        public static async Task<Backend> CreateAsync(string store) => store switch
        {
            InMemory => CreateInMemory(),
            EntityFramework => await CreateEntityFrameworkAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(store), store, null)
        };

        private static Backend CreateInMemory()
        {
            var state = new InMemoryRuntimeCheckpointStoreState();
            var bookmarks = new InMemoryBookmarkStateStore();
            var store = new InMemoryRuntimeCheckpointCommitStore(
                new InMemoryWorkflowExecutionStateStore(),
                new InMemoryActivityExecutionStateStore(),
                bookmarks,
                new InMemoryDurableValueStateStore(),
                new InMemoryIncidentStateStore(),
                new InMemoryExecutionLivenessStateStore(),
                new InMemorySchedulerStateStore(),
                new InMemoryActivityExecutionInspectionStore(),
                new PassThroughRootWriteLeaseManager(),
                state,
                activityScopeCleanupStore: new ActivityScopeCleanupStore(bookmarks, new InMemoryDurableTimerStore(), new InMemoryWorkflowSchedulerWorkQueue()),
                workflowDispatchStore: new InMemoryWorkflowDispatchStore(state));
            return new Backend(store, () => Task.FromResult(store.ListCommits().Count));
        }

        private static async Task<Backend> CreateEntityFrameworkAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var store = new EfRuntimeCheckpointCommitStore(
                context, new FixedAccessor(Tenant), rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
            return new Backend(store, () => context.RuntimeCheckpointCommits.AsNoTracking().CountAsync(), new SqliteResources(context, connection));
        }

        public ValueTask DisposeAsync() => _resources?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class CallCountingStore(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
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

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }

    private static string BlankArgumentMessage(string value, string parameterName)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("The value was expected to be blank.");
    }

    private static RuntimeStateChange<TState> Change<TState>(
        string stateId,
        TState state,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert) =>
        new(stateId, operation, state, new Dictionary<string, string>());

    private static RuntimeStateChange<WorkflowDispatchRecord> DispatchChange(WorkflowDispatchRecord record) =>
        Change(record.DispatchId, record);

    private static RuntimeStateChange<RuntimePostCommitOutboxItem> OutboxChange(
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

    private static RuntimePostCommitIntent Intent(string intentId, string workflowExecutionId = WorkflowId) =>
        new(intentId, workflowExecutionId, "test.intent", OccurredAt, null, null, null);

    private static WorkflowExecutionState Execution(string workflowExecutionId) => new(
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

    private static ActivitySchedulingProvenance Provenance(
        string? executionScopeId,
        ActivityExecutionAttemptLineage? attempt = null,
        string workflowExecutionId = WorkflowId) =>
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, executionScopeId, "checkpoint", attempt: attempt);

    private static ActivityExecutionState Activity(string id, string workflowExecutionId = WorkflowId) => new(
        new ActivityExecution(id, workflowExecutionId, $"node-{id}", $"authored-{id}", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed, null, 1,
        OccurredAt, OccurredAt, OccurredAt, null, null, null, null,
        Provenance(null, workflowExecutionId: workflowExecutionId),
        null, [], [], 0, 0, new Dictionary<string, string>());

    private static ActivityExecutionInspectionProjection Inspection(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, $"node-{id}", $"authored-{id}", "Test.Activity", "1",
        ActivityExecutionStatus.Completed, null, 1, OccurredAt, OccurredAt, OccurredAt,
        "checkpoint-contract", "checkpoint-contract", OccurredAt,
        Provenance("scope-inspected", workflowExecutionId: workflowExecutionId),
        ["Done"], [], [], [], new Dictionary<string, string>(), "scope-inspected");

    private static IncidentState Incident(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, null, null,
        IncidentSeverity.Error, IncidentStatus.Open, null, "test-failure", "contract incident",
        OccurredAt, null);

    private static DurableValueState DurableValue(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, id, new RuntimeValueTypeDescriptor("json", null, null),
        DurableValueLifecycle.Result, DurableValueStorage.Inline,
        JsonSerializer.SerializeToElement("value"),
        null, null, OccurredAt, new Dictionary<string, string>());

    private static BookmarkState Bookmark(string id, string workflowExecutionId = WorkflowId) => new(
        id, workflowExecutionId, "activity-a", "node-activity-a", "resume-a", "stimulus", "stimulus-hash",
        JsonSerializer.SerializeToElement("payload"),
        new Dictionary<string, string>(), OccurredAt, OccurredAt.AddHours(1));

    private static ExecutionLivenessState Liveness(string id, string workflowExecutionId = WorkflowId) =>
        new(id, workflowExecutionId, null, null, null, null);

    private static WorkflowDispatchRecord PendingDispatch(string parent, string activity, DateTimeOffset? updatedAt = null) => new(
        new WorkflowDispatchIdentity(parent, activity).DispatchId,
        parent,
        activity,
        new WorkflowDispatchIdentity(parent, activity).ChildWorkflowExecutionId,
        new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1", "hash-child"),
        new WorkflowExecutableSourceProvenance("source-child", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
        WorkflowDispatchMode.FireAndForget,
        WorkflowDispatchStatus.Pending,
        null,
        Tenant,
        new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
        WorkflowRunKind.PublishedRun,
        new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
        [new WorkflowDispatchInputDescriptor("orderId", "string")],
        OccurredAt,
        updatedAt ?? OccurredAt,
        new Dictionary<string, string> { ["safe-code"] = "dispatch" });

    private static WorkflowDispatchCancellationRequest CancellationRequest(string parent, string activity) => new(
        new WorkflowDispatchIdentity(parent, activity).DispatchId,
        parent,
        activity,
        new WorkflowDispatchIdentity(parent, activity).ChildWorkflowExecutionId,
        OccurredAt);
}
