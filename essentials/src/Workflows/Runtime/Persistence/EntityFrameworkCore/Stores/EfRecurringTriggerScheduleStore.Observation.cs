using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRecurringTriggerScheduleStore
{
    /// <exception cref="TriggerOperationException">Stored corruption or a provider failure prevents observation; the original cause and schedule identity are retained.</exception>
    public async ValueTask<RecurringTriggerScheduleObservation?> ObserveAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        ValidateScheduleId(scheduleId, nameof(scheduleId));
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        return await EfTriggerBoundary.RunAsync<RecurringTriggerScheduleObservation?>(async () =>
        {
            var row = await context.RecurringTriggerSchedules.AsNoTracking().SingleOrDefaultAsync(item => item.Id == Id(scope, scheduleId), cancellationToken);
            if (row is null) return null;
            var schedule = Read(row, scope, scheduleId); // Also checks every claim projection pair.
            var retryAt = row.ClaimOwnerId is null && row.VisibleAfterUtcTicks is { } ticks
                ? EfRuntimeOperationalStoreSupport.FromUtcTicks(ticks, row.VisibleAfterOffsetMinutes!.Value) : (DateTimeOffset?)null;
            return new(schedule, row.FailureCount, retryAt, row.ClaimOwnerId is not null);
        }, scope, scheduleId);
    }
}
