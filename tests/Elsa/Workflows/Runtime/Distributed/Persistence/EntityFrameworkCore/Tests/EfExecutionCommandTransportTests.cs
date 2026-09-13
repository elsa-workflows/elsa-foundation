using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

/// <summary>Executable SQLite proof for the D02 stream head and D03 command transport.</summary>
public sealed class EfExecutionCommandTransportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Send_and_reopen_round_trips_the_frozen_envelope_losslessly_and_separates_sequences()
    {
        await using var fixture = await Fixture.CreateAsync("tenant:alpha");
        using var payload = JsonDocument.Parse("{\"text\":\"x%:🧪\",\"nested\":[1,true,null]}");
        var command = new WorkflowExecutionCommand(
            "command-🧪",
            "wf-1",
            WorkflowExecutionCommandKind.GeneratedEvent,
            Now.AddHours(3),
            payload.RootElement.Clone(),
            new Dictionary<string, string> { ["utf16"] = "value:%🧪", ["z"] = "last" });
        var envelope = new WorkflowExecutionCommandEnvelope(
            "envelope:%:🧪",
            "wf-1",
            command,
            "idempotency:%:🧪",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.FromHours(5.5)),
            sequence: 77,
            metadata: new Dictionary<string, string> { ["envelope"] = "metadata:%🧪" },
            partition: new WorkflowExecutionPartition("tenant:alpha"));

        var sent = await fixture.Transport.SendAsync("wf-1", envelope, Now);
        await using var reopened = await fixture.ReopenAsync("tenant:alpha");
        var item = Assert.Single(await reopened.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1));

        Assert.Equal("transport:wf-1:1", sent.TransportItemId);
        Assert.Equal(sent.TransportItemId, item.TransportItemId);
        Assert.Equal(1, sent.Sequence);
        Assert.Equal(77, item.Envelope.Sequence);
        Assert.Equal(envelope.EnvelopeId, item.Envelope.EnvelopeId);
        Assert.Equal(envelope.IdempotencyKey, item.Envelope.IdempotencyKey);
        Assert.Equal(envelope.DeliveryMode, item.Envelope.DeliveryMode);
        Assert.Equal(envelope.EnqueuedAt, item.Envelope.EnqueuedAt);
        Assert.Equal(envelope.EnqueuedAt.Offset, item.Envelope.EnqueuedAt.Offset);
        Assert.Equal(envelope.Partition, item.Envelope.Partition);
        Assert.Equal(envelope.Metadata, item.Envelope.Metadata);
        Assert.Equal(command.CommandId, item.Envelope.Command.CommandId);
        Assert.Equal(command.Kind, item.Envelope.Command.Kind);
        Assert.Equal(command.EnqueuedAt, item.Envelope.Command.EnqueuedAt);
        Assert.Equal(command.EnqueuedAt.Offset, item.Envelope.Command.EnqueuedAt.Offset);
        Assert.Equal(command.Metadata, item.Envelope.Command.Metadata);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(command.Payload!.Value.GetRawText()),
            JsonNode.Parse(item.Envelope.Command.Payload!.Value.GetRawText())));

        // The persisted payload is the complete frozen transport item, including queue state and
        // the nested envelope; an envelope-only projection cannot reconstruct these fields.
        var stored = await reopened.Context.CommandTransportItems.AsNoTracking().SingleAsync();
        var persistedItem = JsonSerializer.Deserialize<ExecutionCommandTransportItem>(stored.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(persistedItem);
        Assert.Equal(item.TransportItemId, persistedItem!.TransportItemId);
        Assert.Equal(item.Sequence, persistedItem.Sequence);
        Assert.Equal(item.EnqueuedAt, persistedItem.EnqueuedAt);
        Assert.Equal(item.DeliveryAttemptCount, persistedItem.DeliveryAttemptCount);
        Assert.Equal(item.LeasedByOwnerId, persistedItem.LeasedByOwnerId);
        Assert.Equal(item.LeaseExpiresAt, persistedItem.LeaseExpiresAt);
        Assert.Equal(item.Envelope.EnvelopeId, persistedItem.Envelope.EnvelopeId);
        Assert.Equal(item.Envelope.Command.CommandId, persistedItem.Envelope.Command.CommandId);

        var escaped = await reopened.Transport.SendAsync("wf:%🧪", Envelope("wf:%🧪", "special", partition: "tenant:alpha"), Now);
        Assert.Equal("transport:wf%3A%25🧪:1", escaped.TransportItemId);
    }

    [Fact]
    public async Task Send_rejects_mismatched_ids_and_partition_before_any_provider_command()
    {
        var interceptor = new CountingInterceptor();
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        var mismatchedWorkflow = Envelope("wf-envelope", "mismatch");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Transport.SendAsync("wf-request", mismatchedWorkflow, Now).AsTask());

        var mismatchedPartition = Envelope("wf-1", "partition", partition: "scope-b");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transport.SendAsync("wf-1", mismatchedPartition, Now).AsTask());
        Assert.Equal(0, interceptor.Commands);
    }

    [Fact]
    public async Task First_multi_sender_and_concurrent_sends_are_contiguous_and_unique()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var first = await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "first"), Now);
        var sends = await Task.WhenAll(Enumerable.Range(0, 11).Select(index =>
            SendFromReopenedContextAsync(fixture, index)));

        var sequences = sends.Append(first).Select(item => item.Sequence).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, sequences.Length).Select(i => (long)i), sequences);
        Assert.Equal(sequences.Length, sends.Append(first).Select(item => item.TransportItemId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Colon_percent_maximum_and_supplementary_ids_are_isolated_and_ordered_ordinally()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        var ids = new[]
        {
            "wf:%:🧪",
            new string('x', DistributedRuntimeIdentityConstraints.MaximumLength - 2) + "😀",
            "wf-\U00010000",
            "wf-\uE000"
        };
        foreach (var id in ids)
            await fixture.Transport.SendAsync(id, Envelope(id, id), Now);

        var expected = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, await fixture.Transport.ListPendingExecutionIdsAsync(Now, 500));

        await using var otherScope = await fixture.ReopenAsync("scope-b");
        Assert.Empty(await otherScope.Transport.ListPendingExecutionIdsAsync(Now, 500));
        Assert.Equal(0, await otherScope.Transport.CountPendingAsync(ids[0]));
    }

    [Fact]
    public async Task Same_execution_id_in_different_partitions_isolated_and_cross_scope_rejected()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "a", partition: "scope-a"), Now);
        await using var other = await fixture.ReopenAsync("scope-b");
        await other.Transport.SendAsync("wf-1", Envelope("wf-1", "b", partition: "scope-b"), Now);
        Assert.Equal(1, await fixture.Transport.CountPendingAsync("wf-1"));
        Assert.Equal(1, await other.Transport.CountPendingAsync("wf-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "wrong", partition: "scope-b"), Now).AsTask());
    }

    [Fact]
    public async Task Durable_head_counts_leased_rows_and_preserves_high_water_after_final_ack()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "one"), Now);
        var leased = Assert.Single(await fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1));
        Assert.Equal(1, await fixture.Transport.CountPendingAsync("wf-1"));
        Assert.True(await fixture.Transport.AckAsync("wf-1", leased.TransportItemId, "node-a", leased.LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.Equal(0, await fixture.Transport.CountPendingAsync("wf-1"));
        var next = await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "two"), Now.AddSeconds(2));
        Assert.Equal(2, next.Sequence);
        await using var reopened = await fixture.ReopenAsync("scope-a");
        Assert.Equal(1, await reopened.Transport.CountPendingAsync("wf-1"));
        Assert.Equal(["wf-1"], await reopened.Transport.ListPendingExecutionIdsAsync(Now.AddSeconds(2), 1));
        Assert.Equal(2, Assert.Single(await reopened.Transport.LeaseAsync("wf-1", "node-b", Now.AddSeconds(2), LeaseDuration, 1)).Sequence);
    }

    [Fact]
    public async Task Lease_is_fifo_bounded_and_replenishes_past_invisible_prefix()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        for (var i = 1; i <= 6; i++)
            await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", $"env-{i}"), Now);
        var first = await fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 3);
        Assert.Equal([1L, 2L, 3L], first.Select(item => item.Sequence));
        var second = await fixture.Transport.LeaseAsync("wf-1", "node-b", Now.AddSeconds(1), LeaseDuration, 3);
        Assert.Equal([4L, 5L, 6L], second.Select(item => item.Sequence));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 0).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 501).AsTask());
    }

    [Fact]
    public async Task Inclusive_expiry_redelivers_and_competing_leasers_receive_disjoint_union()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        for (var i = 1; i <= 20; i++)
            await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", $"env-{i}"), Now);
        var first = await fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1);
        var expired = Now.Add(LeaseDuration);
        var second = await fixture.Transport.LeaseAsync("wf-1", "node-b", expired, LeaseDuration, 1);
        Assert.Single(second);
        Assert.Equal(first[0].Sequence, second[0].Sequence);
        Assert.Equal(2, second[0].DeliveryAttemptCount);
        Assert.False(await fixture.Transport.AckAsync("wf-1", first[0].TransportItemId, "node-a", first[0].LeaseToken!.Value, expired));

        await using var left = await fixture.ReopenAsync("scope-a");
        await using var right = await fixture.ReopenAsync("scope-a");
        var claimsAt = expired.Add(LeaseDuration).AddSeconds(1);
        var claims = await Task.WhenAll(
            left.Transport.LeaseAsync("wf-1", "node-c", claimsAt, LeaseDuration, 10).AsTask(),
            right.Transport.LeaseAsync("wf-1", "node-d", claimsAt, LeaseDuration, 10).AsTask());
        Assert.Equal(20, claims.SelectMany(items => items).Select(item => item.TransportItemId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(claims, items => Assert.Equal(items.Select(item => item.Sequence).Order(), items.Select(item => item.Sequence)));
    }

    [Fact]
    public async Task Acknowledgement_accepts_only_current_live_exact_token_and_deletes_one()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "one"), Now);
        var first = Assert.Single(await fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1));
        Assert.False(await fixture.Transport.AckAsync("wf-other", first.TransportItemId, "node-a", first.LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.False(await fixture.Transport.AckAsync("wf-1", "missing", "node-a", first.LeaseToken.Value, Now.AddSeconds(1)));
        Assert.False(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-b", first.LeaseToken.Value, Now.AddSeconds(1)));
        Assert.False(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-a", first.LeaseToken.Value + 1, Now.AddSeconds(1)));
        Assert.True(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-a", first.LeaseToken.Value, Now.AddSeconds(1)));
        Assert.False(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-a", first.LeaseToken.Value, Now.AddSeconds(1)));
    }

    [Fact]
    public async Task Expired_and_successor_leases_reject_stale_ack_but_accept_current_ack()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "one"), Now);
        var first = Assert.Single(await fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1));
        var afterExpiry = Now.Add(LeaseDuration);
        Assert.False(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-a", first.LeaseToken!.Value, afterExpiry));
        var successor = Assert.Single(await fixture.Transport.LeaseAsync("wf-1", "node-b", afterExpiry, LeaseDuration, 1));
        Assert.True(successor.LeaseToken > first.LeaseToken);
        Assert.False(await fixture.Transport.AckAsync("wf-1", first.TransportItemId, "node-a", first.LeaseToken.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await fixture.Transport.AckAsync("wf-1", successor.TransportItemId, "node-b", successor.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Fact]
    public async Task Expired_lease_redelivers_after_context_reopen_and_rejects_the_stale_token()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-restart", Envelope("wf-restart", "one"), Now);
        var before = Assert.Single(await fixture.Transport.LeaseAsync("wf-restart", "node-a", Now, LeaseDuration, 1));
        var afterExpiry = Now.Add(LeaseDuration).AddSeconds(1);
        await using var reopened = await fixture.ReopenAsync("scope-a");
        var after = Assert.Single(await reopened.Transport.LeaseAsync("wf-restart", "node-b", afterExpiry, LeaseDuration, 1));

        Assert.Equal(before.TransportItemId, after.TransportItemId);
        Assert.Equal(before.DeliveryAttemptCount + 1, after.DeliveryAttemptCount);
        Assert.True(after.LeaseToken > before.LeaseToken);
        Assert.False(await reopened.Transport.AckAsync("wf-restart", before.TransportItemId, "node-a", before.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await reopened.Transport.AckAsync("wf-restart", after.TransportItemId, "node-b", after.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Fact]
    public async Task Pending_execution_listing_is_visible_only_bounded_and_stable_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        foreach (var id in new[] { "wf-c", "wf-a", "wf-b", "wf-d" })
            await fixture.Transport.SendAsync(id, Envelope(id, id), Now);
        var leased = Assert.Single(await fixture.Transport.LeaseAsync("wf-a", "node-a", Now, LeaseDuration, 1));
        Assert.Equal(["wf-b"], await fixture.Transport.ListPendingExecutionIdsAsync(Now, 1));
        Assert.Equal(["wf-b", "wf-c"], await fixture.Transport.ListPendingExecutionIdsAsync(Now, 2));
        await using var reopened = await fixture.ReopenAsync("scope-a");
        Assert.Equal(1, await reopened.Transport.CountPendingAsync("wf-a"));
        Assert.Equal(["wf-a"], await reopened.Transport.ListPendingExecutionIdsAsync(Now.Add(LeaseDuration), 1));
        Assert.True(await reopened.Transport.AckAsync("wf-a", leased.TransportItemId, "node-a", leased.LeaseToken!.Value, Now.AddSeconds(1)));
    }

    [Fact]
    public async Task Cancellation_and_invalid_input_fail_before_provider_io()
    {
        var interceptor = new CountingInterceptor();
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "cancel"), Now, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Transport.ListPendingExecutionIdsAsync(Now, 1, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Transport.CountPendingAsync("wf-1", cancellation.Token).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Transport.SendAsync(" ", Envelope("wf-1", "bad"), Now).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Transport.LeaseAsync("wf-1", " ", Now, LeaseDuration, 1).AsTask());
        Assert.Equal(0, interceptor.Commands);
    }

    [Fact]
    public async Task Provider_failure_is_typed_bounded_and_tracker_recovers()
    {
        var interceptor = new FailOnceProviderInterceptor();
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        interceptor.Arm();
        var failure = await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.SendAsync("wf-failure", Envelope("wf-failure", "first"), Now).AsTask());
        Assert.Equal("sending", failure.Operation);
        Assert.Equal("wf-failure", failure.Identity);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        var recovered = await fixture.Transport.SendAsync("wf-recovery", Envelope("wf-recovery", "recovery"), Now);
        Assert.Equal(1, recovered.Sequence);
    }

    [Fact]
    public async Task Concurrency_retry_exhaustion_is_bounded_and_tracker_remains_reusable()
    {
        var interceptor = new AlwaysFailConcurrencyInterceptor();
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        var failure = await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.SendAsync("wf-contention", Envelope("wf-contention", "first"), Now).AsTask());
        Assert.Equal("sending", failure.Operation);
        Assert.Equal(16, interceptor.Attempts);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Equal(0, await fixture.Transport.CountPendingAsync("wf-contention"));
    }

    [Fact]
    public async Task Indexed_projection_and_json_disagreement_fails_closed_for_addressed_item()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-1", Envelope("wf-1", "one"), Now);
        var row = await fixture.Context.CommandTransportItems.SingleAsync();
        row.PayloadJson = row.PayloadJson.Replace("\"workflowExecutionId\":\"wf-1\"", "\"workflowExecutionId\":\"wf-corrupt\"", StringComparison.Ordinal);
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.LeaseAsync("wf-1", "node-a", Now, LeaseDuration, 1).AsTask());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(3L)]
    public async Task Pending_count_over_or_under_statement_fails_closed_on_count_and_list(long corruptCount)
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-count-corrupt", Envelope("wf-count-corrupt", "one"), Now);
        await fixture.Transport.SendAsync("wf-count-corrupt", Envelope("wf-count-corrupt", "two"), Now);
        var head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.PendingCount = corruptCount;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.CountPendingAsync("wf-count-corrupt").AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.ListPendingExecutionIdsAsync(Now, 10).AsTask());
    }

    [Fact]
    public async Task Pending_summary_and_earliest_payload_corruption_fail_closed_on_read_surfaces()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-summary-corrupt", Envelope("wf-summary-corrupt", "one"), Now);
        var head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.PendingVisibleAtUtcTicks = 1;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.CountPendingAsync("wf-summary-corrupt").AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.ListPendingExecutionIdsAsync(Now, 10).AsTask());

        head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.PendingVisibleAtUtcTicks = 0;
        var item = await fixture.Context.CommandTransportItems.SingleAsync();
        item.PayloadJson = "{";
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.CountPendingAsync("wf-summary-corrupt").AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.ListPendingExecutionIdsAsync(Now, 10).AsTask());
    }

    [Fact]
    public async Task Item_sequence_ahead_of_stream_head_fails_closed_during_acknowledgement()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-sequence-corrupt", Envelope("wf-sequence-corrupt", "one"), Now);
        await fixture.Transport.SendAsync("wf-sequence-corrupt", Envelope("wf-sequence-corrupt", "two"), Now);
        var leased = await fixture.Transport.LeaseAsync("wf-sequence-corrupt", "node-a", Now, LeaseDuration, 2);
        var second = Assert.Single(leased, item => item.Sequence == 2);
        var head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.LastSequence = 1;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.AckAsync(
                "wf-sequence-corrupt",
                second.TransportItemId,
                "node-a",
                second.LeaseToken!.Value,
                Now.AddSeconds(1)).AsTask());
    }

    [Fact]
    public async Task Corrupt_pending_head_identity_cannot_be_hidden_from_send_lease_count_or_list()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-head-corrupt", Envelope("wf-head-corrupt", "one"), Now);
        var head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.ScopeKeyHash = "corrupt";
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.SendAsync("wf-head-corrupt", Envelope("wf-head-corrupt", "two"), Now).AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.LeaseAsync("wf-head-corrupt", "node-a", Now, LeaseDuration, 1).AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.CountPendingAsync("wf-head-corrupt").AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.ListPendingExecutionIdsAsync(Now, 10).AsTask());
    }

    [Fact]
    public async Task Corrupt_empty_head_tombstone_cannot_reset_the_sequence_high_water()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-tombstone-corrupt", Envelope("wf-tombstone-corrupt", "one"), Now);
        var leased = Assert.Single(await fixture.Transport.LeaseAsync("wf-tombstone-corrupt", "node-a", Now, LeaseDuration, 1));
        Assert.True(await fixture.Transport.AckAsync(
            "wf-tombstone-corrupt",
            leased.TransportItemId,
            "node-a",
            leased.LeaseToken!.Value,
            Now.AddSeconds(1)));
        var head = await fixture.Context.CommandStreamHeads.SingleAsync();
        head.WorkflowExecutionIdHash = "corrupt";
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.CountPendingAsync("wf-tombstone-corrupt").AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.LeaseAsync("wf-tombstone-corrupt", "node-a", Now, LeaseDuration, 1).AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.SendAsync("wf-tombstone-corrupt", Envelope("wf-tombstone-corrupt", "two"), Now).AsTask());
    }

    [Fact]
    public async Task Failed_save_changes_rolls_back_item_and_head_as_one_unit()
    {
        var interceptor = new FailOnMutationCommandInterceptor(1);
        await using var fixture = await Fixture.CreateAsync("scope-a", interceptor);
        interceptor.Arm();
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.SendAsync("wf-rollback", Envelope("wf-rollback", "one"), Now).AsTask());
        Assert.Equal(0, await fixture.Transport.CountPendingAsync("wf-rollback"));
        Assert.Empty(await fixture.Transport.ListPendingExecutionIdsAsync(Now, 500));
        Assert.Empty(await fixture.Context.CommandStreamHeads.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Context.CommandTransportItems.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Second_lease_statement_failure_rolls_back_item_and_head_projection()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-lease-rollback", Envelope("wf-lease-rollback", "one"), Now);
        var interceptor = new FailOnMutationCommandInterceptor(1);
        await using var failing = await fixture.ReopenAsync("scope-a", interceptor);
        interceptor.Arm();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            failing.Transport.LeaseAsync("wf-lease-rollback", "node-a", Now, LeaseDuration, 1).AsTask());

        Assert.Equal(1, await failing.Transport.CountPendingAsync("wf-lease-rollback"));
        await using var recovered = await fixture.ReopenAsync("scope-a");
        Assert.Single(await recovered.Transport.LeaseAsync("wf-lease-rollback", "node-a", Now, LeaseDuration, 1));
    }

    [Fact]
    public async Task Second_ack_statement_failure_rolls_back_item_delete_and_head_projection()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-ack-rollback", Envelope("wf-ack-rollback", "one"), Now);
        var leased = Assert.Single(await fixture.Transport.LeaseAsync("wf-ack-rollback", "node-a", Now, LeaseDuration, 1));
        var leaseToken = leased.LeaseToken!.Value;
        var interceptor = new FailOnMutationCommandInterceptor(1);
        await using var failing = await fixture.ReopenAsync("scope-a", interceptor);
        interceptor.Arm();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            failing.Transport.AckAsync("wf-ack-rollback", leased.TransportItemId, "node-a", leaseToken, Now.AddSeconds(1)).AsTask());

        Assert.Equal(1, await failing.Transport.CountPendingAsync("wf-ack-rollback"));
        await using var recovered = await fixture.ReopenAsync("scope-a");
        Assert.True(await recovered.Transport.AckAsync("wf-ack-rollback", leased.TransportItemId, "node-a", leaseToken, Now.AddSeconds(1)));
        Assert.Equal(0, await recovered.Transport.CountPendingAsync("wf-ack-rollback"));
    }

    [Fact]
    public async Task Missing_head_with_existing_item_fails_closed()
    {
        await using var fixture = await Fixture.CreateAsync("scope-a");
        await fixture.Transport.SendAsync("wf-missing-head", Envelope("wf-missing-head", "one"), Now);
        var leased = Assert.Single(await fixture.Transport.LeaseAsync("wf-missing-head", "node-a", Now, LeaseDuration, 1));
        fixture.Context.CommandStreamHeads.RemoveRange(fixture.Context.CommandStreamHeads);
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.LeaseAsync("wf-missing-head", "node-a", Now, LeaseDuration, 1).AsTask());
        await Assert.ThrowsAsync<ExecutionCommandTransportEntityFrameworkPersistenceException>(() =>
            fixture.Transport.AckAsync("wf-missing-head", leased.TransportItemId, "node-a", leased.LeaseToken!.Value, Now.AddSeconds(1)).AsTask());
    }

    private static WorkflowExecutionCommandEnvelope Envelope(string executionId, string suffix, string? partition = null) =>
        new(
            $"envelope-{suffix}",
            executionId,
            new WorkflowExecutionCommand(
                $"command-{suffix}",
                executionId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                Now,
                null,
                new Dictionary<string, string> { ["key"] = $"value-{suffix}" }),
            $"idempotency-{suffix}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            sequence: 3,
            metadata: new Dictionary<string, string> { ["meta"] = suffix },
            partition: new WorkflowExecutionPartition(partition ?? "scope-a"));

    private static async Task<ExecutionCommandTransportItem> SendFromReopenedContextAsync(Fixture fixture, int index)
    {
        await using var reopened = await fixture.ReopenAsync("scope-a");
        return await reopened.Transport.SendAsync("wf-1", Envelope("wf-1", $"sender-{index}"), Now);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string databasePath;
        private readonly bool ownsDatabase;
        private readonly string scope;

        private Fixture(SqliteConnection connection, ExecutionCommandTransportSqliteDbContext context, string scope, bool ownsDatabase)
        {
            this.connection = connection;
            databasePath = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;
            this.scope = scope;
            this.ownsDatabase = ownsDatabase;
            Context = context;
            Transport = new EfExecutionCommandTransport(context, new Accessor(scope));
        }

        public ExecutionCommandTransportSqliteDbContext Context { get; }
        public EfExecutionCommandTransport Transport { get; }

        public static async Task<Fixture> CreateAsync(string scope, params IInterceptor[] interceptors)
        {
            var path = Path.Join(Path.GetTempPath(), $"elsa-command-transport-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ExecutionCommandTransportSqliteDbContext>().UseSqlite(connection, sqlite => sqlite.MaxBatchSize(1));
            foreach (var interceptor in interceptors)
                options.AddInterceptors(interceptor);
            var context = new ExecutionCommandTransportSqliteDbContext(options.Options);
            await context.Database.EnsureCreatedAsync();
            foreach (var interceptor in interceptors.OfType<CountingInterceptor>())
                interceptor.Reset();
            return new Fixture(connection, context, scope, ownsDatabase: true);
        }

        public async Task<Fixture> ReopenAsync(string nextScope, params IInterceptor[] interceptors)
        {
            var next = new SqliteConnection(connection.ConnectionString);
            await next.OpenAsync();
            var options = new DbContextOptionsBuilder<ExecutionCommandTransportSqliteDbContext>().UseSqlite(next, sqlite => sqlite.MaxBatchSize(1));
            foreach (var interceptor in interceptors)
                options.AddInterceptors(interceptor);
            var context = new ExecutionCommandTransportSqliteDbContext(options.Options);
            return new Fixture(next, context, nextScope, ownsDatabase: false);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
            if (ownsDatabase)
                foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
        }

        private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } =
                PersistenceAccessContext.Scoped(new PersistenceScope(scope));
        }
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int Commands => Volatile.Read(ref commands);
        private int commands;
        public void Reset() => Interlocked.Exchange(ref commands, 0);
        private void Count() => Interlocked.Increment(ref commands);
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Count(); return result; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Count(); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Count(); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Count(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Count(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Count(); return ValueTask.FromResult(result); }
    }

    private sealed class FailOnceProviderInterceptor : DbCommandInterceptor
    {
        private int armed;
        private int fired;
        public int MutationCommands => Volatile.Read(ref mutationCommands);
        private int mutationCommands;
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        private void Fail(DbCommand command)
        {
            if (!IsMutation(command.CommandText))
                return;
            Interlocked.Increment(ref mutationCommands);
            if (Volatile.Read(ref armed) == 1 && Interlocked.Exchange(ref fired, 1) == 0)
                throw new SyntheticProviderException();
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Fail(command); return result; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Fail(command); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Fail(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
    }

    private sealed class FailOnMutationCommandInterceptor(int skipCount) : DbCommandInterceptor
    {
        private int armed;
        private int mutations;
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        private void Fail(DbCommand command)
        {
            if (Volatile.Read(ref armed) == 1 && IsMutation(command.CommandText) && Interlocked.Increment(ref mutations) > skipCount)
                throw new SyntheticProviderException();
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) { Fail(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { Fail(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) { Fail(command); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) { Fail(command); return ValueTask.FromResult(result); }
    }

    private sealed class AlwaysFailConcurrencyInterceptor : SaveChangesInterceptor
    {
        public int Attempts => Volatile.Read(ref attempts);
        private int attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref attempts);
            throw new DbUpdateConcurrencyException("Synthetic command transport contention.");
        }
    }

    private sealed class SyntheticProviderException() : DbException("synthetic provider failure");

    private static bool IsMutation(string sql)
    {
        var text = sql.AsSpan().TrimStart();
        return text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }
}
