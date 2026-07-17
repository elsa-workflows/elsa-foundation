using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryDurableTimerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_Find_RoundTripsTimer()
    {
        var store = new InMemoryDurableTimerStore();

        var saved = await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(5)));
        Assert.Equal("timer-1", saved.TimerId);

        var found = await store.FindAsync("wfexec-1", "timer-1");
        Assert.NotNull(found);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), found!.DueTime);
    }

    [Fact]
    public async Task Find_ReturnsNull_WhenAbsent()
    {
        var store = new InMemoryDurableTimerStore();
        Assert.Null(await store.FindAsync("wfexec-1", "missing"));
    }

    [Fact]
    public async Task Save_IsIdempotentUpsert_ExistingWins()
    {
        var store = new InMemoryDurableTimerStore();

        var first = await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(5), stimulusHash: "hash-original"));
        var deduplicated = await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(99), stimulusHash: "hash-changed"));

        Assert.Equal("hash-original", first.StimulusHash);
        Assert.Equal("hash-original", deduplicated.StimulusHash);
        Assert.Equal("hash-original", (await store.FindAsync("wfexec-1", "timer-1"))!.StimulusHash);
    }

    [Fact]
    public async Task ListDue_FiltersOrdersAndCaps()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-b", TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(Timer("timer-a", TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(Timer("timer-early", TimeSpan.FromMinutes(-5)));
        await store.SaveAsync(Timer("timer-future", TimeSpan.FromMinutes(10)));

        var due = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal(new[] { "timer-early", "timer-a", "timer-b" }, due.Select(timer => timer.TimerId));

        var limited = await store.ListDueAsync(Now, limit: 2);
        Assert.Equal(new[] { "timer-early", "timer-a" }, limited.Select(timer => timer.TimerId));
    }

    [Fact]
    public async Task ListDue_IncludesTimerDueExactlyAtInstant()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-now", TimeSpan.Zero));

        var due = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal("timer-now", Assert.Single(due).TimerId);
    }

    [Fact]
    public async Task ListDue_RejectsNonPositiveLimit()
    {
        var store = new InMemoryDurableTimerStore();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.ListDueAsync(Now, limit: 0));
    }

    [Fact]
    public async Task Delete_RemovesTimer_AndIsNoOpWhenAbsent()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));

        await store.DeleteAsync("wfexec-1", "timer-1");
        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));

        await store.DeleteAsync("wfexec-1", "timer-1");
    }

    [Fact]
    public async Task ClaimDue_ExpiryRenewalReleaseAndCompletionAreFenced()
    {
        var store = new InMemoryDurableTimerStore();
        await store.SaveAsync(Timer("timer-1", TimeSpan.FromMinutes(-1)));

        var initial = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-a", Now, TimeSpan.FromMinutes(1), limit: 1)));
        Assert.Empty(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-b", Now.AddSeconds(30), TimeSpan.FromMinutes(1), limit: 1)));

        var reclaimed = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1), limit: 1)));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await store.CompleteClaimAsync(initial)).Status);

        var renewal = await store.RenewClaimAsync(
            reclaimed,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(2));
        var renewed = Assert.IsType<RuntimeDurableTimerClaim>(renewal.Claim);
        Assert.True(renewed.Revision > reclaimed.Revision);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await store.ReleaseClaimAsync(reclaimed, Now.AddMinutes(4))).Status);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.ReleaseClaimAsync(renewed, Now.AddMinutes(4))).Status);

        Assert.Empty(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-c", Now.AddMinutes(4).AddTicks(-1), TimeSpan.FromMinutes(1), limit: 1)));
        var released = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-c", Now.AddMinutes(4), TimeSpan.FromMinutes(1), limit: 1)));
        Assert.Equal(1, released.FailureCount);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.CompleteClaimAsync(released)).Status);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied,
            (await store.CompleteClaimAsync(released)).Status);
    }

    [Fact]
    public async Task Save_RejectsNullTimer()
    {
        var store = new InMemoryDurableTimerStore();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SaveAsync(null!));
    }

    private static DurableTimer Timer(string timerId, TimeSpan dueOffset, string? stimulusHash = null) => new(
        TimerId: timerId,
        WorkflowExecutionId: "wfexec-1",
        StimulusType: "DurableTimer",
        StimulusHash: stimulusHash ?? $"hash-{timerId}",
        DueTime: Now + dueOffset,
        CreatedAt: Now,
        Input: JsonSerializer.SerializeToElement(new { reason = "delay" }),
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" });
}

public sealed class DurableTimerSchedulerTests
{
    [Fact]
    public async Task ScheduleAsync_ForwardsToStore()
    {
        var store = new InMemoryDurableTimerStore();
        var scheduler = new DurableTimerScheduler(store);

        var timer = new DurableTimer(
            TimerId: "timer-1",
            WorkflowExecutionId: "wfexec-1",
            StimulusType: "DurableTimer",
            StimulusHash: "hash-1",
            DueTime: DateTimeOffset.UnixEpoch.AddMinutes(5),
            CreatedAt: DateTimeOffset.UnixEpoch);

        var scheduled = await scheduler.ScheduleAsync(timer);

        Assert.Equal("timer-1", scheduled.TimerId);
        Assert.NotNull(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task ScheduleAsync_RejectsNullTimer()
    {
        var scheduler = new DurableTimerScheduler(new InMemoryDurableTimerStore());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await scheduler.ScheduleAsync(null!));
    }
}
