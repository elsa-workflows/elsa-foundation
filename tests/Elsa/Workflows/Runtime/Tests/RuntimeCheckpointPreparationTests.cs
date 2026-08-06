using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointPreparationTests
{
    [Fact]
    public void Execution_context_is_versioned_bounded_immutable_and_canonically_ordered()
    {
        var source = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" };
        var snapshot = new RuntimeExecutionContextSnapshot(RuntimeExecutionContextSnapshot.CurrentVersion, source);
        source["a"] = "changed";

        Assert.Equal(["a", "z"], snapshot.Entries.Keys);
        Assert.Equal("first", snapshot.Entries["a"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeExecutionContextSnapshot(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeExecutionContextSnapshot(
            RuntimeExecutionContextSnapshot.CurrentVersion,
            Enumerable.Range(0, RuntimeExecutionContextSnapshot.MaximumEntryCount + 1)
                .ToDictionary(x => x.ToString(), _ => "value")));
    }

    [Fact]
    public void Provenance_requires_a_positive_workflow_checkpoint_order()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 0));
        Assert.Equal(1, new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 1).WorkflowCheckpointOrder);
    }

    [Fact]
    public async Task In_memory_prepare_reserves_monotonic_order_and_matching_replay_reuses_it()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var store = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: workflowStates);
        var first = NewCommit("commit-1", "checkpoint-1");
        var second = NewCommit("commit-2", "checkpoint-2");

        var prepared = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(first));
        var replay = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(first));
        var successor = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(second));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, prepared.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, replay.Status);
        Assert.Equal(prepared.Token!.Provenance, replay.Token!.Provenance);
        Assert.NotEqual(prepared.Token.LedgerToken, replay.Token.LedgerToken);
        Assert.Equal(1, prepared.Token!.Provenance.WorkflowCheckpointOrder);
        Assert.Equal(2, successor.Token!.Provenance.WorkflowCheckpointOrder);
        Assert.Equal(2, store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Reserved);
        Assert.Equal(0, store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Committed);
        Assert.All(store.ListLogicalCheckpointLedgerEntries(), entry => Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, entry.Status));
    }

    [Fact]
    public async Task Changed_canonical_input_conflicts_without_allocating_another_order()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var store = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: workflowStates);
        var first = NewCommit("commit-conflict", "checkpoint-a");
        var changed = first with { Checkpoint = first.Checkpoint with { Name = "Changed" } };

        await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(first));
        var result = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(changed));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, result.Status);
        Assert.Null(result.Token);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Reserved);
    }

    [Fact]
    public async Task Requested_context_source_and_operation_are_part_of_canonical_prepare_identity()
    {
        var store = NewStore();
        var commit = NewCommit("commit-context", "checkpoint-context");
        var context = Context(("b", "two"), ("a", "one"));
        var request = Request(commit, "source", "operation", context);

        var prepared = await store.PrepareAsync(request);
        var equivalent = await store.PrepareAsync(Request(commit, "source", "operation", Context(("a", "one"), ("b", "two"))));
        var changedContext = await store.PrepareAsync(Request(commit, "source", "operation", Context(("a", "changed"))));
        var changedSource = await store.PrepareAsync(Request(commit, "other-source", "operation", context));
        var changedOperation = await store.PrepareAsync(Request(commit, "source", "other-operation", context));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, prepared.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, equivalent.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, changedContext.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, changedSource.Status);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, changedOperation.Status);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(commit.WorkflowExecutionId).Reserved);
    }

    [Fact]
    public async Task Commit_prepared_enforces_token_and_enriched_fingerprint_and_advances_committed_high_watermark_once()
    {
        var workflowStates = new InMemoryWorkflowExecutionStateStore();
        var store = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: workflowStates);
        var raw = NewCommit("commit-final", "checkpoint-final");
        var prepared = await store.PrepareAsync(RuntimeCheckpointPrepareRequest.From(raw));
        var enriched = raw with { Checkpoint = raw.Checkpoint with { Provenance = prepared.Token!.Provenance } };
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);

        var committed = await store.CommitPreparedAsync(prepared.Token, enriched, decision);
        var replay = await store.CommitPreparedAsync(prepared.Token, enriched, decision);
        var changed = enriched with { Metadata = new Dictionary<string, string> { ["changed"] = "true" } };
        var conflict = await store.CommitPreparedAsync(prepared.Token, changed, decision);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, conflict.Status);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Committed);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Committed, Assert.Single(store.ListLogicalCheckpointLedgerEntries()).Status);
    }

    [Fact]
    public async Task Serialized_token_and_provenance_are_semantically_equal_for_commit_and_replay_recovery()
    {
        var store = NewStore();
        var raw = NewCommit("commit-recovery", "checkpoint-recovery");
        var prepared = await store.PrepareAsync(Request(raw, context: Context(("opaque", "value"))));
        var recoveredToken = RoundTrip(prepared.Token!);
        var recoveredCommit = RoundTrip(raw with
        {
            Checkpoint = raw.Checkpoint with { Provenance = recoveredToken.Provenance }
        });

        var committed = await store.CommitPreparedAsync(recoveredToken, recoveredCommit, Immediate);
        var replay = await store.CommitPreparedAsync(RoundTrip(recoveredToken), RoundTrip(recoveredCommit), Immediate);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Equal(recoveredToken.Provenance, RoundTrip(recoveredToken.Provenance));
    }

    [Fact]
    public async Task Stale_order_revision_conflicts_without_writing_a_marker_or_advancing_committed_order()
    {
        var store = NewStore();
        var first = NewCommit("commit-stale", "checkpoint-stale");
        var successor = NewCommit("commit-successor", "checkpoint-successor");
        var firstPrepared = await store.PrepareAsync(Request(first));
        await store.PrepareAsync(Request(successor));

        var result = await store.CommitPreparedAsync(firstPrepared.Token!, Attach(first, firstPrepared.Token!), Immediate);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, result.Status);
        Assert.Empty(store.ListCommits());
        Assert.Equal(0, store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Committed);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, store.ListLogicalCheckpointLedgerEntries().Single(x => x.CommitId == first.CommitId).Status);
    }

    [Fact]
    public async Task Repreparing_a_stale_reservation_refreshes_revisions_without_changing_order_and_allows_ordered_recovery()
    {
        var store = NewStore();
        var first = NewCommit("commit-recover-a", "checkpoint-recover-a");
        var second = NewCommit("commit-recover-b", "checkpoint-recover-b");
        var firstPrepared = await store.PrepareAsync(Request(first));
        var secondPrepared = await store.PrepareAsync(Request(second));
        var firstToken = firstPrepared.Token!;
        var secondToken = secondPrepared.Token!;

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await store.CommitPreparedAsync(firstToken, Attach(first, firstToken), Immediate)).Status);

        var firstRecovered = await store.PrepareAsync(Request(first));
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, firstRecovered.Status);
        Assert.Equal(firstToken.Provenance, firstRecovered.Token!.Provenance);
        Assert.NotEqual(firstToken.ExpectedOrderRevision, firstRecovered.Token.ExpectedOrderRevision);
        Assert.NotEqual(firstToken.LedgerToken, firstRecovered.Token.LedgerToken);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await store.CommitPreparedAsync(firstToken, Attach(first, firstToken), Immediate)).Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await store.CommitPreparedAsync(firstRecovered.Token, Attach(first, firstRecovered.Token), Immediate)).Status);

        var secondRecovered = await store.PrepareAsync(Request(second));
        Assert.Equal(secondToken.Provenance, secondRecovered.Token!.Provenance);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await store.CommitPreparedAsync(secondRecovered.Token, Attach(second, secondRecovered.Token), Immediate)).Status);
        Assert.Equal((2, 2), (
            store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Reserved,
            store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId).Committed));
    }

    [Fact]
    public async Task Repreparing_after_incompatible_context_commit_conflicts_without_allocating_an_order()
    {
        var store = NewStore();
        var first = NewCommit("commit-context-a", "checkpoint-context-a");
        var second = NewCommit("commit-context-b", "checkpoint-context-b");
        var firstPrepared = await store.PrepareAsync(Request(first, context: Context(("session", "a"))));
        var secondPrepared = await store.PrepareAsync(Request(second, context: Context(("session", "b"))));
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await store.CommitPreparedAsync(secondPrepared.Token!, Attach(second, secondPrepared.Token!), Immediate)).Status);

        var before = store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId);
        var recovered = await store.PrepareAsync(Request(first, context: Context(("session", "a"))));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, recovered.Status);
        Assert.Equal(before, store.GetCheckpointOrderHighWatermarks(first.WorkflowExecutionId));
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared,
            store.ListLogicalCheckpointLedgerEntries().Single(entry => entry.CommitId == first.CommitId).Status);
    }

    [Fact]
    public async Task Every_tampered_token_field_is_rejected_as_a_conflict()
    {
        var store = NewStore();
        var raw = NewCommit("commit-token", "checkpoint-token");
        var token = (await store.PrepareAsync(Request(raw))).Token!;
        var commit = Attach(raw, token);
        RuntimeCheckpointPreparationToken[] invalid =
        [
            token with { CommitId = "missing" },
            token with { LedgerToken = "wrong" },
            token with { Provenance = new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 99) },
            token with { ExpectedOrderRevision = token.ExpectedOrderRevision + 1 },
            token with { ExpectedContextRevision = token.ExpectedContextRevision + 1 },
            token with { ExpectedFence = new RuntimeExecutionFence("lease", "owner", 1) },
            token with { CanonicalInputFingerprint = "wrong" },
            token with { CanonicalInputReference = "wrong" },
            token with { InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred }
        ];

        foreach (var candidate in invalid)
            Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict, (await store.CommitPreparedAsync(candidate, commit, Immediate)).Status);
        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public async Task Skip_is_terminal_and_matching_retry_replays_without_a_commit_marker()
    {
        var store = NewStore();
        var raw = NewCommit("commit-skip", "checkpoint-skip");
        var token = (await store.PrepareAsync(Request(raw))).Token!;
        var commit = Attach(raw, token);
        var skip = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip);

        var skipped = await store.CommitPreparedAsync(token, commit, skip);
        var replay = await store.CommitPreparedAsync(RoundTrip(token), RoundTrip(commit), skip);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Skipped, skipped.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Empty(store.ListCommits());
        Assert.Equal(0, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Committed);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Skipped, Assert.Single(store.ListLogicalCheckpointLedgerEntries()).Status);
    }

    [Fact]
    public async Task Ownership_change_after_prepare_returns_ownership_lost_without_state_or_marker()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var now = DateTimeOffset.UtcNow;
        var firstLease = Lease("lease-1", "owner-1", 1, now);
        await liveness.SaveAsync(Ownership(firstLease));
        var store = NewStore(liveness);
        var raw = NewCommit("commit-owner", "checkpoint-owner") with { ExpectedFence = firstLease.ToFence() };
        var prepared = await store.PrepareAsync(Request(raw));
        await liveness.SaveAsync(Ownership(Lease("lease-2", "owner-2", 2, now)));

        var result = await store.CommitPreparedAsync(prepared.Token!, Attach(raw, prepared.Token!), Immediate);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.OwnershipLost, result.Status);
        Assert.Empty(store.ListCommits());
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, Assert.Single(store.ListLogicalCheckpointLedgerEntries()).Status);
    }

    [Fact]
    public async Task Repreparing_after_ownership_handoff_refreshes_the_fence_and_commits_with_the_original_order()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var now = DateTimeOffset.UtcNow;
        var firstLease = Lease("lease-handoff-1", "owner-handoff-1", 1, now);
        var successorLease = Lease("lease-handoff-2", "owner-handoff-2", 2, now);
        await liveness.SaveAsync(Ownership(firstLease));
        var store = NewStore(liveness);
        var firstCommit = NewCommit("commit-handoff", "checkpoint-handoff") with { ExpectedFence = firstLease.ToFence() };
        var prepared = await store.PrepareAsync(Request(firstCommit));
        await liveness.SaveAsync(Ownership(successorLease));
        var successorCommit = firstCommit with { ExpectedFence = successorLease.ToFence() };

        var recovered = await store.PrepareAsync(Request(successorCommit));
        var committed = await store.CommitPreparedAsync(recovered.Token!, Attach(successorCommit, recovered.Token!), Immediate);

        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, recovered.Status);
        Assert.Equal(prepared.Token!.Provenance, recovered.Token!.Provenance);
        Assert.Equal(successorLease.ToFence(), recovered.Token.ExpectedFence);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(firstCommit.WorkflowExecutionId).Reserved);
    }

    [Fact]
    public async Task Fenced_finalize_with_non_in_memory_ownership_store_fails_closed_without_mutation()
    {
        var now = DateTimeOffset.UtcNow;
        var lease = Lease("lease-external", "owner-external", 1, now);
        var liveness = new StubExecutionLivenessStateStore(Ownership(lease));
        var store = NewStore(liveness);
        var raw = NewCommit("commit-external", "checkpoint-external") with { ExpectedFence = lease.ToFence() };
        await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(
            () => store.PrepareAsync(Request(raw)).AsTask());

        Assert.Empty(store.ListCommits());
        Assert.Empty(store.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(0, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Committed);
    }

    [Fact]
    public async Task Preparation_exception_after_order_reservation_rolls_back_order_and_ledger_residue()
    {
        var store = NewStore();
        var raw = NewCommit("commit-prepare-rollback", "checkpoint-prepare-rollback");
        var primary = new InvalidOperationException("preparation failed after reservation");
        store.InjectPreparationFailureForTesting(primary);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => store.PrepareAsync(Request(raw)).AsTask());

        Assert.Same(primary, failure);
        Assert.Empty(store.ListLogicalCheckpointLedgerEntries());
        Assert.Empty(store.ListCommits());
        Assert.Equal((0L, 0L, 0L), store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId));

        var retry = await store.PrepareAsync(Request(raw));
        Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, retry.Status);
        Assert.Equal(1, retry.Token!.Provenance.WorkflowCheckpointOrder);
        Assert.Single(store.ListLogicalCheckpointLedgerEntries());
    }

    [Fact]
    public async Task Stale_fence_at_prepare_returns_ownership_lost_without_allocating_an_order()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var now = DateTimeOffset.UtcNow;
        await liveness.SaveAsync(Ownership(Lease("lease-current", "owner-current", 2, now)));
        var store = NewStore(liveness);
        var raw = NewCommit("commit-stale-owner", "checkpoint-stale-owner") with
        {
            ExpectedFence = new RuntimeExecutionFence("lease-stale", "owner-stale", 1)
        };

        var result = await store.PrepareAsync(Request(raw));

        Assert.Equal(RuntimeCheckpointPreparationStatus.OwnershipLost, result.Status);
        Assert.Empty(store.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(0, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Reserved);
    }

    [Fact]
    public async Task Existing_legacy_marker_reconciles_as_replay_without_rewriting_its_fingerprint()
    {
        var store = NewStore();
        var raw = NewCommit("commit-legacy", "checkpoint-legacy");
        var legacyFingerprint = RuntimeCheckpointCommitFingerprint.Compute(raw);
        await store.CommitAsync(raw, Immediate);

        var result = await new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), store).CommitAsync(raw);

        Assert.True(result.Succeeded);
        Assert.Equal(legacyFingerprint, RuntimeCheckpointCommitFingerprint.Compute(Assert.Single(store.ListCommits()).Commit));
        var ledger = Assert.Single(store.ListLogicalCheckpointLedgerEntries());
        Assert.True(ledger.ReconciledLegacyMarker);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Committed, ledger.Status);
        Assert.Equal(legacyFingerprint, ledger.CommitFingerprint);
    }

    [Fact]
    public async Task Marker_created_between_prepare_and_finalize_is_reconciled_atomically_as_replay()
    {
        var store = NewStore();
        var raw = NewCommit("commit-racing-marker", "checkpoint-racing-marker");
        var prepared = await store.PrepareAsync(Request(raw));
        var enriched = Attach(raw, prepared.Token!);
        await store.CommitAsync(enriched, Immediate);

        var replay = await store.CommitPreparedAsync(prepared.Token!, enriched, Immediate);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Single(store.ListCommits());
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Committed, Assert.Single(store.ListLogicalCheckpointLedgerEntries()).Status);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Committed);
    }

    [Fact]
    public async Task Enriched_missing_provider_failure_retains_recoverable_pre_enrichment_reservation()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var raw = NewCommit("commit-enriched-provider", "checkpoint-enriched-provider");
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [new MissingBookmarkProviderEnricher()]);

        await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(() => committer.CommitAsync(raw).AsTask());

        var prepared = Assert.Single(store.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, prepared.Status);
        Assert.Equal(1, prepared.Provenance.WorkflowCheckpointOrder);
        Assert.Empty(store.ListCommits());
        Assert.Equal((1L, 0L, 1L), store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId));

        var replay = await store.PrepareAsync(Request(raw));
        var refreshed = Assert.Single(store.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, replay.Status);
        Assert.Equal(prepared.Provenance, replay.Token!.Provenance);
        Assert.Equal(1, replay.Token.Provenance.WorkflowCheckpointOrder);
        Assert.NotEqual(prepared.LedgerToken, refreshed.LedgerToken);
        Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, refreshed.Status);
        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public async Task Changed_input_against_a_reconciled_legacy_marker_conflicts_without_new_order()
    {
        var store = NewStore();
        var raw = NewCommit("commit-legacy-conflict", "checkpoint-legacy-conflict");
        await store.CommitAsync(raw, Immediate);
        Assert.Equal(RuntimeCheckpointPreparationStatus.Replay, (await store.PrepareAsync(Request(raw))).Status);
        var before = store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId);
        var changed = raw with { Metadata = new Dictionary<string, string> { ["changed"] = "true" } };

        var conflict = await store.PrepareAsync(Request(changed));

        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, conflict.Status);
        Assert.Equal(before, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId));
        Assert.Single(store.ListCommits());
    }

    [Fact]
    public async Task Preparation_freezes_caller_owned_raw_collections_for_recovery()
    {
        var activityIds = new List<string> { "activity-original" };
        var checkpointMetadata = new Dictionary<string, string> { ["checkpoint"] = "original" };
        var commitMetadata = new Dictionary<string, string> { ["commit"] = "original" };
        var intents = new List<RuntimePostCommitIntent>();
        var raw = NewCommit("commit-frozen", "checkpoint-frozen") with
        {
            Checkpoint = new RuntimeCheckpoint(
                "checkpoint-frozen",
                "Checkpoint",
                "workflow-preparation",
                DateTimeOffset.UnixEpoch,
                activityIds,
                checkpointMetadata),
            PostCommitIntents = intents,
            Metadata = commitMetadata
        };
        var store = NewStore();
        var prepared = await store.PrepareAsync(Request(raw));
        var frozen = Assert.Single(store.ListLogicalCheckpointLedgerEntries());
        var frozenFingerprint = frozen.InputFingerprint;
        var frozenCanonicalPayload = frozen.CanonicalInputPayload;
        var frozenPayload = JsonSerializer.Serialize(new { frozen.RawCheckpoint, frozen.RawPostCommitIntents, frozen.RawCommitMetadata });

        activityIds.Add("activity-mutated");
        checkpointMetadata["checkpoint"] = "mutated";
        commitMetadata["commit"] = "mutated";
        intents.Add(new RuntimePostCommitIntent(
            "intent-mutated",
            raw.WorkflowExecutionId,
            "Mutated",
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null));

        var after = Assert.Single(store.ListLogicalCheckpointLedgerEntries());
        Assert.Equal(frozenFingerprint, after.InputFingerprint);
        Assert.Equal(frozenCanonicalPayload, after.CanonicalInputPayload);
        Assert.Equal(frozenPayload, JsonSerializer.Serialize(new { after.RawCheckpoint, after.RawPostCommitIntents, after.RawCommitMetadata }));
        Assert.Equal(RuntimeCheckpointPreparationStatus.Conflict, (await store.PrepareAsync(Request(raw))).Status);
        Assert.Equal(prepared.Token!.Provenance, after.Provenance);
        Assert.Equal(1, store.GetCheckpointOrderHighWatermarks(raw.WorkflowExecutionId).Reserved);
    }

    [Fact]
    public async Task Committer_translates_prepare_and_finalize_protocol_failures()
    {
        var commit = NewCommit("commit-outcome", "checkpoint-outcome");

        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => CommitWithAsync(new OutcomeStore(RuntimeCheckpointPreparationStatus.Conflict), commit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommitWithAsync(new OutcomeStore(RuntimeCheckpointPreparationStatus.OwnershipLost), commit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommitWithAsync(new OutcomeStore(RuntimeCheckpointPreparationStatus.Prepared, omitToken: true), commit));
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => CommitWithAsync(new OutcomeStore(finalStatus: RuntimeCheckpointCommitStoreStatus.Conflict), commit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommitWithAsync(new OutcomeStore(finalStatus: RuntimeCheckpointCommitStoreStatus.OwnershipLost), commit));
    }

    [Fact]
    public void Legacy_checkpoint_fingerprint_is_frozen()
    {
        Assert.Equal(
            "16f4918b32dd2e22bb32d31936a5f14ee5ad6be773d62609038ca6f32d30bcf6",
            RuntimeCheckpointCommitFingerprint.Compute(NewCommit("commit-legacy-hash", "checkpoint-legacy-hash")));
    }

    [Fact]
    public void Commit_fingerprint_includes_full_generic_provenance()
    {
        var raw = NewCommit("commit-fingerprint", "checkpoint-fingerprint");
        var first = raw with { Checkpoint = raw.Checkpoint with { Provenance = new(RuntimeExecutionContextSnapshot.Empty, 1) } };
        var second = raw with { Checkpoint = raw.Checkpoint with { Provenance = new(RuntimeExecutionContextSnapshot.Empty, 2) } };
        var withContext = raw with
        {
            Checkpoint = raw.Checkpoint with
            {
                Provenance = new(new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["domain"] = "opaque" }), 1)
            }
        };

        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(first), RuntimeCheckpointCommitFingerprint.Compute(second));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(first), RuntimeCheckpointCommitFingerprint.Compute(withContext));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeInput(first), RuntimeCheckpointCommitFingerprint.ComputeInput(second));
    }

    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    private static InMemoryRuntimeCheckpointCommitStore NewStore(IExecutionLivenessStateStore? liveness = null) =>
        new(operationalStateStore: liveness);

    private static RuntimeCheckpointPrepareRequest Request(
        RuntimeCheckpointCommit commit,
        string source = "Checkpoint",
        string? operation = null,
        RuntimeExecutionContextSnapshot? context = null) =>
        new(commit, source, operation ?? commit.Checkpoint.CheckpointId, context ?? RuntimeExecutionContextSnapshot.Empty);

    private static RuntimeExecutionContextSnapshot Context(params (string Key, string Value)[] entries) =>
        new(RuntimeExecutionContextSnapshot.CurrentVersion, entries.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static RuntimeCheckpointCommit Attach(RuntimeCheckpointCommit commit, RuntimeCheckpointPreparationToken token) =>
        commit with { Checkpoint = commit.Checkpoint with { Provenance = token.Provenance } };

    private static T RoundTrip<T>(T value) where T : notnull =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static RuntimeExecutionLease Lease(string leaseId, string ownerId, long token, DateTimeOffset now) =>
        new(leaseId, "workflow-preparation", ownerId, now.AddMinutes(-1), now.AddMinutes(10), token);

    private static ExecutionLivenessState Ownership(RuntimeExecutionLease lease) =>
        new(
            "ownership:workflow-preparation",
            "workflow-preparation",
            lease,
            null,
            null,
            null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.OwnershipFencingToken] = lease.FencingToken.ToString(CultureInfo.InvariantCulture)
            });

    private static Task CommitWithAsync(IRuntimeCheckpointCommitStore store, RuntimeCheckpointCommit commit) =>
        new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), store).CommitAsync(commit).AsTask();

    private static RuntimeCheckpointCommit NewCommit(string commitId, string checkpointId) =>
        new(
            commitId,
            new RuntimeCheckpoint(checkpointId, "Checkpoint", "workflow-preparation", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

    private sealed class OutcomeStore(
        RuntimeCheckpointPreparationStatus prepareStatus = RuntimeCheckpointPreparationStatus.Prepared,
        bool omitToken = false,
        RuntimeCheckpointCommitStoreStatus finalStatus = RuntimeCheckpointCommitStoreStatus.Committed) : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default)
        {
            if (prepareStatus == RuntimeCheckpointPreparationStatus.Conflict)
                return ValueTask.FromResult(RuntimeCheckpointPreparationResult.Conflict());
            if (prepareStatus == RuntimeCheckpointPreparationStatus.OwnershipLost)
                return ValueTask.FromResult(RuntimeCheckpointPreparationResult.OwnershipLost());
            if (omitToken)
                return ValueTask.FromResult(new RuntimeCheckpointPreparationResult(RuntimeCheckpointPreparationStatus.Prepared, null));
            var token = new RuntimeCheckpointPreparationToken(
                request.Commit.CommitId,
                "ledger",
                new RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot.Empty, 1),
                1,
                0,
                request.Commit.ExpectedFence,
                new RuntimeCheckpointPreparationPayloadCodec().Encode(request).PayloadSha256,
                "canonical",
                RuntimeCheckpointPersistenceMode.Immediate);
            return ValueTask.FromResult(RuntimeCheckpointPreparationResult.Prepared(token));
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]) { Status = finalStatus });

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
    }

    private sealed class MissingBookmarkProviderEnricher : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default)
        {
            var bookmark = new BookmarkState(
                "bookmark-enriched",
                commit.WorkflowExecutionId,
                "activity-enriched",
                "node-enriched",
                "resume-enriched",
                "test",
                "sha256:test",
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UnixEpoch,
                null);
            var changes = commit.StateChanges;
            var enriched = new RuntimeCheckpointStateChangeSet(
                changes.WorkflowExecution,
                changes.Scheduler,
                changes.ActivityExecutions,
                [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())],
                changes.DurableValues,
                changes.Incidents,
                changes.Operational,
                changes.WorkflowDispatches,
                changes.ActivityExecutionInspections,
                changes.PostCommitOutbox,
                changes.ActivityScopeCleanups,
                changes.WorkflowDispatchCancellations,
                changes.ConsumedSchedulerWorkItems,
                changes.AlterationJobTerminalChange);
            return ValueTask.FromResult(commit with { StateChanges = enriched });
        }
    }

    private sealed class StubExecutionLivenessStateStore(ExecutionLivenessState state) : IExecutionLivenessStateStore
    {
        private ExecutionLivenessState _state = state;

        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = value;
            return ValueTask.FromResult(value);
        }

        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
            ExecutionLivenessState value,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = value;
            return ValueTask.FromResult(new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.Saved, 1));
        }

        public ValueTask<ExecutionLivenessState?> FindAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ExecutionLivenessState?>(_state);
        }

        public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
            string workflowExecutionId,
            string operationalStateId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<VersionedExecutionLivenessState?>(new VersionedExecutionLivenessState(_state, 1));
        }
    }
}
