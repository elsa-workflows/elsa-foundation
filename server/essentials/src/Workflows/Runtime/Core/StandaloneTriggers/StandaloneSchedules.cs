using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

public enum StandaloneScheduleAction { Preserve, Replace, Deactivate, Remove }
public sealed record StandaloneSchedulePlan(StandaloneScheduleAction Action, RecurringTriggerSchedule? Replacement = null);

/// <summary>Module-owned pure schedule projection; physical management applies it atomically with the resource mutation.</summary>
public interface IStandaloneSchedulePlanner
{
    bool Supports(string typeKey);
    StandaloneSchedulePlan Plan(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write,
        RecurringTriggerSchedule? current, DateTimeOffset now);
}

/// <summary>The installed source module used by the existing recurring pump for one actual resource-owned claim.</summary>
public interface IStandaloneScheduleSource
{
    bool Supports(string sourceTypeKey, string sourceFamily);
    ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(PersistenceScope scope, RecurringTriggerOccurrenceClaim claim,
        CancellationToken cancellationToken = default);
}
