using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>Routes controlled timers away from every ordinary provider query before they are persisted.</summary>
internal sealed class TestAwareDurableTimerScheduler(
    WorkflowTestTimePolicyResolver policies,
    IControlledWorkflowTimeStore controlled,
    IDurableTimerScheduler ordinary,
    TimeProvider wall) : IDurableTimerScheduler
{
    public async ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        var policy = await policies.ResolveAsync(timer.WorkflowExecutionId, timer.ActivityExecutionId, null, cancellationToken);
        if (policy is null || policy.Mode == WorkflowTestTimeMode.Wall)
        {
            if (timer.BusinessTime?.IsControlled == true)
                throw WorkflowTestTimePolicyResolver.Refuse("test-timer-clock-mismatch", "A controlled clock cannot schedule an ordinary timer.");
            return await ordinary.ScheduleAsync(timer, cancellationToken);
        }
        if (timer.BusinessTime is not { } stamp || !policy.MatchesClock(stamp) || stamp.Instant != timer.CreatedAt)
            throw WorkflowTestTimePolicyResolver.Refuse("test-timer-clock-required", "The timer was not computed from its exact admitted controlled clock.");
        var clock = await controlled.FindClockAsync(policy, cancellationToken);
        if (clock is null || clock.Closed || !policy.Matches(clock.Policy))
            throw WorkflowTestTimePolicyResolver.Refuse("test-clock-unavailable", "The admitted controlled clock is unavailable.");
        // The provider validates retained read-revision evidence atomically with its normal scope fence.
        // A legitimate clock read followed by advancement may save an already-due timer and must retain a scan.
        return await controlled.ScheduleAsync(policy, timer, wall.GetUtcNow(), cancellationToken);
    }
}
