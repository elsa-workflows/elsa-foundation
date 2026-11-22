using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed partial class DurableTimerHistoryPumpTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Retained_capture_requires_the_actual_dispatcher_history_capability(bool claimed)
    {
        await using var h = new Harness(claimed: claimed); await h.Seed("captured", head: true); await h.Schedule("captured");
        h.Dispatcher.HistorySupported = false; await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Empty(h.Dispatcher.Requests);
        await h.InScope(async services =>
        {
            var timer = await services.GetRequiredService<IDurableTimerStore>().FindAsync("captured", "timer");
            Assert.NotNull(timer); Assert.False(timer.HistoryResponsibility!.Retired);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_captured_journal_outage_does_not_starve_an_uncaptured_due_timer(bool claimed)
    {
        await using var h = new Harness(claimed: claimed); await h.Seed("captured", head: true); await h.Schedule("captured");
        await h.Seed("legacy", head: false, captured: false); await ScheduleLegacy(h, "legacy");
        h.Fault.Run = "captured"; h.Fault.Armed = true;
        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("legacy", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId);
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            Assert.NotNull(await owner.FindAsync("captured", "timer")); Assert.True(await owner.HasPendingHistoryAsync("captured"));
            Assert.Null(await owner.FindAsync("legacy", "timer"));
        });
        h.Fault.Armed = false; h.Clock.Advance(TimeSpan.FromSeconds(20)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(h.Dispatcher.Requests, request => request.WorkflowExecutionId == "captured");
        Assert.Single(h.Dispatcher.Requests, request => request.WorkflowExecutionId == "legacy");
    }

    [Theory]
    [InlineData(100)] [InlineData(10000)] [InlineData(360000)]
    public async Task Bounded_legacy_discovery_passes_a_held_prefix_even_after_retry_expiry_or_a_long_pause(int elapsedMilliseconds)
    {
        await using var h = new Harness(claimed: false, maxTimersPerTick: 1);
        await h.Seed("captured", head: true); await h.Schedule("captured");
        await h.Seed("legacy", head: false, captured: false);
        await ScheduleLegacy(h, "legacy", At.AddSeconds(1));
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        h.Fault.Run = "captured"; h.Fault.Armed = true;

        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Empty(h.Dispatcher.Requests); Assert.Equal(1, Assert.Single(h.DueQueryLimits));
        h.Clock.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("legacy", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId);
        Assert.Equal(new[] { 1, 1 }, h.DueQueryLimits);
        for (var tick = 0; tick < 3; tick++)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.Pump.ExecuteAsync(CancellationToken.None);
            Assert.Single(h.Dispatcher.Requests);
        }
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            var captured = await owner.FindAsync("captured", "timer");
            Assert.NotNull(captured); Assert.False(captured.HistoryResponsibility!.Retired);
            Assert.True(await owner.HasPendingHistoryAsync("captured")); Assert.Null(await owner.FindAsync("legacy", "timer"));
        });
        Assert.All(h.DueQueryLimits, limit => Assert.Equal(1, limit));
        h.Fault.Armed = false; h.Clock.Advance(TimeSpan.FromMinutes(6));
        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, h.Dispatcher.Requests.Count);
        Assert.Single(h.Dispatcher.Requests, request => request.WorkflowExecutionId == "captured");
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            Assert.Null(await owner.FindAsync("captured", "timer")); Assert.False(await owner.HasPendingHistoryAsync("captured"));
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_pumping_does_not_activate_an_unused_history_factory(bool claimed)
    {
        await using var h = new Harness(claimed: claimed);
        await h.Seed("legacy", head: false, captured: false); await ScheduleLegacy(h, "legacy");
        h.Fault.FactoryArmed = true; await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("legacy", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId); Assert.Equal(0, h.Fault.FactoryReads);
    }

    [Fact]
    public async Task Accepted_long_memory_timer_ids_do_not_block_bounded_legacy_dispatch()
    {
        await using var h = new Harness(claimed: false, maxTimersPerTick: 1);
        await h.Seed("legacy", head: false, captured: false); var longId = new string('a', 10000);
        await h.InScope(async services =>
        {
            var store = services.GetRequiredService<IDurableTimerStore>();
            await store.SaveAsync(new(longId, "legacy", "Timer", "long", At, At));
            await store.SaveAsync(new("z", "legacy", "Timer", "short", At, At));
        });
        await h.Pump.ExecuteAsync(CancellationToken.None); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, h.Dispatcher.Requests.Count);
        Assert.Contains(h.Dispatcher.Requests, request => request.IdempotencyKey == "timer:" + longId);
        Assert.Contains(h.Dispatcher.Requests, request => request.IdempotencyKey == "timer:z");
        Assert.All(h.DueQueryLimits, limit => Assert.Equal(1, limit));
    }

    [Fact]
    public async Task Legacy_discovery_at_the_provider_ceiling_recovers_with_a_known_retry()
    {
        await using var h = new Harness(claimed: false, maxTimersPerTick: RuntimeStorePageRequest.MaximumLimit);
        await h.Seed("captured", head: true); await h.Schedule("captured");
        h.Fault.Run = "captured"; h.Fault.Armed = true;
        await h.Pump.ExecuteAsync(CancellationToken.None); Assert.Empty(h.Dispatcher.Requests);
        await h.Seed("later", head: false, captured: false); await ScheduleLegacy(h, "later", At.AddSeconds(1));
        h.Clock.Advance(TimeSpan.FromSeconds(2)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("later", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId);
        Assert.All(h.DueQueryLimits, limit => Assert.InRange(limit, 1, RuntimeStorePageRequest.MaximumLimit));
        h.Fault.Armed = false; h.Clock.Advance(TimeSpan.FromMinutes(6)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Single(h.Dispatcher.Requests, request => request.WorkflowExecutionId == "captured");
    }

    [Fact]
    public async Task Legacy_discovery_progresses_past_a_full_failed_page_without_oversized_reads()
    {
        const int limit = RuntimeStorePageRequest.MaximumLimit;
        await using var h = new Harness(claimed: false, maxTimersPerTick: limit);
        await h.Seed("captured", head: true);
        for (var index = 0; index < limit; index++) await h.Schedule("captured", timerId: $"prefix-{index:D4}");
        await h.Seed("later", head: false, captured: false); await ScheduleLegacy(h, "later", At.AddSeconds(1));
        h.Clock.Advance(TimeSpan.FromSeconds(2)); h.Fault.Run = "captured"; h.Fault.Armed = true;
        await h.Pump.ExecuteAsync(CancellationToken.None); Assert.Empty(h.Dispatcher.Requests);
        h.Clock.Advance(TimeSpan.FromMilliseconds(100)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("later", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId);
        Assert.All(h.DueQueryLimits, requested => Assert.InRange(requested, 1, limit));
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            Assert.Equal(limit, (await owner.ListPageAsync(new("captured", limit))).Items.Count);
            Assert.True(await owner.HasPendingHistoryAsync("captured"));
        });
        h.Fault.Armed = false; h.Clock.Advance(TimeSpan.FromSeconds(20)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(limit, h.Dispatcher.Requests.Count(request => request.WorkflowExecutionId == "captured"));
        Assert.Single(h.Dispatcher.Requests, request => request.WorkflowExecutionId == "later");
        await h.InScope(async services =>
            Assert.False(await ((IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>()).HasPendingHistoryAsync("captured")));
    }

    [Fact]
    public async Task An_eligible_legacy_retry_keeps_its_place_ahead_of_newer_arrivals()
    {
        await using var h = new Harness(claimed: false, maxTimersPerTick: 1);
        await h.Seed("captured", head: true); await h.Schedule("captured");
        h.Clock.Advance(TimeSpan.FromSeconds(2)); h.Fault.Armed = true;
        await h.Pump.ExecuteAsync(CancellationToken.None); Assert.Empty(h.Dispatcher.Requests);
        h.Clock.Advance(TimeSpan.FromSeconds(20)); h.Fault.Armed = false;
        await h.Seed("arrival", head: false, captured: false); await ScheduleLegacy(h, "arrival", h.Clock.GetUtcNow());
        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal("captured", Assert.Single(h.Dispatcher.Requests).WorkflowExecutionId);
        await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(new[] { "captured", "arrival" }, h.Dispatcher.Requests.Select(request => request.WorkflowExecutionId));
        Assert.Equal(new[] { 1, 1, 1 }, h.DueQueryLimits);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_retained_captured_timer_survives_an_unavailable_history_factory(bool claimed)
    {
        await using var h = new Harness(claimed: claimed); await h.Seed("captured", head: true); await h.Schedule("captured");
        h.Fault.FactoryArmed = true; await h.Pump.ExecuteAsync(CancellationToken.None); Assert.Empty(h.Dispatcher.Requests);
        await h.InScope(async services =>
            Assert.True(await ((IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>()).HasPendingHistoryAsync("captured")));
    }

    private static Task ScheduleLegacy(Harness h, string run, DateTimeOffset? due = null) => h.InScope(async services =>
    {
        var timer = await services.GetRequiredService<IDurableTimerScheduler>().ScheduleAsync(new("timer", run, "Timer", "timer", due ?? At, At));
        Assert.Null(timer.HistoryResponsibility);
    });

    // The real owner retains the source journal; this conduit uses its original list/delete dispatch path.
    private sealed class LegacyHistoryOwner(IDurableTimerHistoryStore inner, List<int> dueQueryLimits) : IDurableTimerHistoryStore
    {
        public bool SupportsHistoryResponsibility => inner.SupportsHistoryResponsibility;
        public bool CanInspectHistorySources(IRuntimeHistoryStore reader) => inner.CanInspectHistorySources(reader);
        public bool CanDischargeUnrecordedCompletion(IRuntimeCheckpointCommitStore native) => inner.CanDischargeUnrecordedCompletion(native);
        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken token = default) => inner.SaveAsync(timer, token);
        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset at, int limit, CancellationToken token = default)
        { dueQueryLimits.Add(limit); return inner.ListDueAsync(at, limit, token); }
        public ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageAsync(DateTimeOffset at, RuntimeStorePageRequest page, CancellationToken token = default)
        { dueQueryLimits.Add(page.Limit); return inner.ListDuePageAsync(at, page, token); }
        public ValueTask<DurableTimer?> FindAsync(string run, string timer, CancellationToken token = default) => inner.FindAsync(run, timer, token);
        public ValueTask<RuntimeStorePage<DurableTimer>> ListPageAsync(DurableTimerPageQuery query, CancellationToken token = default) => inner.ListPageAsync(query, token);
        public ValueTask DeleteAsync(string run, string timer, CancellationToken token = default) => inner.DeleteAsync(run, timer, token);
        public ValueTask<bool> HasPendingHistoryAsync(string run, CancellationToken token = default) => inner.HasPendingHistoryAsync(run, token);
        public ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken token = default) => inner.ListPendingHistoryAsync(query, token);
        public ValueTask<bool> AcknowledgeHistoryAsync(string run, string timer, string entry, CancellationToken token = default) => inner.AcknowledgeHistoryAsync(run, timer, entry, token);
        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim, RuntimeHistoryObservation observation, CancellationToken token = default) => inner.CompleteClaimWithHistoryAsync(claim, observation, token);
        public ValueTask RetireWithHistoryAsync(string run, string timer, RuntimeHistoryObservation observation, CancellationToken token = default) => inner.RetireWithHistoryAsync(run, timer, observation, token);
    }
}
