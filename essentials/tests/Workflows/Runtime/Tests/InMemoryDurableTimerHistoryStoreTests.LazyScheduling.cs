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
}
