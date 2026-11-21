using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfDurableTimerStoreTests
{
    [Fact]
    public async Task Due_memory_pages_keep_accepted_long_identities_within_the_continuation_bound()
    {
        var store = new InMemoryDurableTimerStore(); var now = DateTimeOffset.UtcNow; var longId = new string('a', 10000);
        await store.SaveAsync(Timer("run", longId, now)); await store.SaveAsync(Timer("run", "z", now));
        var first = await store.ListDuePageAsync(now, new(1));
        Assert.Single(first.Items); Assert.NotNull(first.NextContinuationToken);
        Assert.InRange(first.NextContinuationToken.Length, 1, RuntimeStorePageRequest.MaximumContinuationTokenLength);
        var last = await store.ListDuePageAsync(now, new(1, first.NextContinuationToken));
        Assert.Single(last.Items); Assert.Null(last.NextContinuationToken);
        Assert.Equal(new[] { longId, "z" }, first.Items.Concat(last.Items).Select(timer => timer.TimerId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Due_pages_preserve_ties_after_deletion_and_bind_the_cutoff_and_limit(bool ef)
    {
        await using var database = await TestDatabase.CreateAsync(); await using var fixture = database.Open("due-pages");
        IDurableTimerHistoryStore store = ef ? fixture.Store : new InMemoryDurableTimerStore();
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var run in new[] { "run-1", "run-2", "run-3" }) await store.SaveAsync(Timer(run, "same", now));
        await store.SaveAsync(Timer("future", "same", now.AddSeconds(1)));
        var first = await store.ListDuePageAsync(now, new(1));
        Assert.Equal("run-1", Assert.Single(first.Items).WorkflowExecutionId); Assert.NotNull(first.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListDuePageAsync(now.AddTicks(1), new(1, first.NextContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListDuePageAsync(now, new(2, first.NextContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListDuePageAsync(now, new(1, "malformed")).AsTask());
        await store.DeleteAsync("run-1", "same");
        var second = await store.ListDuePageAsync(now, new(1, first.NextContinuationToken));
        Assert.Equal("run-2", Assert.Single(second.Items).WorkflowExecutionId); Assert.NotNull(second.NextContinuationToken);
        var last = await store.ListDuePageAsync(now, new(1, second.NextContinuationToken));
        Assert.Equal("run-3", Assert.Single(last.Items).WorkflowExecutionId); Assert.Null(last.NextContinuationToken);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Due_pages_continue_beyond_a_full_provider_batch(bool ef)
    {
        await using var database = await TestDatabase.CreateAsync(); await using var fixture = database.Open("due-ceiling");
        IDurableTimerHistoryStore store = ef ? fixture.Store : new InMemoryDurableTimerStore();
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await store.SaveAsync(Timer("run", $"timer-{index:D4}", now));
        var first = await store.ListDuePageAsync(now, new(RuntimeStorePageRequest.MaximumLimit));
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit, first.Items.Count); Assert.NotNull(first.NextContinuationToken);
        var last = await store.ListDuePageAsync(now, new(RuntimeStorePageRequest.MaximumLimit, first.NextContinuationToken));
        Assert.Equal("timer-0500", Assert.Single(last.Items).TimerId); Assert.Null(last.NextContinuationToken);
    }

    [Fact]
    public async Task Due_page_continuations_cannot_cross_persistence_scopes()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var first = database.Open("due-scope-a"); await using var second = database.Open("due-scope-b");
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await first.Store.SaveAsync(Timer("run", "a", now)); await first.Store.SaveAsync(Timer("run", "b", now));
        await second.Store.SaveAsync(Timer("run", "a", now)); await second.Store.SaveAsync(Timer("run", "b", now));
        var page = await first.Store.ListDuePageAsync(now, new(1)); Assert.NotNull(page.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => second.Store.ListDuePageAsync(now, new(1, page.NextContinuationToken)).AsTask());
    }
}
