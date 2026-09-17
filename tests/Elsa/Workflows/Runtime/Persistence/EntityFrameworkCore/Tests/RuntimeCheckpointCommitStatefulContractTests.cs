using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The stateful half of the checkpoint commit contract, run identically against every
/// <see cref="IRuntimeCheckpointCommitStore"/>. These rules read the state a store holds, so each store enforces them inside
/// its own atomic boundary by calling one shared rule function. Each case seeds the same prior state into every store, then
/// commits a change the rule must refuse: every store must throw the same exception with the same message, write no commit
/// marker, and leave the seeded state as it was.
/// </summary>
public sealed class RuntimeCheckpointCommitStatefulContractTests
{
    private const string StatefulCommitId = "commit-stateful";

    public static TheoryData<string> StoreData => RuntimeCheckpointCommitContract.StoreData();

    public static TheoryData<string, string> ConflictData => StoreCaseData(ConflictCases.Keys);

    [Theory]
    [MemberData(nameof(ConflictData))]
    public async Task A_commit_that_conflicts_with_stored_state_is_rejected_the_same_way_by_every_store(string store, string caseName)
    {
        var conflict = ConflictCases[caseName];
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var commit = await conflict.Arrange(backend);

        var exception = await Assert.ThrowsAsync(conflict.ExceptionType, () => backend.Committer.CommitAsync(commit).AsTask());

        Assert.Equal(conflict.Message(commit), exception.Message);
        Assert.Equal(1, backend.Store.Calls);
        Assert.Equal(0, await backend.CountMarkersAsync());
        await conflict.AssertUnchanged(backend);
    }

