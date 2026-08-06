using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Contract tests for the durable recovery input required before a coalescing session may defer a checkpoint.
/// They intentionally name the separate prepared-ledger capability: there is no compatibility or sequential fallback.
/// </summary>
public sealed class RuntimeCheckpointCoalescingPreparedLedgerTests
{
    [Fact]
    public void Prepared_reservation_retains_immutable_recovery_authority_beside_its_canonical_envelope()
    {
        var authorityType = RecoveryAuthorityTestContract.AuthorityType;
        Assert.Equal(authorityType, typeof(RuntimeCheckpointPreparationToken).GetProperty("RecoveryAuthority")?.PropertyType);
        Assert.Equal(authorityType, typeof(RuntimeLogicalCheckpointLedgerEntry).GetProperty("RecoveryAuthority")?.PropertyType);
        Assert.Equal(authorityType, typeof(RuntimeCheckpointPreparedReservation).GetProperty("RecoveryAuthority")?.PropertyType);
        Assert.Equal(
            typeof(RuntimeCheckpointPreparationPayloadEnvelope),
            typeof(RuntimeCheckpointPreparedReservation).GetProperty(nameof(RuntimeCheckpointPreparedReservation.CanonicalEnvelope))?.PropertyType);
    }

    [Fact]
    public void Prepared_ledger_capability_is_explicit_and_has_no_default_interface_fallback()
    {
        Assert.All(
            typeof(IRuntimeCheckpointPreparedLedgerStore).GetMethods(),
            operation => Assert.True(operation.IsAbstract, $"Prepared-ledger operation '{operation.Name}' must be implemented by the durable provider."));
    }

    [Fact]
    public void Live_committer_uses_the_explicit_deterministic_replayer_contract()
    {
        Assert.Contains(
            typeof(RuntimeCheckpointCommitter).GetConstructors(),
            constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IRuntimeCheckpointPreparationReplayer)));
        Assert.True(typeof(IRuntimeCheckpointPreparationReplayer).IsAssignableFrom(typeof(RuntimeCheckpointPreparationReplayer)));
    }

    [Fact]
    public async Task Prepared_page_is_bounded_stable_and_rejects_a_cursor_rebound_to_another_request()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var ledger = Assert.IsAssignableFrom<IRuntimeCheckpointPreparedLedgerStore>(store);
        await store.PrepareAsync(Request("commit-b"));
        await store.PrepareAsync(Request("commit-a"));
        await store.PrepareAsync(Request("commit-c"));

        var first = await ledger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("workflow-coalescing", 2, null));

        Assert.Equal(["commit-b", "commit-a"], first.Reservations.Select(reservation => reservation.CommitId));
        Assert.NotNull(first.NextCursor);
        Assert.All(first.Reservations, reservation => Assert.Equal(RuntimeLogicalCheckpointLedgerStatus.Prepared, reservation.Status));

        var second = await ledger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("workflow-coalescing", 2, first.NextCursor));
        Assert.Equal(["commit-c"], second.Reservations.Select(reservation => reservation.CommitId));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ledger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("other-workflow", 2, first.NextCursor)));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await ledger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("workflow-coalescing", 1, first.NextCursor)));
    }

    [Fact]
    public async Task Reservation_exposes_digest_verified_canonical_recovery_input_without_visible_checkpoint_effects()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var request = Request("commit-canonical", context: new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string>
        {
            ["capture"] = "opaque"
        }));
        var prepared = await store.PrepareAsync(request);
        var ledger = Assert.IsAssignableFrom<IRuntimeCheckpointPreparedLedgerStore>(store);

        var reservation = Assert.Single((await ledger.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1, null))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);

        Assert.Equivalent(request.Commit, decoded.Commit, strict: true);
        Assert.Equal(request.SourceIdentity, decoded.SourceIdentity);
        Assert.Equal(request.OperationIdentity, decoded.OperationIdentity);
        Assert.Equal(request.RequestedExecutionContext, decoded.RequestedExecutionContext);
        Assert.Equal(prepared.Token!.Provenance, reservation.Provenance);
        Assert.Equal(prepared.Token.ExpectedOrderRevision, reservation.ExpectedOrderRevision);
        Assert.Equal(prepared.Token.ExpectedContextRevision, reservation.ExpectedContextRevision);
        Assert.Equal(prepared.Token.ExpectedFence, reservation.ExpectedFence);
        Assert.Equal(prepared.Token.InitialPersistenceMode, reservation.CandidatePersistenceMode);
        Assert.Equal(prepared.Token.CanonicalInputFingerprint, reservation.InputFingerprint);
        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public async Task Prepared_authority_round_trips_through_in_memory_restart_page_and_decode_without_checkpoint_side_effects()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var firstStore = new InMemoryRuntimeCheckpointCommitStore(state: state);
        var authority = RecoveryAuthorityTestContract.Encode(AuthorityWorkItem.Sample().ToRuntimeWorkItem());
        var request = WithRecoveryAuthority(Request("commit-authority-inmemory"), authority);

        var prepared = await firstStore.PrepareAsync(request);
        var restartedStore = new InMemoryRuntimeCheckpointCommitStore(state: state);
        var reservation = Assert.Single((await restartedStore.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);

        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(prepared.Token!));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(reservation));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(decoded));
        Assert.Equal(prepared.Token!.CanonicalInputFingerprint, reservation.InputFingerprint);
        Assert.Equal(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256);
        Assert.Empty(restartedStore.ListCommits());
        Assert.Empty((await restartedStore.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations
            .Where(candidate => candidate.CommitId != request.Commit.CommitId));
    }

    private static RuntimeCheckpointPrepareRequest WithRecoveryAuthority(
        RuntimeCheckpointPrepareRequest request,
        object authority)
    {
        var property = typeof(RuntimeCheckpointPrepareRequest).GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        Assert.Equal(RecoveryAuthorityTestContract.AuthorityType, property!.PropertyType);
        property.SetValue(request, authority);
        return request;
    }

    private static object GetRecoveryAuthority(object value)
    {
        var property = value.GetType().GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        var authority = property!.GetValue(value);
        Assert.IsType(RecoveryAuthorityTestContract.AuthorityType, authority);
        return authority!;
    }

    private static void AssertRecoveryAuthorityEquivalent(object expected, object actual)
    {
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "Fingerprint"),
            RecoveryAuthorityTestContract.Property<string>(actual, "Fingerprint"));
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "WorkflowExecutionId"),
            RecoveryAuthorityTestContract.Property<string>(actual, "WorkflowExecutionId"));
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(expected, "WorkItemId"),
            RecoveryAuthorityTestContract.Property<string>(actual, "WorkItemId"));
    }

    private static RuntimeCheckpointPrepareRequest Request(
        string commitId,
        RuntimeExecutionContextSnapshot? context = null,
        string workflowExecutionId = "workflow-coalescing",
        RuntimeCheckpointStateChangeSet? stateChanges = null)
    {
        var checkpoint = new RuntimeCheckpoint(
            $"checkpoint-{commitId}",
            "Checkpoint",
            workflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>());
        var commit = new RuntimeCheckpointCommit(
            commitId,
            checkpoint,
            stateChanges ?? new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

        return new RuntimeCheckpointPrepareRequest(
            commit,
            "coalescing-source",
            $"operation-{commitId}",
            context ?? RuntimeExecutionContextSnapshot.Empty);
    }
}
