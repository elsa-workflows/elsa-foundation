using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// T026 RED. These facts deliberately create durable rows before looking up the T027 operation.  Consequently a
/// no-op implementation cannot satisfy them: successful adoption has to change only the current authority binding,
/// and a rejection has to leave every observable durable surface byte-for-byte unchanged.
/// </summary>
public sealed class RuntimeCheckpointPreparedAdoptionTests
{
    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Exact_set_adoption_updates_only_current_authority_binding_and_replays_at_the_same_fence(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await AdoptionFixture.CreateAsync(route, entryCount: 2);
        var before = fixture.Snapshot();
        var target = Fence("target", 2);
        var request = fixture.Request(target);

        var adopted = await InvokeAdoptionAsync(fixture.Store, request);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted, adopted.Status);
        Assert.Equal(request.WorkflowExecutionId, adopted.WorkflowExecutionId);
        Assert.Equal(request.Route, adopted.Route);
        Assert.Equal(request.ThroughWorkflowCheckpointOrder, adopted.ThroughWorkflowCheckpointOrder);

        var afterAdoption = fixture.Snapshot();
        Assert.Equal(before.Commits, afterAdoption.Commits);
        Assert.Equal(before.HighWatermarks, afterAdoption.HighWatermarks);
        Assert.Equal(before.TerminalSurfaces, afterAdoption.TerminalSurfaces);
        Assert.Equal(before.BackingStores, afterAdoption.BackingStores);
        AssertCurrentBindingOnlyChanged(before.Prepared, afterAdoption.Prepared, target);

