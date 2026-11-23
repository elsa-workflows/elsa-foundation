using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Checks that source admission concerns this exact server-owned resource occurrence; it does not replace physical claim fencing.</summary>
public static class StandaloneScheduleAuthority
{
    public static void Validate(RecurringTriggerOccurrenceClaim claim, RuntimeStandaloneIngressSource source)
    {
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(source);
        var schedule = claim.Schedule;
        var owner = schedule.Resource ?? throw new InvalidDataException("The occurrence is not owned by a standalone trigger resource.");
        if (!schedule.IsActive || source.Scope != owner.Scope || source.TriggerId != owner.TriggerId || source.TenantId != owner.TenantId ||
            source.SourceFamily != owner.SourceFamily || source.SourceKey != schedule.BuildOccurrenceKey() ||
            source.TriggerRevision < owner.Generation || source.CanonicalPayload.GetProperty("trigger").GetProperty("typeKey").GetString() != owner.SourceTypeKey ||
            source.CanonicalPayload.GetProperty("trigger").GetProperty("configurationRevision").GetInt64() != owner.ConfigurationRevision)
            throw new InvalidDataException("The submitted source differs from its exact resource-owned occurrence generation.");
    }
}
