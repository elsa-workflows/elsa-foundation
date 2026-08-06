using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointPreparedQueryAndReplayerBranchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Query_rejects_missing_workflow_execution_id(string? workflowExecutionId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RuntimeCheckpointPreparedQuery(workflowExecutionId!, 1).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RuntimeCheckpointPreparedQuery.MaximumPageSize + 1)]
    public void Query_rejects_page_sizes_outside_the_bounded_range(int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeCheckpointPreparedQuery("workflow-query", pageSize).Validate());
    }

    [Fact]
    public void Query_accepts_minimum_and_maximum_page_sizes_and_a_null_cursor()
    {
        Assert.Null(new RuntimeCheckpointPreparedQuery("workflow-query", 1).Validate().DecodeCursor());
        Assert.Null(new RuntimeCheckpointPreparedQuery("workflow-query", RuntimeCheckpointPreparedQuery.MaximumPageSize).Validate().DecodeCursor());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-base64")]
    [InlineData("bm90LWpzb24")]
    [InlineData("e30")]
    public void Query_rejects_malformed_or_blank_cursor(string cursor)
    {
        Assert.Throws<ArgumentException>(() => new RuntimeCheckpointPreparedQuery("workflow-query", 2, cursor).Validate());
    }

    [Fact]
    public void Query_cursor_round_trips_and_rejects_every_bound_authority_mismatch()
    {
        var query = new RuntimeCheckpointPreparedQuery("workflow-query", 2);
        var cursor = query.EncodeCursor(3, "commit-3");

        var decoded = new RuntimeCheckpointPreparedQuery("workflow-query", 2, cursor).Validate().DecodeCursor();
        Assert.Equal(3, decoded!.WorkflowCheckpointOrder);
        Assert.Equal("commit-3", decoded.CommitId);

        foreach (var invalid in new[]
                 {
                     new RuntimeCheckpointPreparedCursor(2, "workflow-query", RuntimeLogicalCheckpointLedgerStatus.Prepared, 2, 3, "commit-3"),
                     new RuntimeCheckpointPreparedCursor(1, "other-workflow", RuntimeLogicalCheckpointLedgerStatus.Prepared, 2, 3, "commit-3"),
                     new RuntimeCheckpointPreparedCursor(1, "workflow-query", RuntimeLogicalCheckpointLedgerStatus.Committed, 2, 3, "commit-3"),
                     new RuntimeCheckpointPreparedCursor(1, "workflow-query", RuntimeLogicalCheckpointLedgerStatus.Prepared, 3, 3, "commit-3"),
                     new RuntimeCheckpointPreparedCursor(1, "workflow-query", RuntimeLogicalCheckpointLedgerStatus.Prepared, 2, 0, "commit-3"),
                     new RuntimeCheckpointPreparedCursor(1, "workflow-query", RuntimeLogicalCheckpointLedgerStatus.Prepared, 2, 3, " ")
                 })
        {
            var invalidCursor = Encode(invalid);
            Assert.Throws<ArgumentException>(() => new RuntimeCheckpointPreparedQuery("workflow-query", 2, invalidCursor).Validate());
        }
    }

    [Fact]
    public void Query_rejects_invalid_cursor_sort_keys_before_encoding()
    {
        var query = new RuntimeCheckpointPreparedQuery("workflow-query", 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => query.EncodeCursor(0, "commit-1"));
        Assert.Throws<ArgumentException>(() => query.EncodeCursor(1, " "));
    }

    [Fact]
    public async Task Replayer_rejects_null_invalid_digest_mismatched_and_non_authoritative_reservations()
    {
        var request = Request("commit-replayer-authority");
        var reservation = await PrepareReservationAsync(request);
        var replayer = Replayer(RuntimeCheckpointPersistenceMode.Immediate);

        await Assert.ThrowsAsync<ArgumentNullException>(() => replayer.RehydrateAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => replayer.RehydrateAsync(reservation with { Token = null! }).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => replayer.RehydrateAsync(reservation with { CanonicalEnvelope = null! }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => replayer.RehydrateAsync(reservation with { Token = reservation.Token with { CommitId = " " } }).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => replayer.RehydrateAsync(reservation with { Token = reservation.Token with { CanonicalInputFingerprint = "not-the-envelope" } }).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => replayer.RehydrateAsync(reservation with { Status = RuntimeLogicalCheckpointLedgerStatus.Committed }).AsTask());

        var codec = new RuntimeCheckpointPreparationPayloadCodec();
        var changedCommitEnvelope = codec.Encode(request with { Commit = request.Commit with { CommitId = "other-commit" } });
        var changedModeEnvelope = codec.Encode(request with { InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred });
        await Assert.ThrowsAsync<InvalidOperationException>(() => replayer.RehydrateAsync(reservation with
        {
            CanonicalEnvelope = changedCommitEnvelope,
            Token = reservation.Token with { CanonicalInputFingerprint = changedCommitEnvelope.PayloadSha256 }
        }).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => replayer.RehydrateAsync(reservation with
        {
            CanonicalEnvelope = changedModeEnvelope,
            Token = reservation.Token with { CanonicalInputFingerprint = changedModeEnvelope.PayloadSha256 }
        }).AsTask());
    }

    [Fact]
    public async Task Replayer_orders_enrichers_propagates_exceptions_and_observes_cancellation_before_enrichment()
    {
        var calls = new List<string>();
        var reservation = await PrepareReservationAsync(Request("commit-replayer-order"));
        var ordered = Replayer(RuntimeCheckpointPersistenceMode.Immediate,
            new RecordingEnricher(20, "second", calls),
            new RecordingEnricher(10, "first", calls),
            new RecordingEnricher(20, "third", calls));

        await ordered.RehydrateAsync(reservation);
        Assert.Equal(["first", "second", "third"], calls);

        var primary = new InvalidOperationException("enrichment failed");
        var throwing = Replayer(RuntimeCheckpointPersistenceMode.Immediate, new ThrowingEnricher(primary));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => throwing.RehydrateAsync(reservation).AsTask());
        Assert.Same(primary, failure);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledCalls = new List<string>();
        var cancelled = Replayer(RuntimeCheckpointPersistenceMode.Immediate, new RecordingEnricher(0, "must-not-run", cancelledCalls));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.RehydrateAsync(reservation, cancellation.Token).AsTask());
        Assert.Empty(cancelledCalls);
    }

    [Theory]
    [InlineData(RuntimeCheckpointPersistenceMode.Immediate, RuntimeCheckpointPersistenceMode.Immediate)]
    [InlineData(RuntimeCheckpointPersistenceMode.Deferred, RuntimeCheckpointPersistenceMode.Deferred)]
    [InlineData(RuntimeCheckpointPersistenceMode.Skip, RuntimeCheckpointPersistenceMode.Skip)]
    public async Task Replayer_preserves_the_policy_decision_when_no_atomicity_override_is_required(
        RuntimeCheckpointPersistenceMode configured,
        RuntimeCheckpointPersistenceMode expected)
    {
        var prepared = await Replayer(configured).RehydrateAsync(await PrepareReservationAsync(Request("commit-policy-" + configured)));

        Assert.Equal(expected, prepared.Decision.Mode);
    }

    [Fact]
    public async Task Replayer_folds_outbox_and_forces_immediate_only_for_context_or_outbox_atomicity()
    {
        var contextReservation = await PrepareReservationAsync(Request(
            "commit-replayer-context",
            context: new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["opaque"] = "context" })));
        var context = await Replayer(RuntimeCheckpointPersistenceMode.Deferred).RehydrateAsync(contextReservation);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, context.Decision.Mode);
        Assert.Empty(context.Commit.StateChanges.PostCommitOutbox);

        var intent = new RuntimePostCommitIntent(
            "intent-replayer", "workflow-replayer", "Test.Replayer", DateTimeOffset.UnixEpoch, null, null, null);
        var outboxReservation = await PrepareReservationAsync(Request("commit-replayer-outbox", intents: [intent]));
        var outbox = await Replayer(
            RuntimeCheckpointPersistenceMode.Deferred,
            contributions: [new RuntimePostCommitIntentHandlerContribution("Test.Replayer", typeof(NoopIntentHandler))])
            .RehydrateAsync(outboxReservation);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, outbox.Decision.Mode);
        var item = Assert.Single(outbox.Commit.StateChanges.PostCommitOutbox);
        Assert.Equal("commit-replayer-outbox:intent-replayer", item.State.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.State.Status);
    }

    private static async Task<RuntimeCheckpointPreparedReservation> PrepareReservationAsync(RuntimeCheckpointPrepareRequest request)
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.PrepareAsync(request);
        return Assert.Single((await ((IRuntimeCheckpointPreparedLedgerStore)store).PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
    }

    private static RuntimeCheckpointPreparationReplayer Replayer(
        RuntimeCheckpointPersistenceMode mode,
        params IRuntimeCheckpointCommitEnricher[] enrichers) =>
        Replayer(mode, enrichers, []);

    private static RuntimeCheckpointPreparationReplayer Replayer(
        RuntimeCheckpointPersistenceMode mode,
        IEnumerable<IRuntimeCheckpointCommitEnricher>? enrichers = null,
        IEnumerable<RuntimePostCommitIntentHandlerContribution>? contributions = null) =>
        new(new FixedPolicy(mode), enrichers ?? [], contributions ?? []);

    private static RuntimeCheckpointPrepareRequest Request(
        string commitId,
        RuntimeExecutionContextSnapshot? context = null,
        IReadOnlyList<RuntimePostCommitIntent>? intents = null) =>
        new(
            new RuntimeCheckpointCommit(
                commitId,
                new RuntimeCheckpoint($"checkpoint-{commitId}", "Replayer", "workflow-replayer", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
                intents ?? [],
                new Dictionary<string, string>()),
            "replayer-source",
            $"operation-{commitId}",
            context ?? RuntimeExecutionContextSnapshot.Empty);

    private static string Encode(RuntimeCheckpointPreparedCursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedPolicy(RuntimeCheckpointPersistenceMode mode) : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(mode));
    }

    private sealed class RecordingEnricher(int order, string name, ICollection<string> calls) : IRuntimeCheckpointCommitEnricher
    {
        public int Order => order;
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            calls.Add(name);
            return ValueTask.FromResult(commit);
        }
    }

    private sealed class ThrowingEnricher(Exception exception) : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<RuntimeCheckpointCommit>(exception);
    }

    private sealed class NoopIntentHandler : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
