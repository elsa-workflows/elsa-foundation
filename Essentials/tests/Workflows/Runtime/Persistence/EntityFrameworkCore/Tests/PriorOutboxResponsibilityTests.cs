using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Fact]
    public async Task Durable_responsibility_lookup_crosses_a_full_page_of_excluded_history_before_proving_settlement()
    {
        await using var harness = await Harness.Create(true);
        var owed = Enumerable.Range(0, 16).Select(i => Pending("owed-" + i, true, kind: "child.cancel"))
            .OrderBy(item => EfRuntimePostCommitOutboxStore.RowId(Scope.Value, item.OutboxItemId), StringComparer.Ordinal).Last();
        var owedKey = EfRuntimePostCommitOutboxStore.RowId(Scope.Value, owed.OutboxItemId);
        var saved = 0;
        for (var candidate = 0; saved < RuntimeStorePageRequest.MaximumLimit; candidate++)
        {
            var history = Pending("old-result-" + candidate, true, kind: "worker.result");
            if (string.CompareOrdinal(EfRuntimePostCommitOutboxStore.RowId(Scope.Value, history.OutboxItemId), owedKey) >= 0) continue;
            await harness.SavePending(history); saved++;
        }
        await harness.SavePending(owed);
        Assert.Equal(owed.OutboxItemId, (await harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result"))!.OutboxItemId);
        var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(1), 1,
            workflowExecutionId: "publisher", intentKind: "child.cancel")));
        await harness.Claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.Null(await harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result"));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("scope-hash")]
    [InlineData("execution-hash")]
    [InlineData("execution-key")]
    public async Task Durable_responsibility_lookup_cannot_call_a_corrupt_owned_projection_absent(string projection)
    {
        await using var harness = await Harness.Create(true);
        await harness.SavePending(Pending("earlier-checkpoint", true, kind: "child.cancel"));
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        switch (projection)
        {
            case "status": row.Status = (int)RuntimePostCommitOutboxStatus.Delivered; break;
            case "scope-hash": row.ScopeKeyHash = "damaged-scope-projection"; break;
            case "execution-hash": row.WorkflowExecutionIdHash = "damaged-execution-projection"; break;
            case "execution-key": row.WorkflowExecutionId = "damaged-execution-projection"; break;
        }
        await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result").AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Durable_responsibility_lookup_excludes_only_the_exact_ordinal_result_kind(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.SavePending(Pending("case-distinct-kind", true, kind: "WORKER.RESULT"));
        Assert.NotNull(await harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result"));
        Assert.Null(await harness.Lookup.FindUndeliveredDurableAsync("publisher", "WORKER.RESULT"));
    }

    [Theory]
    [InlineData(false, RuntimePostCommitOutboxStatus.Pending)]
    [InlineData(true, RuntimePostCommitOutboxStatus.Pending)]
    [InlineData(false, RuntimePostCommitOutboxStatus.Delivering)]
    [InlineData(true, RuntimePostCommitOutboxStatus.Delivering)]
    [InlineData(false, RuntimePostCommitOutboxStatus.FailedRetryable)]
    [InlineData(true, RuntimePostCommitOutboxStatus.FailedRetryable)]
    [InlineData(false, RuntimePostCommitOutboxStatus.FailedFinal)]
    [InlineData(true, RuntimePostCommitOutboxStatus.FailedFinal)]
    [InlineData(false, RuntimePostCommitOutboxStatus.Delivered)]
    [InlineData(true, RuntimePostCommitOutboxStatus.Delivered)]
    public async Task Durable_responsibility_lookup_includes_every_undelivered_state_regardless_of_due_time(bool ef,
        RuntimePostCommitOutboxStatus status)
    {
        await using var harness = await Harness.Create(ef);
        await harness.SeedPending(kind: "child.cancel");
        if (status != RuntimePostCommitOutboxStatus.Pending)
        {
            var claim = Assert.Single(await harness.Claims.ClaimAsync(new("owner", Now, TimeSpan.FromMinutes(5), 1)));
            if (status != RuntimePostCommitOutboxStatus.Delivering)
                await harness.Claims.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId, status, Now,
                    status == RuntimePostCommitOutboxStatus.Delivered ? null : "A retained cancellation delivery failure."));
        }
        var lookup = (IPostCommitOutboxLookupStore)harness.Reopen();
        var source = await lookup.FindUndeliveredDurableAsync("publisher", "worker.result");
        if (status == RuntimePostCommitOutboxStatus.Delivered) Assert.Null(source);
        else
        {
            Assert.Equal("commit:send", source!.OutboxItemId);
            Assert.Equal(status, source.Status);
            if (status == RuntimePostCommitOutboxStatus.Delivering) Assert.True(source.DeliveryVisibleAfter > Now);
            if (status == RuntimePostCommitOutboxStatus.FailedRetryable) Assert.True(source.AvailableAt > Now);
        }
        Assert.Null(await lookup.FindUndeliveredDurableAsync("publisher", "child.cancel"));
        Assert.Null(await lookup.FindUndeliveredDurableAsync("other-worker", "worker.result"));
        var foreign = (IPostCommitOutboxLookupStore)harness.Reopen(new("other-scope"));
        Assert.Null(await foreign.FindUndeliveredDurableAsync("publisher", "worker.result"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Durable_responsibility_lookup_includes_sources_not_available_until_the_future(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var original = Pending("earlier-checkpoint", true, kind: "child.cancel");
        var delayed = new RuntimePostCommitOutboxItem(original.OutboxItemId, original.Intent, original.Status, original.RecordedAt,
            Now.AddYears(1), original.RetryPolicy, 0, null, null, null, null, null, 0, null, 1, Scope, null);
        await harness.SavePending(delayed);
        Assert.Empty(await harness.Outbox.GetDeliverableAsync(new(Now, 1, "publisher")));
        Assert.Equal(delayed.OutboxItemId, (await harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result"))!.OutboxItemId);
    }

    [Fact]
    public async Task Durable_responsibility_lookup_refuses_a_caller_owned_EF_transaction()
    {
        await using var harness = await Harness.Create(true);
        await harness.SeedPending(kind: "child.cancel");
        await using var transaction = await harness.Context!.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result").AsTask());
        await transaction.RollbackAsync();
        Assert.NotNull(await harness.Lookup.FindUndeliveredDurableAsync("publisher", "worker.result"));
    }

    [Fact]
    public async Task Durable_responsibility_lookup_refuses_a_legacy_provider_instead_of_assuming_settlement()
    {
        IPostCommitOutboxLookupStore lookup = new LegacyResponsibilityLookup();
        await Assert.ThrowsAsync<NotSupportedException>(() => lookup.FindUndeliveredDurableAsync("publisher", "worker.result").AsTask());
    }

    private sealed class LegacyResponsibilityLookup : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(null);
    }
}