    /// <summary>The accepted side of the linkage rule: a waited child's terminal commit resumes the parent it links.</summary>
    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_child_terminal_commit_carries_work_for_the_parent_its_terminal_dispatch_links(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var pending = PendingDispatch("workflow-parent", "activity-waited", mode: WorkflowDispatchMode.WaitForCompletion);
        await backend.Dispatches.SaveAsync(pending);
        await backend.Dispatches.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Started, OccurredAt.AddSeconds(1)));
        var completed = pending.TransitionTo(WorkflowDispatchStatus.Completed, OccurredAt.AddSeconds(2));

        var result = await backend.Committer.CommitAsync(Commit(
            pending.ChildWorkflowExecutionId,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], [DispatchChange(completed)]),
            [Intent("intent-parent-resume", pending.ParentWorkflowExecutionId)]));

        Assert.True(result.Succeeded);
        Assert.Single(result.PendingPostCommitWorkIds);
        Assert.Equal(1, await backend.CountMarkersAsync());
        Assert.Equal(WorkflowDispatchStatus.Completed, (await backend.Dispatches.FindAsync(pending.DispatchId))!.Status);
    }

    /// <summary>
    /// Spec 102 FR-011: a child that won admission before its scope began closing must still start, so cleanup can cancel
    /// it. Only a root start and a new child dispatch require an open scope; a child's first checkpoint does not.
    /// </summary>
    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task An_admitted_test_child_starts_even_after_its_scope_began_closing(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var scope = TestScope("scope-admitted-child");
        await backend.Scopes.CreateAsync(scope, OccurredAt.AddMinutes(-10));
        var dispatch = PendingDispatch(WorkflowId, "activity-test-child", testScope: scope);
        await backend.Dispatches.SaveAsync(dispatch);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted,
            (await backend.Admissions.TryAdmitAsync(dispatch.DispatchId, OccurredAt.AddMinutes(-5))).Disposition);
        await backend.Scopes.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, OccurredAt.AddMinutes(-1)));
        var child = TestRunExecution(dispatch.ChildWorkflowExecutionId, scope, parentWorkflowExecutionId: WorkflowId);

        var result = await backend.Committer.CommitAsync(Commit(
            child.WorkflowExecutionId,
            new RuntimeCheckpointStateChangeSet(Change(child.WorkflowExecutionId, child), null, [], [], [], [], [])));

        Assert.True(result.Succeeded);
        Assert.Equal(1, await backend.CountMarkersAsync());
        Assert.Equal(WorkflowTestScopeState.Closing, (await backend.Scopes.FindAsync(scope.ScopeId))!.State);
        Assert.NotNull(await backend.Executions.FindAsync(child.WorkflowExecutionId));
    }

    /// <summary>
    /// Deliberately bypasses the committer. The reserved ownership record is protected by each store as storage integrity,
    /// not only by the validator: a fenced in-memory commit used to wait forever on its own ownership gate here, and an EF
    /// commit would have overwritten the lease it was fenced against.
    /// </summary>
    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_fenced_write_to_the_reserved_ownership_record_fails_in_every_store_instead_of_hanging(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var lease = await backend.Ownership.AcquireAsync(WorkflowId);
        var ownershipStateId = RuntimeExecutionOwnershipStateId.For(WorkflowId);
        var commit = Commit(
            WorkflowId,
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [Change(ownershipStateId, Liveness(ownershipStateId))])) with
        {
            ExpectedFence = lease.ToFence()
        };

        var exception = await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            backend.Store.CommitAsync(commit, Immediate()).AsTask().WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.", exception.Message);
        Assert.Equal(0, await backend.CountMarkersAsync());
        Assert.Equal(lease.LeaseId, (await backend.Liveness.FindAsync(WorkflowId, ownershipStateId))!.ExecutionLease!.LeaseId);
    }

    private sealed record ConflictCase(
        Func<RuntimeCheckpointCommitContractBackend, Task<RuntimeCheckpointCommit>> Arrange,
        Type ExceptionType,
        Func<RuntimeCheckpointCommit, string> Message,
        Func<RuntimeCheckpointCommitContractBackend, Task> AssertUnchanged);

    /// <summary>
    /// A rejection that stays an <see cref="InvalidOperationException"/>: its rule can also report a lost race, also guards
    /// writes that are not checkpoint commits, or is raised by a store rather than by a shared rule function.
    /// </summary>
    private static ConflictCase Conflict(
        Func<RuntimeCheckpointCommitContractBackend, Task<RuntimeCheckpointCommit>> arrange,
        string message,
        Func<RuntimeCheckpointCommitContractBackend, Task> assertUnchanged) =>
        new(arrange, typeof(InvalidOperationException), _ => message, assertUnchanged);

    /// <summary>A rule violation no later attempt can change: the shared rule refuses it with the checkpoint validation type.</summary>
    private static ConflictCase Violation(
        Func<RuntimeCheckpointCommitContractBackend, Task<RuntimeCheckpointCommit>> arrange,
        string message,
        Func<RuntimeCheckpointCommitContractBackend, Task> assertUnchanged) =>
        new(arrange, typeof(RuntimeCheckpointCommitValidationException), _ => message, assertUnchanged);

    private static RuntimeCheckpointCommit StatefulCommit(
        RuntimeCheckpointStateChangeSet? stateChanges = null,
        IReadOnlyList<RuntimePostCommitIntent>? intents = null) =>
        Commit(WorkflowId, stateChanges, intents, StatefulCommitId);

    private static RuntimeCheckpointStateChangeSet Incidents(params RuntimeStateChange<IncidentState>[] incidents) =>
        new(null, null, [], [], [], incidents, []);

    private static RuntimeCheckpointStateChangeSet Terminal(WorkflowAlterationJobTerminalChange change) =>
        new(null, null, [], [], [], [], [], null, null, null, null, null, null, change);

    private static WorkflowAlterationJobTerminalChange TerminalChange(WorkflowAlterationJobState job, string checkpointCommitId) =>
        new(job.JobId, job.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [], checkpointCommitId, OccurredAt.AddMinutes(1));

    private static string JobId(string workflowExecutionId) =>
        WorkflowAlterationIdentity.CreateJobId(RuntimeCheckpointCommitContractBackend.AlterationPlanId, workflowExecutionId);

    private static ConflictCase ScopeNotOpen(
        Func<RuntimeCheckpointCommitContractBackend, Task<RuntimeCheckpointCommit>> arrange,
        Func<RuntimeCheckpointCommitContractBackend, Task> assertUnchanged) =>
        new(arrange, typeof(TestScopeAdmissionException), _ => "The workflow test scope is not open in the current persistence context.", assertUnchanged);

    private static Task CloseScopeAsync(RuntimeCheckpointCommitContractBackend backend, WorkflowTestScope scope) =>
        backend.Scopes.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, OccurredAt.AddMinutes(-1))).AsTask();

    private static readonly WorkflowTestScope ClosedScope = TestScope("scope-closed");
    private static readonly WorkflowTestScope ExpiredScope = TestScope("scope-expired", OccurredAt.AddMinutes(-1));
    private static readonly string TestDispatchId = new WorkflowDispatchIdentity(WorkflowId, "activity-test-child").DispatchId;
    private static readonly string DispatchId = new WorkflowDispatchIdentity(WorkflowId, DispatchActivityId).DispatchId;
    private static readonly string OutboxItemId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(StatefulCommitId, "intent-a");

    private static readonly IReadOnlyDictionary<string, ConflictCase> ConflictCases = new Dictionary<string, ConflictCase>
    {
        ["incident-resolution-outcome-is-write-once"] = Conflict(
            async backend =>
            {
                await backend.Incidents.SaveAsync(ResolvedIncident("incident-a", "Acme.First"));
                return StatefulCommit(Incidents(Change("incident-a", ResolvedIncident("incident-a", "Acme.Second"))));
            },
            "Incident 'incident-a' has a committed resolution outcome and lifecycle effect that cannot be changed.",
            async backend => Assert.Equal("Acme.First", (await backend.Incidents.FindAsync(WorkflowId, "incident-a"))!.ResolutionOutcome!.ActionKind)),

        ["incident-append-is-create-only"] = Violation(
            async backend =>
            {
                await backend.Incidents.SaveAsync(Incident("incident-a"));
                var appendedAgain = new IncidentState("incident-a", WorkflowId, null, null, IncidentSeverity.Error, IncidentStatus.Open, null, "test-failure", "appended again", OccurredAt, null);
                return StatefulCommit(Incidents(Change("incident-a", appendedAgain, RuntimeStateChangeOperation.Append)));
            },
            "Incident 'incident-a' already exists for workflow execution 'workflow-a' and cannot be appended again.",
            async backend => Assert.Equal("contract incident", (await backend.Incidents.FindAsync(WorkflowId, "incident-a"))!.Message)),

        ["dispatch-transition-must-be-legal"] = Conflict(
            async backend =>
            {
                var pending = PendingDispatch(WorkflowId, DispatchActivityId);
                await backend.Dispatches.SaveAsync(pending);
                await backend.Dispatches.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Started, OccurredAt.AddSeconds(1)));
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                    [DispatchChange(PendingDispatch(WorkflowId, DispatchActivityId, OccurredAt.AddSeconds(2)))]));
            },
            $"Workflow dispatch '{DispatchId}' cannot transition from 'Started' to 'Pending'.",
            async backend => Assert.Equal(WorkflowDispatchStatus.Started, (await backend.Dispatches.FindAsync(DispatchId))!.Status)),

        ["dispatch-cancellation-must-be-permitted"] = Violation(
            async backend =>
            {
                await backend.Dispatches.SaveAsync(PendingDispatch(WorkflowId, DispatchActivityId));
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null, null, null, null,
                    [CancellationRequest(WorkflowId, DispatchActivityId)]));
            },
            $"Workflow dispatch '{DispatchId}' does not permit parent cancellation propagation.",
            async backend => Assert.Equal(WorkflowDispatchStatus.Pending, (await backend.Dispatches.FindAsync(DispatchId))!.Status)),

        ["alteration-job-must-belong-to-the-checkpoint-workflow"] = Violation(
            async backend =>
            {
                var job = await backend.ClaimAlterationJobAsync(OtherWorkflowId);
                return StatefulCommit(Terminal(TerminalChange(job, StatefulCommitId)));
            },
            $"Alteration job '{JobId(OtherWorkflowId)}' belongs to workflow 'workflow-other', not 'workflow-a'.",
            async backend => Assert.Equal(WorkflowAlterationJobStatus.Running, (await backend.Alterations.FindJobAsync(JobId(OtherWorkflowId)))!.Status)),

        ["alteration-terminal-evidence-is-write-once"] = Conflict(
            async backend =>
            {
                var job = await backend.ClaimAlterationJobAsync(WorkflowId);
                await backend.Alterations.ApplyTerminalJobChangeAsync(TerminalChange(job, "commit-earlier"));
                return StatefulCommit(Terminal(TerminalChange(job, StatefulCommitId)));
            },
            "Terminal alteration evidence conflicts with the stored result.",
            async backend => Assert.Equal("commit-earlier", (await backend.Alterations.FindJobAsync(JobId(WorkflowId)))!.CheckpointCommitId)),

        ["outbox-item-identity-is-idempotent-only-for-an-equivalent-pending-item"] = Conflict(
            async backend =>
            {
                // Same identity and intent, but a different availability: only the item-level fields differ.
                await backend.SeedPendingOutboxItemAsync(new RuntimePostCommitOutboxItem(
                    OutboxItemId, Intent("intent-a"), RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt.AddMinutes(1)));
                return StatefulCommit(intents: [Intent("intent-a")]);
            },
            $"Post-commit outbox item '{OutboxItemId}' already exists with a different intent or status.",
            async backend => Assert.Equal(OccurredAt.AddMinutes(1), (await backend.Outbox.FindAsync(OutboxItemId))!.AvailableAt)),

        ["test-scope-root-start-requires-an-open-scope"] = ScopeNotOpen(
            async backend =>
            {
                await backend.Scopes.CreateAsync(ClosedScope, OccurredAt.AddMinutes(-10));
                await CloseScopeAsync(backend, ClosedScope);
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(Change(WorkflowId, TestRunExecution(WorkflowId, ClosedScope)), null, [], [], [], [], []));
            },
            async backend => Assert.Null(await backend.Executions.FindAsync(WorkflowId))),

        ["test-scope-root-start-requires-an-unexpired-scope"] = ScopeNotOpen(
            async backend =>
            {
                await backend.Scopes.CreateAsync(ExpiredScope, OccurredAt.AddMinutes(-10));
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(Change(WorkflowId, TestRunExecution(WorkflowId, ExpiredScope)), null, [], [], [], [], []));
            },
            async backend => Assert.Null(await backend.Executions.FindAsync(WorkflowId))),

        ["test-scope-child-dispatch-requires-an-open-scope"] = ScopeNotOpen(
            async backend =>
            {
                await backend.Scopes.CreateAsync(ClosedScope, OccurredAt.AddMinutes(-10));
                await CloseScopeAsync(backend, ClosedScope);
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                    [DispatchChange(PendingDispatch(WorkflowId, "activity-test-child", testScope: ClosedScope))]));
            },
            async backend => Assert.Null(await backend.Dispatches.FindAsync(TestDispatchId))),

        ["execution-fence-must-be-current"] = new(
            async backend =>
            {
                var lease = await backend.Ownership.AcquireAsync(WorkflowId);
                return StatefulCommit() with { ExpectedFence = new RuntimeExecutionFence(lease.LeaseId, lease.OwnerId, lease.FencingToken + 1) };
            },
            typeof(RuntimeStaleFencingTokenException),
            commit => new RuntimeStaleFencingTokenException(WorkflowId, commit.ExpectedFence!.FencingToken, commit.ExpectedFence.FencingToken - 1, RuntimeFencingRejectionReason.StaleToken).Message,
            async backend => Assert.Equal(1, (await backend.Liveness.FindAsync(WorkflowId, RuntimeExecutionOwnershipStateId.For(WorkflowId)))!.ExecutionLease!.FencingToken)),

        ["consumed-work-claim-token-must-be-current"] = new(
            async backend =>
            {
                // The claim expires and the same owner reclaims: the successor claim advances the token.
                await backend.Queue.EnqueueAsync(SchedulerWork("work-a"));
                var stale = (await backend.Queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowId, "owner-a", OccurredAt, TimeSpan.FromMinutes(1))))!;
                var successor = (await backend.Queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowId, "owner-a", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1))))!;
                Assert.True(successor.FencingToken > stale.FencingToken);
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null, null, null, null, null,
                    [ConsumedSchedulerWorkItem.FromClaim(stale)]));
            },
            typeof(RuntimeSchedulerWorkConsumeConflictException),
            _ => new RuntimeSchedulerWorkConsumeConflictException(WorkflowId, "work-a").Message,
            async backend =>
            {
                var claim = (await backend.Queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowId, "owner-a", OccurredAt.AddMinutes(10), TimeSpan.FromMinutes(1))))!;
                Assert.Equal("work-a", claim.Item.WorkItemId);
            }),

        ["consumed-work-claim-owner-must-be-current"] = new(
            async backend =>
            {
                await backend.Queue.EnqueueAsync(SchedulerWork("work-a"));
                var claim = (await backend.Queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowId, "owner-a", OccurredAt, TimeSpan.FromMinutes(5))))!;
                return StatefulCommit(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null, null, null, null, null,
                    [ConsumedSchedulerWorkItem.FromClaim(claim) with { ClaimOwnerId = "owner-b" }]));
            },
            typeof(RuntimeSchedulerWorkConsumeConflictException),
            _ => new RuntimeSchedulerWorkConsumeConflictException(WorkflowId, "work-a").Message,
            async backend =>
            {
                var claim = (await backend.Queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(WorkflowId, "owner-a", OccurredAt.AddMinutes(10), TimeSpan.FromMinutes(5))))!;
                Assert.Equal("work-a", claim.Item.WorkItemId);
            }),
    };
}
