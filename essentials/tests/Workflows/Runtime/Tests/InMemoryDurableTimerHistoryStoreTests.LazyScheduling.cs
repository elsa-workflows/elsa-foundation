using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class InMemoryDurableTimerHistoryStoreTests
{
    [Theory]
    [InlineData("writer", false)] [InlineData("writer", true)]
    [InlineData("reader", false)] [InlineData("reader", true)]
    [InlineData("native", false)] [InlineData("native", true)]
    [InlineData("activities", false)] [InlineData("activities", true)]
    public async Task Default_timer_scheduler_leaves_unneeded_history_factories_inactive(string collaborator, bool captured)
    {
        var f = await Fixture.CreateAsync(native: false, head: false);
        await f.Executions.SaveAsync(SchedulerRun() with
        { HistoryCapturePolicy = captured ? Policy : null, HistoryCaptureEnabledAtAdmission = captured });
        var services = TimerServices(f, enabled: true);
        var type = collaborator switch
        {
            "writer" => typeof(IRuntimeHistoryObservationWriter), "reader" => typeof(IRuntimeHistoryStore),
            "native" => typeof(IRuntimeCheckpointCommitStore), _ => typeof(IActivityExecutionStateStore)
        };
        var activations = 0;
        services.RemoveAll(type);
        services.AddScoped(type, _ => { activations++; throw new IOException("unused history collaborator is unavailable"); });
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>();
        var timer = Timer(captured: false) with { ActivityExecutionId = null, ExecutionScopeId = null };
        if (captured && collaborator != "activities")
        {
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => scheduler.ScheduleAsync(timer).AsTask());
            Assert.True(activations > 0); Assert.Empty(await f.Timers.ListAsync("run"));
        }
        else
        {
            var stored = await scheduler.ScheduleAsync(timer);
            Assert.Equal(0, activations); Assert.Equal(captured, stored.HistoryResponsibility is not null);
            Assert.Equal(stored, await f.Timers.FindAsync("run", "timer"));
        }
    }
    [Theory]
    [InlineData("unavailable", "schedule")] [InlineData("malformed", "schedule")]
    [InlineData("unavailable", "predicate")] [InlineData("malformed", "predicate")]
    public async Task Timer_producer_normalizes_selected_history_capability_probe_failures(string failure, string entry)
    {
        var f = await Fixture.CreateAsync();
        Exception error = failure == "malformed" ? new InvalidDataException("invalid selected history reader") : new IOException("selected history provider unavailable");
        var services = TimerServices(f, enabled: true);
        services.RemoveAll<IRuntimeHistoryObservationWriter>();
        services.AddSingleton<IRuntimeHistoryObservationWriter>(new UnavailableTimerWriter(error));
        await using var host = services.BuildServiceProvider(); using var scope = host.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>();
        if (entry == "predicate") Assert.False(scheduler.CanRecordHistory(scope.ServiceProvider.GetRequiredService<IRuntimeHistoryObservationWriter>(), f.Reader));
        else if (failure == "malformed")
        {
            var result = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => scheduler.ScheduleAsync(Timer(captured: false)).AsTask());
            Assert.Same(error, result.InnerException);
        }
        else
        {
            var result = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => scheduler.ScheduleAsync(Timer(captured: false)).AsTask());
            Assert.Same(error, Assert.IsType<RuntimeHistoryUnavailableException>(result.InnerException).InnerException);
        }
        Assert.Empty(await f.Timers.ListAsync("run")); Assert.False(await f.Timers.HasPendingHistoryAsync("run"));
    }

    private sealed class UnavailableTimerWriter(Exception failure) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => throw failure;
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Capability refusal must not attempt an observation write.");
    }

}
