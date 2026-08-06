using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Groundwork.Documents.Store;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_adoption_operates_on_real_durable_prepared_rows_and_cannot_be_a_no_op(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2);
        var writer = fixture.Writer;
        var target = new RuntimeExecutionFence($"groundwork-{route}", "adoption", 2);
        await fixture.ActivateFenceAsync(target);
        var before = await SnapshotPreparedAsync(writer);
        var beforeNormalized = fixture.NormalizedRawSnapshot();
        var members = before.Reservations.Select(reservation => new RuntimeCheckpointPreparedAdoptionMember(
            reservation.CommitId,
            reservation.Token.LedgerToken,
            reservation.Provenance.WorkflowCheckpointOrder,
            reservation.InputFingerprint,
            reservation.Token.CanonicalInputReference,
            reservation.ExpectedFence,
            reservation.ExpectedOrderRevision,
            reservation.ExpectedContextRevision,
            reservation.RecoveryAuthority,
            reservation.CurrentAuthorityFence,
            reservation.AuthorityRevision)).ToArray();
        var request = new RuntimeCheckpointPreparedAdoptionRequest("wf-1", route, members[^1].WorkflowCheckpointOrder, target, members);

        var receipt = await InvokeAdoptionAsync(writer, request);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted, receipt.Status);
        var adopted = await SnapshotPreparedAsync(writer);
        Assert.Equal(before.ImmutablePreparedBytes, adopted.ImmutablePreparedBytes);
        Assert.Equal(beforeNormalized, fixture.NormalizedRawSnapshot());
        Assert.Equal(before.Reservations.Select(x => x with { CurrentAuthorityFence = target, AuthorityRevision = x.AuthorityRevision + 1 }), adopted.Reservations);

        var replayRequest = request with
        {
            Members = adopted.Reservations.Select(reservation => new RuntimeCheckpointPreparedAdoptionMember(
                reservation.CommitId, reservation.Token.LedgerToken, reservation.Provenance.WorkflowCheckpointOrder,
                reservation.InputFingerprint, reservation.Token.CanonicalInputReference, reservation.ExpectedFence,
                reservation.ExpectedOrderRevision, reservation.ExpectedContextRevision, reservation.RecoveryAuthority,
                reservation.CurrentAuthorityFence, reservation.AuthorityRevision)).ToArray()
        };
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay, (await InvokeAdoptionAsync(writer, replayRequest)).Status);
        var replayed = await SnapshotPreparedAsync(writer);
        Assert.Equal(adopted.Reservations, replayed.Reservations);
        Assert.Equal(adopted.ImmutablePreparedBytes, replayed.ImmutablePreparedBytes);
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_adopted_current_fence_rejects_older_or_different_ownership_without_raw_mutation(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2);
        var current = new RuntimeExecutionFence("lease-current", "owner-current", 5);
        await fixture.ActivateFenceAsync(current);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(current))).Status);
        var adoptedAtFive = fixture.RawSnapshot();
        var currentMembers = await fixture.ReadMembersAsync();
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(current, currentMembers))).Status);
        Assert.Equal(adoptedAtFive, fixture.RawSnapshot());

        var newer = new RuntimeExecutionFence("lease-current", "owner-current", 6);
        await fixture.ActivateFenceAsync(newer);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(newer, currentMembers))).Status);
        var adoptedAtSix = fixture.RawSnapshot();
        var newerMembers = await fixture.ReadMembersAsync();

        RuntimeExecutionFence[] rejectedTargets =
        [
            new("lease-current", "owner-current", 5),
            new("lease-current", "owner-current", 4),
            new("lease-current", "owner-other", 7),
            new("lease-other", "owner-current", 7)
        ];
        foreach (var rejected in rejectedTargets)
        {
            var receipt = await InvokeAdoptionAsync(fixture.Writer, fixture.Request(rejected, newerMembers));
            Assert.True(receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost);
            Assert.Equal(adoptedAtSix, fixture.RawSnapshot());
        }
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_successive_active_owners_with_higher_global_tokens_adopt_revision_two(
        RuntimeCheckpointRecoveryRoute route)
    {
        var now = DateTimeOffset.UtcNow;
        var original = new RuntimeExecutionLease("lease-a", "wf-1", "owner-a", now, now.AddHours(1), 1);
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2, originalLease: original);
        var ownerB = new RuntimeExecutionFence("lease-b", "owner-b", 2);
        await fixture.ActivateFenceAsync(ownerB);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(ownerB))).Status);

        var revisionTwo = await fixture.ReadMembersAsync();
        var ownerC = new RuntimeExecutionFence("lease-c", "owner-c", 3);
        await fixture.ActivateFenceAsync(ownerC);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(ownerC, revisionTwo))).Status);
        Assert.All(await fixture.ReadMembersAsync(), member =>
        {
            Assert.Equal(ownerC, member.ExpectedCurrentAuthorityFence);
            Assert.Equal(3, member.ExpectedAuthorityRevision);
        });
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_replay_needs_no_active_old_lease_and_forged_higher_target_is_byte_identical(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2);
        var adoptedFence = GroundworkFence("adopted", 2);
        await fixture.ActivateFenceAsync(adoptedFence);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(adoptedFence))).Status);
        var members = await fixture.ReadMembersAsync();

        var now = DateTimeOffset.UtcNow;
        await SaveOwnershipAsync(fixture.Documents, new RuntimeExecutionLease(
            adoptedFence.LeaseId, "wf-1", adoptedFence.OwnerId, now.AddHours(-2), now.AddHours(-1), adoptedFence.FencingToken));
        var beforeReplay = fixture.RawSnapshot();
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(adoptedFence, members))).Status);
        Assert.Equal(beforeReplay, fixture.RawSnapshot());

        var forged = GroundworkFence("forged", 3);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(forged, members))).Status);
        Assert.Equal(beforeReplay, fixture.RawSnapshot());
    }

    [Theory]
    [MemberData(nameof(GroundworkRejectedExactSetCases))]
    public async Task Groundwork_exact_set_adoption_rejects_provider_owned_mismatches_with_raw_documents_unchanged(
        RuntimeCheckpointRecoveryRoute route,
        string name,
        Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> buildRequest)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 3);
        var before = fixture.RawSnapshot();
        var receipt = await InvokeAdoptionAsync(fixture.Writer, buildRequest(fixture));

        Assert.True(
            receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost,
            $"Expected conflict or ownership loss for '{name}' ({route}), but received {receipt.Status}.");
        Assert.Equal(before, fixture.RawSnapshot());
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_cancellation_and_mid_transaction_document_failure_roll_back_every_raw_document(RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 3);
        var before = fixture.RawSnapshot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeAdoptionAsync(fixture.Writer, fixture.Request(GroundworkFence("cancelled", 2)), cancellation.Token));
        Assert.Equal(before, fixture.RawSnapshot());

        var failedTarget = GroundworkFence("failed", 2);
        await fixture.ActivateFenceAsync(failedTarget);
        var beforeFailure = fixture.RawSnapshot();
        var saves = 0;
        fixture.Interceptor.OnSaveResult = _ => ++saves == 2
            ? throw new InvalidOperationException("mid-adoption-document-write")
            : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAdoptionAsync(fixture.Writer, fixture.Request(failedTarget)));

        Assert.True(saves >= 2, "The injected failure must occur after at least one staged document write.");
        Assert.Equal(beforeFailure, fixture.RawSnapshot());
    }

    [Fact]
    public async Task Groundwork_source_free_fold_rejects_stale_binding_and_current_rebuild_succeeds()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(RuntimeCheckpointRecoveryRoute.SourceFree, 2);
        var firstTarget = new RuntimeExecutionFence("lease-recovery", "recovery", 1);
        var finalTarget = new RuntimeExecutionFence(firstTarget.LeaseId, firstTarget.OwnerId, 2);
        var staleFold = await fixture.CreateFoldRequestAsync(finalTarget);
        await fixture.ActivateFenceAsync(firstTarget);

        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await fixture.Writer.AdoptPreparedAsync(fixture.Request(firstTarget))).Status);
        var afterAdoption = fixture.RawSnapshot();

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await fixture.Writer.CommitPreparedFoldAsync(staleFold)).Status);
        Assert.Equal(afterAdoption, fixture.RawSnapshot());

        var currentFold = await fixture.CreateFoldRequestAsync(finalTarget);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await fixture.Writer.CommitPreparedFoldAsync(currentFold)).Status);
        Assert.Empty((await fixture.Writer.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery("wf-1", 250))).Reservations);
    }

    [Fact]
    public async Task Groundwork_source_free_fold_uses_different_active_successor_while_retaining_original_fence_identity()
    {
        var now = DateTimeOffset.UtcNow;
        var original = new RuntimeExecutionLease(
            "lease-original", "wf-1", "owner-original", now, now.AddHours(1), 7);
        var successor = new RuntimeExecutionLease(
            "lease-successor", "wf-1", "owner-successor", now, now.AddHours(1), 8);
        var fixture = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree,
            2,
            originalLease: original);
        await SaveOwnershipAsync(fixture.Documents, successor);
        var request = await fixture.CreateFoldRequestAsync(successor.ToFence());

        var result = await fixture.Writer.CommitPreparedFoldAsync(request);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, result.Status);
        var terminal = fixture.ReadLedgerEntries()
            .Where(entry => request.Members.Any(member => member.CommitId == entry.CommitId))
            .ToArray();
        Assert.All(terminal, entry =>
        {
            Assert.Equal(original.ToFence(), entry.TerminalPreparationToken!.ExpectedFence);
            Assert.Equal(successor.ToFence(), entry.CurrentAuthorityFence);
            Assert.Equal(2, entry.AuthorityRevision);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Groundwork_hidden_prepare_racing_exact_membership_conflicts_without_mutating_requested_rows(bool fold)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(RuntimeCheckpointRecoveryRoute.SourceFree, 2);
        var before = await SnapshotPreparedAsync(fixture.Writer);
        var target = new RuntimeExecutionFence("lease-race", "race", 1);
        fixture.Interceptor.OnBeforeBegin = async _ =>
        {
            var concurrentWriter = CreateWriter(fixture.Documents);
            Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared,
                (await concurrentWriter.PrepareAsync(RuntimeCheckpointPrepareRequest.From(
                    BuildCommit(fold ? "hidden-during-fold" : "hidden-during-adoption")))).Status);
        };

        if (fold)
        {
            var request = await fixture.CreateFoldRequestAsync(target);
            Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
                (await fixture.Writer.CommitPreparedFoldAsync(request)).Status);
        }
        else
        {
            Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Conflict,
                (await fixture.Writer.AdoptPreparedAsync(fixture.Request(target))).Status);
        }

        var after = await SnapshotPreparedAsync(fixture.Writer);
        Assert.Equal(before.Reservations, after.Reservations.Take(before.Reservations.Length));
        Assert.Equal(before.ImmutablePreparedBytes,
            JsonSerializer.Serialize(after.Reservations.Take(before.Reservations.Length).Select(reservation => new
            {
                reservation.Status,
                reservation.Token.CommitId,
                reservation.Token.LedgerToken,
                reservation.Provenance,
                reservation.Token.CanonicalInputReference,
                reservation.Token.CanonicalInputFingerprint,
                reservation.ExpectedFence,
                reservation.ExpectedOrderRevision,
                reservation.ExpectedContextRevision,
                reservation.RecoveryAuthority,
                reservation.CanonicalEnvelope
            })));
        Assert.Equal(before.Reservations.Length + 1, after.Reservations.Length);
    }

    [Fact]
    public async Task Groundwork_fold_commits_skips_and_fails_exact_members_with_receipts_watermark_compaction_and_replay()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, includeFoldOutbox: true);
        var target = new RuntimeExecutionFence("lease-fold", "fold", 1);
        var request = await fixture.CreateMixedFoldRequestAsync(target);

        var committed = await fixture.Writer.CommitPreparedFoldAsync(request);

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Status);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed, committed.Receipts[request.Members[0].CommitId].Status);
        Assert.Single(committed.Receipts[request.Members[0].CommitId].PendingPostCommitWorkIds);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Skipped, committed.Receipts[request.Members[1].CommitId].Status);
        Assert.Equal(
            request.Members[1].PreparedCommit!.VerifiedCommitFingerprint,
            committed.Receipts[request.Members[1].CommitId].CommitFingerprint);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Failed, committed.Receipts[request.Members[2].CommitId].Status);
        Assert.Equal("trusted-terminal", committed.Receipts[request.Members[2].CommitId].FailureCode);
        Assert.Empty((await fixture.Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250))).Reservations);

        var ledgers = fixture.ReadLedgerEntries();
        Assert.Equal(
            [RuntimeLogicalCheckpointLedgerStatus.Committed, RuntimeLogicalCheckpointLedgerStatus.Skipped, RuntimeLogicalCheckpointLedgerStatus.Failed],
            ledgers.Where(entry => request.Members.Any(member => member.CommitId == entry.CommitId))
                .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder).Select(entry => entry.Status));
        Assert.All(ledgers.Where(entry => request.Members.Any(member => member.CommitId == entry.CommitId)), entry =>
        {
            Assert.Null(entry.RawCheckpoint);
            Assert.Null(entry.RawStateChanges);
            Assert.Null(entry.CanonicalInputPayload);
            Assert.NotNull(entry.TerminalPreparationToken);
            Assert.Equal(target, entry.CurrentAuthorityFence);
            Assert.Equal(2, entry.AuthorityRevision);
        });
        Assert.NotNull(await fixture.Documents.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, request.Members[0].CommitId));
        Assert.Null(await fixture.Documents.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, request.Members[1].CommitId));
        Assert.Null(await fixture.Documents.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, request.Members[2].CommitId));
        var bookmark = await new GroundworkBookmarkStateStore(
            fixture.Documents,
            GroundworkTestSerialization.Serializer).FindAsync("wf-1", "bm-1");
        Assert.Equal("fold-committed", bookmark!.ExecutableNodeId);
        Assert.Equal(request.MaxWorkflowCheckpointOrder, fixture.ReadCommittedOrder());

        var replay = await fixture.Writer.CommitPreparedFoldAsync(request);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Replay, replay.Status);
        Assert.Equal(committed.Receipts, replay.Receipts);
    }

    [Fact]
    public async Task Groundwork_fold_rejects_omitted_committed_and_injected_noncommitted_scope_cleanups_byte_identically()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, includeFoldOutbox: true, includeFoldScopeCleanups: true);
        var request = await fixture.CreateMixedFoldRequestAsync(GroundworkFence("effects", 2));
        var before = fixture.RawSnapshot();

        Assert.Single(request.FoldedStateChanges.ActivityScopeCleanups);
        var omitted = request with { FoldedStateChanges = WithActivityScopeCleanups(request.FoldedStateChanges, []) };
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await fixture.Writer.CommitPreparedFoldAsync(omitted)).Status);
        Assert.Equal(before, fixture.RawSnapshot());

        var skippedCleanups = request.Members[1].PreparedCommit!.Commit.StateChanges.ActivityScopeCleanups;
        Assert.NotEmpty(skippedCleanups);
        var injected = request with
        {
            FoldedStateChanges = WithActivityScopeCleanups(
                request.FoldedStateChanges,
                request.FoldedStateChanges.ActivityScopeCleanups.Concat(skippedCleanups).ToArray())
        };
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await fixture.Writer.CommitPreparedFoldAsync(injected)).Status);
        Assert.Equal(before, fixture.RawSnapshot());
    }

    private static RuntimeCheckpointStateChangeSet WithActivityScopeCleanups(
        RuntimeCheckpointStateChangeSet stateChanges,
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups) =>
        new(
            stateChanges.WorkflowExecution,
            stateChanges.Scheduler,
            stateChanges.ActivityExecutions,
            stateChanges.Bookmarks,
            stateChanges.DurableValues,
            stateChanges.Incidents,
            stateChanges.Operational,
            stateChanges.WorkflowDispatches,
            stateChanges.ActivityExecutionInspections,
            stateChanges.PostCommitOutbox,
            cleanups,
            stateChanges.WorkflowDispatchCancellations,
            stateChanges.ConsumedSchedulerWorkItems,
            stateChanges.AlterationJobTerminalChange);

    [Fact]
    public async Task Groundwork_recovery_authority_tampering_conflicts_for_single_and_fold_without_mutation()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(RuntimeCheckpointRecoveryRoute.SourceBound, 1);
        var reservation = Assert.Single((await fixture.Writer.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery("wf-1", 250))).Reservations);
        var prepared = await new RuntimeCheckpointPreparationReplayer(
            new ImmediateRuntimeCheckpointPersistencePolicy(), [], []).RehydrateAsync(reservation);
        var tamperedToken = prepared.Token with { RecoveryAuthority = GroundworkAuthority("tampered") };
        var before = fixture.RawSnapshot();

        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await fixture.Writer.CommitPreparedAsync(tamperedToken, prepared.Commit, prepared.Decision)).Status);
        Assert.Equal(before, fixture.RawSnapshot());

        var tamperedPrepared = prepared with { Token = tamperedToken };
        var member = new RuntimeCheckpointPreparedFoldMember(
            tamperedToken,
            RuntimeCheckpointPreparedDisposition.Committed,
            reservation.CurrentAuthorityFence,
            reservation.AuthorityRevision,
            tamperedPrepared);
        var fold = new RuntimeCheckpointPreparedFoldRequest(
            "wf-1",
            [member],
            member.WorkflowCheckpointOrder,
            RuntimeCheckpointFold.FoldPrepared([tamperedPrepared]),
            reservation.CurrentAuthorityFence);
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Conflict,
            (await fixture.Writer.CommitPreparedFoldAsync(fold)).Status);
        Assert.Equal(before, fixture.RawSnapshot());
    }

    [Fact]
    public async Task Groundwork_mixed_fold_uses_last_committed_context_and_all_noncommitted_retains_current_context()
    {
        RuntimeExecutionContextSnapshot[] contexts =
        [
            new(1, new Dictionary<string, string> { ["member"] = "committed" }),
            new(1, new Dictionary<string, string> { ["member"] = "skipped" }),
            new(1, new Dictionary<string, string> { ["member"] = "failed" })
        ];

        var mixed = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, preparedContexts: contexts);
        var mixedRequest = await mixed.CreateMixedFoldRequestAsync(GroundworkFence("mixed-context", 2));
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await mixed.Writer.CommitPreparedFoldAsync(mixedRequest)).Status);
        Assert.Equal(contexts[0], (await LoadCoordinationAsync(mixed.Documents, "wf-1")).ExecutionContext);

        var noncommitted = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, preparedContexts: contexts);
        var before = await LoadCoordinationAsync(noncommitted.Documents, "wf-1");
        var noncommittedRequest = await noncommitted.CreateAllNoncommittedFoldRequestAsync(
            GroundworkFence("noncommitted-context", 2));
        Assert.Equal(RuntimeCheckpointCommitStoreStatus.Committed,
            (await noncommitted.Writer.CommitPreparedFoldAsync(noncommittedRequest)).Status);
        var after = await LoadCoordinationAsync(noncommitted.Documents, "wf-1");
        Assert.Equal(before.ExecutionContext, after.ExecutionContext);
        Assert.Equal(before.ContextRevision, after.ContextRevision);
    }

    [Fact]
    public async Task Groundwork_fold_mid_transaction_failure_rolls_back_all_documents()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, includeFoldOutbox: true);
        var request = await fixture.CreateMixedFoldRequestAsync(new RuntimeExecutionFence("lease-fold-failure", "fold", 1));
        var before = fixture.RawSnapshot();
        var saves = 0;
        fixture.Interceptor.OnSaveResult = _ => ++saves == 2
            ? throw new InvalidOperationException("mid-fold-write")
            : null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Writer.CommitPreparedFoldAsync(request).AsTask());

        Assert.True(saves >= 2);
        Assert.Equal(before, fixture.RawSnapshot());
    }

    [Fact]
    public async Task Groundwork_fold_cancellation_after_a_staged_mutation_rolls_back_all_documents()
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(
            RuntimeCheckpointRecoveryRoute.SourceFree, 3, includeFoldOutbox: true);
        var request = await fixture.CreateMixedFoldRequestAsync(
            new RuntimeExecutionFence("lease-fold-cancel", "fold", 1));
        var before = fixture.RawSnapshot();
        using var cancellation = new CancellationTokenSource();
        var saves = 0;
        fixture.Interceptor.OnSaveResult = _ =>
        {
            if (++saves == 2)
                cancellation.Cancel();
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Writer.CommitPreparedFoldAsync(request, cancellation.Token).AsTask());

        Assert.True(saves >= 2, "Cancellation must occur after at least one document mutation was staged.");
        Assert.Equal(before, fixture.RawSnapshot());
    }

    private static IEnumerable<(string Name, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> Build)> GroundworkRejectedExactSetRequests()
    {
        yield return ("missing member", fixture => fixture.Request(GroundworkFence("missing", 2), fixture.Members[..1]));
        yield return ("extra member", fixture => fixture.Request(GroundworkFence("extra", 2), [.. fixture.Members, fixture.Members[0] with { CommitId = "extra" }]));
        yield return ("duplicate member", fixture => fixture.Request(GroundworkFence("duplicate", 2), [.. fixture.Members, fixture.Members[0]]));
        yield return ("partial exact set", fixture => fixture.Request(GroundworkFence("partial", 2), fixture.Members[..2]));
        yield return ("out of order members", fixture => fixture.Request(GroundworkFence("order", 2), fixture.Members.Reverse().ToArray()));
        yield return ("mixed workflow", fixture => fixture.Request(GroundworkFence("workflow", 2)) with { WorkflowExecutionId = "wf-other" });
        yield return ("mixed original authority", fixture => fixture.Request(GroundworkFence("authority", 2), [fixture.Members[0] with { RecoveryAuthority = GroundworkAuthority("work-other") }, .. fixture.Members[1..]]));
        yield return ("mixed current fence", fixture => fixture.Request(GroundworkFence("current", 2), [fixture.Members[0] with { ExpectedCurrentAuthorityFence = GroundworkFence("wrong-current", 1) }, .. fixture.Members[1..]]));
        yield return ("ledger-token mismatch", fixture => fixture.Request(GroundworkFence("token", 2), [fixture.Members[0] with { LedgerToken = "wrong-token" }, .. fixture.Members[1..]]));
        yield return ("canonical digest mismatch", fixture => fixture.Request(GroundworkFence("digest", 2), [fixture.Members[0] with { CanonicalInputReference = "wrong-digest-reference" }, .. fixture.Members[1..]]));
        yield return ("original order revision mismatch", fixture => fixture.Request(GroundworkFence("order-revision", 2), [fixture.Members[0] with { OriginalOrderRevision = fixture.Members[0].OriginalOrderRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("original context revision mismatch", fixture => fixture.Request(GroundworkFence("context-revision", 2), [fixture.Members[0] with { OriginalContextRevision = fixture.Members[0].OriginalContextRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("authority revision mismatch", fixture => fixture.Request(GroundworkFence("authority-revision", 2), [fixture.Members[0] with { ExpectedAuthorityRevision = fixture.Members[0].ExpectedAuthorityRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("canonical fingerprint mismatch", fixture => fixture.Request(GroundworkFence("fingerprint", 2), [fixture.Members[0] with { CanonicalInputFingerprint = "sha256:wrong" }, .. fixture.Members[1..]]));
        yield return ("hidden prepared-set gap", fixture => fixture.Request(GroundworkFence("gap", 2), [fixture.Members[0], fixture.Members[2]]));
    }

    public static TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>> GroundworkRejectedExactSetCases
    {
        get
        {
            var data = new TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>>();
            foreach (var route in new[] { RuntimeCheckpointRecoveryRoute.SourceBound, RuntimeCheckpointRecoveryRoute.SourceFree })
            foreach (var (name, build) in GroundworkRejectedExactSetRequests())
                data.Add(route, name, build);
            return data;
        }
    }

    private static RuntimeExecutionFence GroundworkFence(string owner, long token) =>
        new($"lease-{owner}", owner, token);

    private static RuntimeCheckpointRecoveryAuthority GroundworkAuthority(string workItemId) =>
        new(1, "runtime.scheduler-work", "wf-1", workItemId, $"sha256:{workItemId}");

    public sealed class GroundworkAdoptionFixture
    {
        private GroundworkAdoptionFixture(
            InMemoryDocumentStore documents,
            InterceptingDocumentStore interceptor,
            GroundworkRuntimeCheckpointWriter writer,
            RuntimeCheckpointRecoveryRoute route,
            RuntimeCheckpointPreparedAdoptionMember[] members)
        {
            Documents = documents;
            Interceptor = interceptor;
            Writer = writer;
            Route = route;
            Members = members;
        }

        public InMemoryDocumentStore Documents { get; }
        internal InterceptingDocumentStore Interceptor { get; }
        public GroundworkRuntimeCheckpointWriter Writer { get; }
        public RuntimeCheckpointRecoveryRoute Route { get; }
        public RuntimeCheckpointPreparedAdoptionMember[] Members { get; }

        public static async Task<GroundworkAdoptionFixture> CreateAsync(
            RuntimeCheckpointRecoveryRoute route,
            int entryCount,
            bool includeFoldOutbox = false,
            bool includeFoldScopeCleanups = false,
            RuntimeExecutionLease? originalLease = null,
            IReadOnlyList<RuntimeExecutionContextSnapshot>? preparedContexts = null)
        {
            if (preparedContexts is not null && preparedContexts.Count != entryCount)
                throw new ArgumentException("Prepared context count must match the requested entry count.", nameof(preparedContexts));
            var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
            var interceptor = new InterceptingDocumentStore(documents);
            var writer = CreateWriter(interceptor);

            // Seed every durability family before adoption: domain state, execution context, outbox, dispatch,
            // marker/receipt, ledger compaction, and checkpoint-order high-watermarks. Adoption may change only the
            // prepared ledger's current authority binding/revision; every other raw storage unit remains identical.
            var seedCommit = BuildCommit($"groundwork-adoption-seed-{route}", includeDispatch: true);
            var seedOutbox = PendingDispatchOutbox(seedCommit.CommitId, "wf-1");
            seedCommit = seedCommit with
            {
                StateChanges = seedCommit.StateChanges.WithPostCommitOutbox(
                    [Change(seedOutbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, seedOutbox)])
            };
            var seedRequest = new RuntimeCheckpointPrepareRequest(
                seedCommit,
                "groundwork-adoption-seed",
                $"groundwork-adoption-seed-{route}",
                new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["adoption.seed"] = route.ToString() }));
            var seedPreparation = await writer.PrepareAsync(seedRequest);
            var seedToken = Assert.IsType<RuntimeCheckpointPreparationToken>(seedPreparation.Token);
            var enrichedSeed = seedCommit with { Checkpoint = seedCommit.Checkpoint with { Provenance = seedToken.Provenance } };
            Assert.Equal(
                RuntimeCheckpointCommitStoreStatus.Committed,
                (await writer.CommitPreparedAsync(seedToken, enrichedSeed, Decision)).Status);

            if (originalLease is not null)
                await SaveOwnershipAsync(documents, originalLease);

            for (var index = 1; index <= entryCount; index++)
            {
                var commit = BuildCommit(
                    $"groundwork-negative-{route}-{index}",
                    bookmarkNode: index == 1 && includeFoldOutbox ? "fold-committed" : "node-bm-1");
                commit = commit with { ExpectedFence = originalLease?.ToFence() };
                if (includeFoldScopeCleanups)
                {
                    commit = commit with
                    {
                        StateChanges = WithActivityScopeCleanups(commit.StateChanges,
                        [
                            new ActivityScopeCleanupRequest(
                                "wf-1",
                                $"scope-{index}",
                                [],
                                [],
                                [],
                                [])
                        ])
                    };
                }
                if (includeFoldOutbox)
                {
                    var outbox = PendingDispatchOutbox(commit.CommitId, "wf-1");
                    commit = commit with
                    {
                        StateChanges = commit.StateChanges.WithPostCommitOutbox(
                            [Change(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox)])
                    };
                }
                var request = new RuntimeCheckpointPrepareRequest(
                    commit,
                    commit.Checkpoint.Name,
                    commit.Checkpoint.CheckpointId,
                    preparedContexts?[index - 1] ?? RuntimeExecutionContextSnapshot.Empty,
                    RecoveryAuthority: route == RuntimeCheckpointRecoveryRoute.SourceBound
                        ? GroundworkAuthority($"work-adoption-{index}")
                        : null);
                Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, (await writer.PrepareAsync(request)).Status);
            }

            var reservations = (await writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray();
            return new(documents, interceptor, writer, route, reservations.Select(ToAdoptionMember).ToArray());
        }

        public RuntimeCheckpointPreparedAdoptionRequest Request(
            RuntimeExecutionFence target,
            IReadOnlyList<RuntimeCheckpointPreparedAdoptionMember>? members = null) =>
            new("wf-1", Route, Members[^1].WorkflowCheckpointOrder, target, members ?? Members);

        public async Task<RuntimeCheckpointPreparedAdoptionMember[]> ReadMembersAsync()
        {
            var reservations = (await Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder);
            return reservations.Select(ToAdoptionMember).ToArray();
        }

        public async Task<RuntimeCheckpointPreparedFoldRequest> CreateFoldRequestAsync(RuntimeExecutionFence target)
        {
            await ActivateFenceAsync(target);
            var reservations = (await Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray();
            var replayer = new RuntimeCheckpointPreparationReplayer(
                new ImmediateRuntimeCheckpointPersistencePolicy(), [], []);
            var prepared = new List<RuntimeCheckpointPreparedCommit>(reservations.Length);
            foreach (var reservation in reservations)
                prepared.Add(await replayer.RehydrateAsync(reservation));
            var members = prepared.Select((commit, index) => new RuntimeCheckpointPreparedFoldMember(
                commit.Token,
                RuntimeCheckpointPreparedDisposition.Committed,
                reservations[index].CurrentAuthorityFence,
                reservations[index].AuthorityRevision,
                commit)).ToArray();
            return new RuntimeCheckpointPreparedFoldRequest(
                "wf-1",
                members,
                members[^1].WorkflowCheckpointOrder,
                RuntimeCheckpointFold.FoldPrepared(prepared),
                target,
                RuntimeCheckpointRecoveryRoute.SourceFree);
        }

        public async Task<RuntimeCheckpointPreparedFoldRequest> CreateMixedFoldRequestAsync(RuntimeExecutionFence target)
        {
            await ActivateFenceAsync(target);
            var reservations = (await Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray();
            Assert.Equal(3, reservations.Length);
            var replayer = new RuntimeCheckpointPreparationReplayer(
                new ImmediateRuntimeCheckpointPersistencePolicy(), [], []);
            var prepared = new List<RuntimeCheckpointPreparedCommit>(reservations.Length);
            foreach (var reservation in reservations)
                prepared.Add(await replayer.RehydrateAsync(reservation));
            var skipped = new RuntimeCheckpointPreparedCommit(
                prepared[1].Token,
                prepared[1].Commit,
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip))
            {
                CurrentAuthorityFence = prepared[1].CurrentAuthorityFence,
                AuthorityRevision = prepared[1].AuthorityRevision
            };
            RuntimeCheckpointPreparedFoldMember[] members =
            [
                new(prepared[0].Token, RuntimeCheckpointPreparedDisposition.Committed,
                    reservations[0].CurrentAuthorityFence, reservations[0].AuthorityRevision, prepared[0]),
                new(skipped.Token, RuntimeCheckpointPreparedDisposition.Skipped,
                    reservations[1].CurrentAuthorityFence, reservations[1].AuthorityRevision, skipped),
                new(prepared[2].Token, RuntimeCheckpointPreparedDisposition.Failed,
                    reservations[2].CurrentAuthorityFence, reservations[2].AuthorityRevision,
                    FailureCode: "trusted-terminal", FailureMessage: "trusted terminal disposition")
            ];
            return new RuntimeCheckpointPreparedFoldRequest(
                "wf-1",
                members,
                members[^1].WorkflowCheckpointOrder,
                RuntimeCheckpointFold.FoldPrepared([prepared[0]]),
                target,
                RuntimeCheckpointRecoveryRoute.SourceFree);
        }

        public async Task<RuntimeCheckpointPreparedFoldRequest> CreateAllNoncommittedFoldRequestAsync(
            RuntimeExecutionFence target)
        {
            await ActivateFenceAsync(target);
            var reservations = (await Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray();
            Assert.Equal(3, reservations.Length);
            var replayer = new RuntimeCheckpointPreparationReplayer(
                new ImmediateRuntimeCheckpointPersistencePolicy(), [], []);
            var prepared = new List<RuntimeCheckpointPreparedCommit>(reservations.Length);
            foreach (var reservation in reservations)
                prepared.Add(await replayer.RehydrateAsync(reservation));
            var skipped = prepared[0] with
            {
                Decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip)
            };
            RuntimeCheckpointPreparedFoldMember[] members =
            [
                new(skipped.Token, RuntimeCheckpointPreparedDisposition.Skipped,
                    reservations[0].CurrentAuthorityFence, reservations[0].AuthorityRevision, skipped),
                new(prepared[1].Token, RuntimeCheckpointPreparedDisposition.Failed,
                    reservations[1].CurrentAuthorityFence, reservations[1].AuthorityRevision,
                    FailureCode: "terminal-1"),
                new(prepared[2].Token, RuntimeCheckpointPreparedDisposition.Failed,
                    reservations[2].CurrentAuthorityFence, reservations[2].AuthorityRevision,
                    FailureCode: "terminal-2")
            ];
            return new RuntimeCheckpointPreparedFoldRequest(
                "wf-1",
                members,
                members[^1].WorkflowCheckpointOrder,
                RuntimeCheckpointFold.FoldPrepared([]),
                target,
                RuntimeCheckpointRecoveryRoute.SourceFree);
        }

        public async Task ActivateFenceAsync(RuntimeExecutionFence target)
        {
            var now = DateTimeOffset.UtcNow;
            await SaveOwnershipAsync(Documents, new RuntimeExecutionLease(
                target.LeaseId,
                "wf-1",
                target.OwnerId,
                now,
                now.AddHours(1),
                target.FencingToken));
        }

        public RuntimeLogicalCheckpointLedgerEntry[] ReadLedgerEntries()
        {
            var serializer = new GroundworkRuntimeDocumentSerializer();
            return Documents.Snapshot(ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind)
                .Cast<DocumentEnvelope>()
                .Select(envelope => serializer.Deserialize<NormalizableRuntimeCheckpointLedgerDocument>(envelope).Entry)
                .OrderBy(entry => entry.Provenance.WorkflowCheckpointOrder)
                .ToArray();
        }

        public long ReadCommittedOrder()
        {
            var envelope = Assert.IsType<DocumentEnvelope>(Assert.Single(
                Documents.Snapshot(ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind)));
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return document.RootElement.GetProperty("committedOrder").GetInt64();
        }

        public string RawSnapshot()
            => SnapshotRawDocuments(normalizeAuthorityBinding: false);

        public string NormalizedRawSnapshot()
            => SnapshotRawDocuments(normalizeAuthorityBinding: true);

        private string SnapshotRawDocuments(bool normalizeAuthorityBinding)
        {
            var kinds = ElsaRuntimeStorageManifest.Create().StorageUnits
                .Select(unit => unit.Identity.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            return JsonSerializer.Serialize(kinds.Select(kind => new
            {
                Kind = kind,
                Documents = Documents.Snapshot(kind)
                    .Select(document => normalizeAuthorityBinding
                        ? NormalizeAuthorityBinding(document)
                        : JsonSerializer.Serialize(document))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            }));
        }

        private static string NormalizeAuthorityBinding(object document)
        {
            if (document is not DocumentEnvelope envelope)
                return JsonSerializer.Serialize(document);

            var normalizedEnvelope = envelope;
            if (StringComparer.Ordinal.Equals(
                    envelope.DocumentKind,
                    ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind))
            {
                var serializer = new GroundworkRuntimeDocumentSerializer();
                var ledger = serializer.Deserialize<NormalizableRuntimeCheckpointLedgerDocument>(envelope);
                var normalized = ledger with
                {
                    Entry = ledger.Entry with { CurrentAuthorityFence = null, AuthorityRevision = 0 }
                };
                var serialized = serializer.Serialize(
                    ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind,
                    normalized);
                normalizedEnvelope = envelope with { ContentJson = serialized.ContentJson };
            }
            else if (!StringComparer.Ordinal.Equals(
                         envelope.DocumentKind,
                         ElsaRuntimeStorageManifest.RuntimeCheckpointCoordinationDocumentKind) &&
                     !StringComparer.Ordinal.Equals(
                         envelope.DocumentKind,
                         ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind))
                return JsonSerializer.Serialize(document);

            // Adoption deliberately CAS-touches the semantic-neutral coordination row in the same UoW. Normalize
            // only provider envelope metadata here; ContentJson remains exact and would expose collateral mutation.
            var root = JsonNode.Parse(JsonSerializer.Serialize(
                normalizedEnvelope with { Version = 0 }))!.AsObject();
            foreach (var property in root.ToArray())
            {
                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    DateTimeOffset.TryParse(text, out _))
                    root[property.Key] = DateTimeOffset.UnixEpoch;
            }
            return root.ToJsonString();
        }

        private static RuntimeCheckpointPreparedAdoptionMember ToAdoptionMember(RuntimeCheckpointPreparedReservation reservation) =>
            new(
                reservation.CommitId,
                reservation.Token.LedgerToken,
                reservation.Provenance.WorkflowCheckpointOrder,
                reservation.InputFingerprint,
                reservation.Token.CanonicalInputReference,
                reservation.ExpectedFence,
                reservation.ExpectedOrderRevision,
                reservation.ExpectedContextRevision,
                reservation.RecoveryAuthority,
                reservation.CurrentAuthorityFence,
                reservation.AuthorityRevision);
    }

    private sealed record NormalizableRuntimeCheckpointLedgerDocument(RuntimeLogicalCheckpointLedgerEntry Entry);

    private static async Task<(RuntimeCheckpointPreparedReservation[] Reservations, string ImmutablePreparedBytes)> SnapshotPreparedAsync(
        GroundworkRuntimeCheckpointWriter writer)
    {
        var page = await writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250));
        return (
            page.Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray(),
            JsonSerializer.Serialize(page.Reservations.Select(reservation => new
            {
                reservation.Status,
                reservation.Token.CommitId,
                reservation.Token.LedgerToken,
                reservation.Provenance,
                reservation.Token.CanonicalInputReference,
                reservation.Token.CanonicalInputFingerprint,
                reservation.ExpectedFence,
                reservation.ExpectedOrderRevision,
                reservation.ExpectedContextRevision,
                reservation.RecoveryAuthority,
                reservation.CanonicalEnvelope
            })));
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
            "T027 must add the single provider-atomic adoption CAS after this T026 fixture has persisted its rows.");
        var awaitable = operation!.Invoke(store, [request, cancellationToken]);
        var task = (Task)awaitable!.GetType().GetMethod("AsTask")!.Invoke(awaitable, [])!;
        await task;
        return (RuntimeCheckpointPreparedAdoptionReceipt)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    [Fact]
    public async Task Prepared_ledger_page_uses_the_declared_workflow_status_order_commit_route()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var ledgerUnit = Assert.Single(
            manifest.StorageUnits,
            unit => unit.Identity.Value == ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind);
        var route = Assert.Single(
            ledgerUnit.Queries,
            query => query.Identity == ElsaRuntimeStorageManifest.PagePreparedRuntimeCheckpointsQuery);
        var index = Assert.Single(
            ledgerUnit.Indexes,
            candidate => candidate.Identity == ElsaRuntimeStorageManifest.RuntimeCheckpointPreparedLedgerByWorkflowStatusOrderCommit);

        Assert.Equal(ElsaRuntimeStorageManifest.RuntimeCheckpointPreparedLedgerByWorkflowStatusOrderCommit, route.IndexIdentity);
        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowExecutionIdField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerStatusField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowCheckpointOrderField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerCommitIdField
            ],
            index.Fields.Select(field => field.Path));

        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var writer = CreateWriter(store);
        var preparedLedger = Assert.IsAssignableFrom<IRuntimeCheckpointPreparedLedgerStore>(writer);
        await writer.PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-prepared-page-a")));
        await writer.PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-prepared-page-b")));

        var page = await preparedLedger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 1, null));
        Assert.Single(page.Reservations);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Deferred_candidate_round_trips_through_token_envelope_and_shared_replayer()
    {
        var writer = CreateWriter(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized()));
        var request = RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-deferred-candidate")) with
        {
            InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred
        };

        var preparation = await writer.PrepareAsync(request);
        var reservation = Assert.Single((await writer.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);
        var replayed = await new RuntimeCheckpointPreparationReplayer(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                [],
                [])
            .RehydrateAsync(reservation);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, preparation.Token!.InitialPersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, reservation.CandidatePersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, decoded.InitialPersistenceMode);
        Assert.Equal(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256);
        Assert.Equal(request.Commit.CommitId, replayed.Commit.CommitId);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, replayed.Decision.Mode);
    }

    [Fact]
    public async Task Prepared_authority_round_trips_through_groundwork_restart_page_and_decode_without_marker_or_state_side_effects()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var initialWriter = CreateWriter(store);
        var authority = EncodeRecoveryAuthority(BuildRecoveryAuthorityWorkItem());
        var request = WithRecoveryAuthority(
            RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-authority-groundwork")),
            authority);

        var prepared = await initialWriter.PrepareAsync(request);
        var restartedWriter = CreateWriter(store);
        var reservation = Assert.Single((await restartedWriter.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);

        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(prepared.Token!));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(reservation));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(decoded));
        Assert.Equal(prepared.Token!.CanonicalInputFingerprint, reservation.InputFingerprint);
        Assert.Equal(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, request.Commit.CommitId));
        Assert.Empty((await restartedWriter.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations
            .Where(candidate => candidate.CommitId != request.Commit.CommitId));
    }

    private static RuntimeSchedulerWorkItem BuildRecoveryAuthorityWorkItem()
    {
        using var document = JsonDocument.Parse("""{"source":"authority"}""");
        return new RuntimeSchedulerWorkItem(
            "work-authority-groundwork",
            "wf-1",
            "command-authority-groundwork",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            "envelope-authority-groundwork",
            "idempotency-authority-groundwork",
            DateTimeOffset.UnixEpoch.AddTicks(11),
            DateTimeOffset.UnixEpoch.AddTicks(17),
            7,
            document.RootElement.Clone(),
            new Dictionary<string, string> { ["command"] = "authority" },
            new Dictionary<string, string> { ["envelope"] = "authority" },
            "scope-authority-groundwork",
            new ActivityExecutionAttemptLineage(2, "attempt-first", "attempt-previous"));
    }

    private static object EncodeRecoveryAuthority(RuntimeSchedulerWorkItem workItem)
    {
        var authorityType = typeof(RuntimeSchedulerWorkItem).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryAuthority");
        var codecType = typeof(RuntimeSchedulerWorkItem).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryAuthorityCodec");
        Assert.NotNull(authorityType);
        Assert.NotNull(codecType);
        var method = codecType!.GetMethods()
            .Single(candidate => candidate.ReturnType == authorityType &&
                                 candidate.GetParameters() is [{ ParameterType: var type }] &&
                                 type == typeof(RuntimeSchedulerWorkItem));
        return method.Invoke(method.IsStatic ? null : Activator.CreateInstance(codecType), [workItem])!;
    }

    private static RuntimeCheckpointPrepareRequest WithRecoveryAuthority(
        RuntimeCheckpointPrepareRequest request,
        object authority)
    {
        var property = typeof(RuntimeCheckpointPrepareRequest).GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        property!.SetValue(request, authority);
        return request;
    }

    private static object GetRecoveryAuthority(object value)
    {
        var property = value.GetType().GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        var authority = property!.GetValue(value);
        Assert.IsType(property.PropertyType, authority);
        return authority!;
    }

    private static void AssertRecoveryAuthorityEquivalent(object expected, object actual)
    {
        var authorityType = expected.GetType();
        foreach (var propertyName in new[] { "Fingerprint", "WorkflowExecutionId", "WorkItemId" })
            Assert.Equal(
                authorityType.GetProperty(propertyName)!.GetValue(expected),
                authorityType.GetProperty(propertyName)!.GetValue(actual));
    }
}
