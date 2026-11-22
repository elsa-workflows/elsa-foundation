using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class RecurringScheduleWriteVersion
{
    internal static string For(RuntimeDbContext context, RecurringTriggerSchedule schedule, string? storedVersion) =>
        RuntimeSchemaWriteVersion.For(context, RuntimeOperationalStateEfModule.SchemaFamily,
            schedule.Resource is null ? RuntimeOperationalStateEfModule.SchemaVersion : RuntimeOperationalStateEfModule.StandaloneScheduleSchemaVersion, storedVersion);
}
