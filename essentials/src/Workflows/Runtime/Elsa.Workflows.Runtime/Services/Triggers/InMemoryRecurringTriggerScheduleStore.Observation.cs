using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

public sealed partial class InMemoryRecurringTriggerScheduleStore
{
    public ValueTask<RecurringTriggerScheduleObservation?> ObserveAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_schedules.TryGetValue(scheduleId, out var schedule)) return ValueTask.FromResult<RecurringTriggerScheduleObservation?>(null);
            _claims.TryGetValue(scheduleId, out var claim);
            return ValueTask.FromResult<RecurringTriggerScheduleObservation?>(new(schedule, claim?.FailureCount ?? 0,
                claim?.OwnerId is null ? claim?.VisibleAfter : null, claim?.OwnerId is not null));
        }
    }
}
