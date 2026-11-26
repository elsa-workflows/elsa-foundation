using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class InMemoryDurableTimerHistoryStoreTests
{
    [Fact]
    public async Task Default_timer_scheduler_retains_original_policy_after_current_capture_is_disabled()
    {
        var f = await Fixture.CreateAsync();
        await using var provider = TimerServices(f, false).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>();
        Assert.True(scheduler.CanRecordHistory(f.Writer, f.Reader));
        var original = Timer(captured: false) with { ActivityExecutionId = null, ExecutionScopeId = null };
        var stored = await scheduler.ScheduleAsync(original);
        Assert.True(Policy.IsEquivalentTo(stored.HistoryResponsibility!.Policy));
        var scheduled = Assert.Single(stored.HistoryResponsibility.PendingEvents);
        Assert.Equal("TimerScheduled", scheduled.Name); Assert.False(scheduled.Metadata.ContainsKey("secret"));
        using var another = provider.CreateScope();
        var next = Assert.IsAssignableFrom<IDurableTimerHistoryStore>(another.ServiceProvider.GetRequiredService<IDurableTimerStore>());
        Assert.NotSame(scope.ServiceProvider.GetRequiredService<IDurableTimerStore>(), next);
        Assert.True(next.CanInspectHistorySources(f.Reader)); Assert.True(await next.HasPendingHistoryAsync("run"));
        Assert.Equal(stored, await next.FindAsync("run", "timer"));
    }

    [Fact]
    public async Task Default_timer_scheduler_keeps_a_genuine_uncaptured_choice_when_current_capture_is_enabled()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await f.Executions.SaveAsync(SchedulerRun() with { HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false });
        await using var provider = TimerServices(f, true).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>().ScheduleAsync(Timer(captured: false));
        Assert.Null(stored.HistoryResponsibility); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Timer_scheduler_reads_the_actual_coalescing_admission_before_the_first_native_row()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var queue = new Elsa.Workflows.Runtime.Services.Scheduler.InMemoryWorkflowSchedulerWorkQueue();
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var session = new RuntimeCoalescingSession("run", queue, new());
        session.BufferDeferred(new("start", new("start", "WorkflowStarted", "run", At, [], new Dictionary<string, string>()),
            new(new("run", RuntimeStateChangeOperation.Upsert, SchedulerRun(), new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>()));
        using var active = sessions.Push(session);
        var overlay = new CoalescingWorkflowExecutionStateStore(new(f.Executions), sessions);
        var scheduler = new DurableTimerScheduler(f.Timers)
            { HistoryProviders = () => new(overlay, null, f.Commits, f.Writer, f.Reader, f.Access) };
        var stored = await scheduler.ScheduleAsync(Timer(captured: false) with { ActivityExecutionId = null, ExecutionScopeId = null });
        Assert.True(Policy.IsEquivalentTo(stored.HistoryResponsibility!.Policy));
        Assert.Null(await f.Executions.FindAsync("run")); Assert.True(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Default_timer_registration_preserves_a_foreign_store_and_refuses_required_scheduling_without_side_effects()
    {
        var f = await Fixture.CreateAsync(); var foreign = new InMemoryDurableTimerStore();
        var services = TimerServices(f, true, foreign);
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Same(foreign, scope.ServiceProvider.GetRequiredService<IDurableTimerStore>());
        var scheduler = scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>();
        Assert.False(scheduler.CanRecordHistory(f.Writer, f.Reader));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => scheduler.ScheduleAsync(Timer(captured: false)).AsTask());
        Assert.Empty(await foreign.ListAsync("run")); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public async Task Timer_scheduler_cannot_qualify_an_unrelated_history_reader_or_legacy_constructor()
    {
        var f = await Fixture.CreateAsync(); var other = new Fixture();
        var scheduler = new DurableTimerScheduler(f.Timers)
            { HistoryProviders = () => new(f.Executions, null, f.Commits, f.Writer, other.Reader, f.Access) };
        Assert.False(scheduler.CanRecordHistory(f.Writer, other.Reader));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => scheduler.ScheduleAsync(Timer(captured: false)).AsTask());
        Assert.False(new DurableTimerScheduler(f.Timers).CanRecordHistory(f.Writer, f.Reader));
        Assert.False(((IRuntimeHistoryTimerSupport)new LegacyTimerSupport()).CanCapture);
        Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    [Fact]
    public void Default_timer_factory_remains_replaceable_by_an_explicit_backend_without_claiming_foreign_registrations()
    {
        var defaults = new ServiceCollection(); defaults.AddRuntimeDurableTimerScheduler();
        DurableTimerStoreBackend.EnsureNoUnownedRegistrations(defaults);
        var foreign = new ServiceCollection(); foreign.AddSingleton<IDurableTimerStore>(new InMemoryDurableTimerStore());
        foreign.AddRuntimeDurableTimerScheduler();
        Assert.Throws<InvalidOperationException>(() => DurableTimerStoreBackend.EnsureNoUnownedRegistrations(foreign));
    }

    [Fact]
    public async Task Partial_history_composition_retains_the_usable_legacy_default_instead_of_an_unqualified_memory_adapter()
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        var services = TimerServices(f, true);
        var execution = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionStateStore));
        services.Remove(execution);
        services.AddSingleton<IWorkflowExecutionStateStore>(new Elsa.Workflows.Runtime.Services.Executions.InMemoryWorkflowExecutionStateStore());
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDurableTimerStore>();
        Assert.False(((IDurableTimerHistoryStore)store).SupportsHistoryResponsibility);
        var scheduled = await scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>().ScheduleAsync(Timer(captured: false));
        Assert.Null(scheduled.HistoryResponsibility); Assert.Equal(scheduled, await store.FindAsync("run", "timer"));
        using var later = provider.CreateScope(); Assert.Same(store, later.ServiceProvider.GetRequiredService<IDurableTimerStore>());
    }

    private static WorkflowExecutionState SchedulerRun() => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope-a", new Dictionary<string, string>())
        { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
    private static ServiceCollection TimerServices(Fixture f, bool enabled, IDurableTimerStore? explicitStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(f.State); services.AddSingleton<IPersistenceAccessContextAccessor>(f.Access);
        services.AddSingleton<IWorkflowExecutionStateStore>(f.Executions); services.AddSingleton<IExecutionLivenessStateStore>(f.Liveness);
        services.AddSingleton<IRuntimeCheckpointCommitStore>(f.Commits); services.AddSingleton<IRuntimeHistoryStore>(f.Reader);
        services.AddSingleton<IRuntimeHistoryObservationWriter>(f.Writer); services.AddSingleton<TimeProvider>(f.Clock);
        services.AddSingleton(new RuntimeHistoryOptions { Enabled = enabled }); services.AddSingleton<IDeferredEventPublisher>(new TimerEvents());
        if (explicitStore is not null) services.AddSingleton(explicitStore);
        services.AddRuntimeDurableTimerScheduler(); return services;
    }
    private sealed class LegacyTimerSupport : IRuntimeHistoryTimerSupport;
    private sealed class TimerEvents : IDeferredEventPublisher
    { public Task Publish(IEvent notification, CancellationToken cancellationToken = default) => Task.CompletedTask; }
}