        // The receipt must be idempotent at the already-current fence. Rebuild members from the durable rows so a
        // provider cannot accept stale expected revisions by accident.
        var replay = await InvokeAdoptionAsync(
            fixture.Store,
            fixture.Request(target, afterAdoption.Prepared.Select(fixture.Member).ToArray()));
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay, replay.Status);
        AssertDurableSnapshotEqual(afterAdoption, fixture.Snapshot());
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Adopted_current_fence_rejects_older_or_different_ownership_without_mutation(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await AdoptionFixture.CreateAsync(route, entryCount: 2);
        var current = new RuntimeExecutionFence("lease-current", "owner-current", 5);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Store, fixture.Request(current))).Status);
        var adoptedAtFive = fixture.Snapshot();
        var currentMembers = adoptedAtFive.Prepared.Select(fixture.Member).ToArray();
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay,
            (await InvokeAdoptionAsync(fixture.Store, fixture.Request(current, currentMembers))).Status);
        AssertDurableSnapshotEqual(adoptedAtFive, fixture.Snapshot());

        var newer = new RuntimeExecutionFence("lease-current", "owner-current", 6);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Store, fixture.Request(newer, currentMembers))).Status);
        var adoptedAtSix = fixture.Snapshot();
        var newerMembers = adoptedAtSix.Prepared.Select(fixture.Member).ToArray();

        RuntimeExecutionFence[] rejectedTargets =
        [
            new("lease-current", "owner-current", 5),
            new("lease-current", "owner-current", 4),
            new("lease-current", "owner-other", 7),
            new("lease-other", "owner-current", 7)
        ];
        foreach (var rejected in rejectedTargets)
        {
            var receipt = await InvokeAdoptionAsync(fixture.Store, fixture.Request(rejected, newerMembers));
            Assert.True(receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost);
            AssertDurableSnapshotEqual(adoptedAtSix, fixture.Snapshot());
        }
    }

    [Theory]
    [MemberData(nameof(RejectedExactSetCases))]
    public async Task Exact_set_adoption_rejects_concrete_mismatches_atomically(
        RuntimeCheckpointRecoveryRoute route,
        string name,
        Func<AdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> buildRequest)
    {
        var fixture = await AdoptionFixture.CreateAsync(route, entryCount: 3);
        var before = fixture.Snapshot();
        var receipt = await InvokeAdoptionAsync(fixture.Store, buildRequest(fixture));

        Assert.True(
            receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost,
            $"Expected conflict or ownership loss for '{name}' ({route}), but received {receipt.Status}.");
        AssertDurableSnapshotEqual(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Cancellation_and_transaction_entry_failure_leave_real_prepared_rows_untouched(RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await AdoptionFixture.CreateAsync(route, entryCount: 2);
        var before = fixture.Snapshot();
        var beforeRaw = fixture.RawStateSnapshot();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeAdoptionAsync(fixture.Store, fixture.Request(Fence("cancelled", 2)), cancelled.Token));
        AssertDurableSnapshotEqual(before, fixture.Snapshot());

        // Use the existing internal participant-gate fault injector. It is test-owned through InternalsVisibleTo and
        // requires no public production seam; adoption must enter the provider transaction before mutating rows.
        typeof(InMemoryCheckpointParticipantGate)
            .GetProperty("PostWaitFaultForTesting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fixture.State.TransactionGate, new InvalidOperationException("adoption-transaction"));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            InvokeAdoptionAsync(fixture.Store, fixture.Request(Fence("failed", 2))));
        Assert.Equal(beforeRaw, fixture.RawStateSnapshot());
    }

    private static IEnumerable<(string Name, Func<AdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> Build)> RejectedExactSetRequests()
    {
        yield return ("missing member", fixture => fixture.Request(Fence("missing", 2), fixture.Prepared[..1]));
        yield return ("extra member", fixture => fixture.Request(Fence("extra", 2), [.. fixture.Prepared, fixture.Prepared[0] with { CommitId = "extra" }]));
        yield return ("duplicate member", fixture => fixture.Request(Fence("duplicate", 2), [.. fixture.Prepared, fixture.Prepared[0]]));
        yield return ("partial exact set", fixture => fixture.Request(Fence("partial", 2), fixture.Prepared[..2]));
        yield return ("out of order members", fixture => fixture.Request(Fence("order", 2), fixture.Prepared.Reverse().ToArray()));
        yield return ("mixed workflow", fixture => fixture.Request(Fence("workflow", 2)) with { WorkflowExecutionId = "workflow-other" });
        yield return ("mixed original authority", fixture => fixture.Request(Fence("authority", 2), [fixture.Prepared[0] with { RecoveryAuthority = Authority("other-work") }, .. fixture.Prepared[1..]]));
        yield return ("mixed current fence", fixture => fixture.Request(Fence("current", 2), [fixture.Prepared[0] with { ExpectedCurrentAuthorityFence = Fence("wrong-current", 1) }, .. fixture.Prepared[1..]]));
        yield return ("ledger-token mismatch", fixture => fixture.Request(Fence("token", 2), [fixture.Prepared[0] with { LedgerToken = "wrong-token" }, .. fixture.Prepared[1..]]));
        yield return ("canonical digest mismatch", fixture => fixture.Request(Fence("digest", 2), [fixture.Prepared[0] with { CanonicalInputReference = "wrong-digest-reference" }, .. fixture.Prepared[1..]]));
        yield return ("original order revision mismatch", fixture => fixture.Request(Fence("order-revision", 2), [fixture.Prepared[0] with { OriginalOrderRevision = fixture.Prepared[0].OriginalOrderRevision + 1 }, .. fixture.Prepared[1..]]));
        yield return ("original context revision mismatch", fixture => fixture.Request(Fence("context-revision", 2), [fixture.Prepared[0] with { OriginalContextRevision = fixture.Prepared[0].OriginalContextRevision + 1 }, .. fixture.Prepared[1..]]));
        yield return ("authority revision mismatch", fixture => fixture.Request(Fence("authority-revision", 2), [fixture.Prepared[0] with { ExpectedAuthorityRevision = fixture.Prepared[0].ExpectedAuthorityRevision + 1 }, .. fixture.Prepared[1..]]));
        yield return ("canonical fingerprint mismatch", fixture => fixture.Request(Fence("fingerprint", 2), [fixture.Prepared[0] with { CanonicalInputFingerprint = "sha256:wrong" }, .. fixture.Prepared[1..]]));
        yield return ("hidden prepared-set gap", fixture => fixture.Request(Fence("gap", 2), [fixture.Prepared[0], fixture.Prepared[2]]));
    }

    public static TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<AdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>> RejectedExactSetCases
    {
        get
        {
            var data = new TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<AdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>>();
            foreach (var route in new[] { RuntimeCheckpointRecoveryRoute.SourceBound, RuntimeCheckpointRecoveryRoute.SourceFree })
            foreach (var (name, build) in RejectedExactSetRequests())
                data.Add(route, name, build);
            return data;
        }
    }

    private static async Task<RuntimeCheckpointPreparedAdoptionReceipt> InvokeAdoptionAsync(
        IRuntimeCheckpointPreparedLedgerStore store,
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var operation = typeof(IRuntimeCheckpointPreparedLedgerStore).GetMethod(
            "AdoptPreparedAsync",
            [typeof(RuntimeCheckpointPreparedAdoptionRequest), typeof(CancellationToken)]);
        Assert.True(operation is not null,
            "T027 must add one provider-atomic AdoptPreparedAsync exact-set CAS; T026 remains intentionally RED until then.");

        var awaitable = operation!.Invoke(store, [request, cancellationToken]);
        Assert.NotNull(awaitable);
        var task = (Task)awaitable!.GetType().GetMethod("AsTask")!.Invoke(awaitable, [])!;
        await task;
        return (RuntimeCheckpointPreparedAdoptionReceipt)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static void AssertCurrentBindingOnlyChanged(
        IReadOnlyList<PreparedSnapshot> before,
        IReadOnlyList<PreparedSnapshot> after,
        RuntimeExecutionFence target)
    {
        Assert.Equal(before.Count, after.Count);
        for (var index = 0; index < before.Count; index++)
        {
            Assert.Equal(before[index] with { CurrentAuthorityFence = target, AuthorityRevision = before[index].AuthorityRevision + 1 }, after[index]);
        }
    }

    private static void AssertDurableSnapshotEqual(DurableSnapshot expected, DurableSnapshot actual)
    {
        Assert.Equal(expected.Prepared, actual.Prepared);
        Assert.Equal(expected.Commits, actual.Commits);
        Assert.Equal(expected.HighWatermarks, actual.HighWatermarks);
        Assert.Equal(expected.TerminalSurfaces, actual.TerminalSurfaces);
        Assert.Equal(expected.BackingStores, actual.BackingStores);
    }

    private static RuntimeExecutionFence Fence(string owner, long token) =>
        new($"lease-{owner}", owner, Math.Max(token, 1));

    private static RuntimeCheckpointRecoveryAuthority Authority(string workItemId) =>
        new(1, "runtime.scheduler-work", "workflow-adoption", workItemId, $"sha256:{workItemId}");

    public sealed record PreparedSnapshot(
        string CommitId,
        string LedgerToken,
        long Order,
        string Fingerprint,
        string Reference,
        RuntimeExecutionFence? OriginalFence,
        long OriginalOrderRevision,
        long OriginalContextRevision,
        RuntimeCheckpointRecoveryAuthority? RecoveryAuthority,
        RuntimeExecutionFence? CurrentAuthorityFence,
        long AuthorityRevision,
        string CanonicalBytes,
        string Provenance);

    public sealed record DurableSnapshot(
        IReadOnlyList<PreparedSnapshot> Prepared,
        string Commits,
        (long Reserved, long Committed, long Revision) HighWatermarks,
        string TerminalSurfaces,
        string BackingStores);

    public sealed class AdoptionFixture
    {
        private AdoptionFixture(InMemoryRuntimeCheckpointCommitStore store, InMemoryRuntimeCheckpointStoreState state, string workflowExecutionId, RuntimeCheckpointRecoveryRoute route, RuntimeCheckpointPreparedAdoptionMember[] prepared)
        {
            Store = store;
            State = state;
            WorkflowExecutionId = workflowExecutionId;
            Route = route;
            Prepared = prepared;
        }

        public InMemoryRuntimeCheckpointCommitStore Store { get; }
        public InMemoryRuntimeCheckpointStoreState State { get; }
        public string WorkflowExecutionId { get; }
        public RuntimeCheckpointRecoveryRoute Route { get; }
        public RuntimeCheckpointPreparedAdoptionMember[] Prepared { get; }

        public static async Task<AdoptionFixture> CreateAsync(RuntimeCheckpointRecoveryRoute route, int entryCount)
        {
            var workflow = "workflow-adoption";
            var state = new InMemoryRuntimeCheckpointStoreState();
            var dispatchStore = new InMemoryWorkflowDispatchStore(state);
            var store = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);

            // Seed nonempty context, outbox, marker/receipt, high-watermark, ledger-compaction, and dispatch/state
            // surfaces. The adoption assertions snapshot all of them so the future CAS cannot hide collateral writes.
            var seedDispatch = SeedDispatch(workflow);
            var seedIntent = new RuntimePostCommitIntent(
                "adoption-seed-intent", workflow, "adoption.seed", DateTimeOffset.UnixEpoch,
                "activity-seed", "adoption-seed-key", JsonSerializer.SerializeToElement(new { seed = true }));
            var seedOutbox = new RuntimePostCommitOutboxItem(
                "adoption-seed-outbox", seedIntent, RuntimePostCommitOutboxStatus.Pending,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            var seedCommit = new RuntimeCheckpointCommit(
                "adoption-seed-commit",
                new RuntimeCheckpoint("adoption-seed-checkpoint", "AdoptionSeed", workflow, DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                    workflowDispatches:
                    [
                        new RuntimeStateChange<WorkflowDispatchRecord>(
                            seedDispatch.DispatchId, RuntimeStateChangeOperation.Upsert, seedDispatch, new Dictionary<string, string>())
                    ],
                    postCommitOutbox:
                    [
                        new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                            seedOutbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, seedOutbox, new Dictionary<string, string>())
                    ]),
                [],
                new Dictionary<string, string>());
            var seedRequest = new RuntimeCheckpointPrepareRequest(
                seedCommit,
                "adoption-seed",
                "adoption-seed-operation",
                new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["adoption.seed"] = route.ToString() }));
            var seedPreparation = await store.PrepareAsync(seedRequest);
            var seedToken = Assert.IsType<RuntimeCheckpointPreparationToken>(seedPreparation.Token);
            var enrichedSeed = seedCommit with { Checkpoint = seedCommit.Checkpoint with { Provenance = seedToken.Provenance } };
            Assert.Equal(
                RuntimeCheckpointCommitStoreStatus.Committed,
                (await store.CommitPreparedAsync(
                    seedToken,
                    enrichedSeed,
                    new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate))).Status);

            for (var index = 1; index <= entryCount; index++)
            {
                var commit = new RuntimeCheckpointCommit(
                    $"adoption-{route}-{index}",
                    new RuntimeCheckpoint($"checkpoint-{index}", "ScheduleActivity", workflow, DateTimeOffset.UnixEpoch.AddTicks(index), [], new Dictionary<string, string>()),
                    new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
                var request = RuntimeCheckpointPrepareRequest.From(commit) with
                {
                    RecoveryAuthority = route == RuntimeCheckpointRecoveryRoute.SourceBound ? Authority("work-adoption") : null
                };
                Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, (await store.PrepareAsync(request)).Status);
            }

            var provisional = new AdoptionFixture(store, state, workflow, route, []);
            return new AdoptionFixture(
                store,
                state,
                workflow,
                route,
                provisional.Snapshot().Prepared.Select(provisional.Member).ToArray());
        }

        public RuntimeCheckpointPreparedAdoptionRequest Request(RuntimeExecutionFence target, IReadOnlyList<RuntimeCheckpointPreparedAdoptionMember>? members = null) =>
            new(WorkflowExecutionId, Route, Prepared[^1].WorkflowCheckpointOrder, target, members ?? Prepared);

        public RuntimeCheckpointPreparedAdoptionMember Member(PreparedSnapshot reservation) =>
            new(reservation.CommitId, reservation.LedgerToken, reservation.Order, reservation.Fingerprint, reservation.Reference,
                reservation.OriginalFence, reservation.OriginalOrderRevision, reservation.OriginalContextRevision,
                reservation.RecoveryAuthority, reservation.CurrentAuthorityFence, reservation.AuthorityRevision);

        public DurableSnapshot Snapshot()
        {
            var entries = Store.ListLogicalCheckpointLedgerEntries().OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder).ToArray();
            var prepared = entries.Where(entry => entry.Status == RuntimeLogicalCheckpointLedgerStatus.Prepared).Select(entry => new PreparedSnapshot(
                entry.CommitId, entry.LedgerToken, entry.Provenance.WorkflowCheckpointOrder, entry.InputFingerprint,
                entry.CanonicalInputReference!, entry.ExpectedFence, entry.ExpectedOrderRevision!.Value, entry.ExpectedContextRevision!.Value,
                entry.RecoveryAuthority, entry.CurrentAuthorityFence, entry.AuthorityRevision, entry.CanonicalInputPayload!,
                JsonSerializer.Serialize(entry.Provenance))).ToArray();
            return new(prepared,
                JsonSerializer.Serialize(Store.ListCommits()),
                Store.GetCheckpointOrderHighWatermarks(WorkflowExecutionId),
                JsonSerializer.Serialize(entries.Select(entry => new { entry.Status, entry.Receipt, entry.CommitFingerprint, entry.TerminalFoldFingerprint, entry.TerminalPreparationToken })),
                SnapshotBackingStores());
        }

        public string RawStateSnapshot() => JsonSerializer.Serialize(new[]
        {
            SnapshotStateProperty("Commits"),
            SnapshotStateProperty("LogicalCheckpointLedger"),
            SnapshotStateProperty("CheckpointOrders"),
            SnapshotStateProperty("ExecutionContexts"),
            SnapshotStateProperty("OutboxItems"),
            SnapshotStateProperty("WorkflowDispatches"),
            SnapshotStateProperty("WorkflowTestScopes")
        });

        private string SnapshotBackingStores() => JsonSerializer.Serialize(new[]
        {
            SnapshotStateProperty("ExecutionContexts"),
            SnapshotStateProperty("OutboxItems"),
            SnapshotStateProperty("WorkflowDispatches"),
            SnapshotStateProperty("WorkflowTestScopes")
        });

        private string SnapshotStateProperty(string name)
        {
            var value = typeof(InMemoryRuntimeCheckpointStoreState)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(State);
            return JsonSerializer.Serialize(value, value!.GetType());
        }

        private static WorkflowDispatchRecord SeedDispatch(string workflowExecutionId)
        {
            var identity = new WorkflowDispatchIdentity(workflowExecutionId, "activity-seed");
            return new WorkflowDispatchRecord(
                identity.DispatchId,
                workflowExecutionId,
                "activity-seed",
                identity.ChildWorkflowExecutionId,
                new WorkflowExecutableIdentity("artifact-seed", "definition-seed", "version-seed", "1", "hash-seed"),
                new WorkflowExecutableSourceProvenance(
                    "source-seed", "WorkflowDefinitionVersion", "version-seed", "1",
                    "definition-seed", "version-seed", "1", "publication-seed", "slot-seed"),
                WorkflowDispatchMode.FireAndForget,
                WorkflowDispatchStatus.Pending,
                null,
                null,
                new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
                WorkflowRunKind.PublishedRun,
                new WorkflowExecutionAuthoritySnapshot(workflowExecutionId, "initiator-seed"),
                [],
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
        }
    }
}
