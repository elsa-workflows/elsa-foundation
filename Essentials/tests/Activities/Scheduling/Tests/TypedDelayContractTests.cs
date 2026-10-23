using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

public sealed class TypedDelayContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Delay_UsesPlainInputAndReturnsTypedStateAndTrigger()
    {
        var scheduler = new RecordingTimerScheduler();
        var activity = new Delay(scheduler, new FixedClock(Now))
        {
            Duration = TimeSpan.FromMinutes(2)
        };

        var transition = await ((IStatefulActivity<ActivityUnit, DelayState, DurableTimerElapsed>)activity)
            .ExecuteAsync(ExecutionContext("attempt-1"));

        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition>(transition);
        Assert.Equal(new DelayState("timer:invocation-1"), suspension.State);
        var registration = Assert.Single(suspension.Registrations);
        Assert.Equal("resume-target:durable-timer", registration.ResumeTargetKey);
        Assert.Equal("DurableTimer", registration.StimulusType);
        Assert.Equal("timer:invocation-1", registration.StimulusHash);

        var timer = Assert.Single(scheduler.Timers);
        Assert.Equal(Now.AddMinutes(2), timer.DueTime);
        Assert.Equal(registration.PayloadType, timer.PayloadType);
        Assert.Equal(Delay.TimerTriggerProviderId, timer.ProviderId);
        Assert.Equal("timer:invocation-1", timer.Input!.Value.GetProperty("TimerId").GetString());
    }

    [Fact]
    public async Task Delay_ResumesOnFreshObjectAndReturnsOneCompletion()
    {
        var scheduler = new RecordingTimerScheduler();
        var activity = new Delay(scheduler, new FixedClock(Now));

        var transition = await ((IStatefulActivity<ActivityUnit, DelayState, DurableTimerElapsed>)activity)
            .ResumeAsync(new ActivityResumeContext<DelayState, DurableTimerElapsed>(
                ExecutionContext("attempt-2"),
                new DelayState("timer:invocation-1"),
                new DurableTimerElapsed("timer:invocation-1"),
                "registration-1",
                "delivery-1"));

        Assert.IsAssignableFrom<IActivityCompletionTransition>(transition);
        Assert.Empty(scheduler.Timers);
    }

    private static ActivityExecutionContext ExecutionContext(string attemptId) =>
        new("workflow-1", "invocation-1", attemptId, "node-delay", CancellationToken.None);

    private sealed class RecordingTimerScheduler : IDurableTimerScheduler
    {
        public List<DurableTimer> Timers { get; } = [];

        public ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
        {
            Timers.Add(timer);
            return ValueTask.FromResult(timer);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
