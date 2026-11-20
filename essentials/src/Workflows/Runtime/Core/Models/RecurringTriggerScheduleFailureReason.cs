namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Safe source-admission failure facts for one retained resource-owned occurrence.</summary>
public enum RecurringTriggerScheduleFailureReason
{
    /// <summary>A complete evaluation found no subscribers that could accept the occurrence.</summary>
    NoEligibleSubscribers = 1,

    /// <summary>The source could not establish durable admission; this is not evidence of zero eligibility.</summary>
    AdmissionUnavailable = 2
}
