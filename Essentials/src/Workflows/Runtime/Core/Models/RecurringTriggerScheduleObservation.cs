namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Actual source schedule and safe recovery facts, without exposing a claim owner or fence.</summary>
/// <remarks>RetryAt is stored released-failure eligibility. OccurrenceInFlight means a stored owner exists,
/// including an expired visibility window; it does not assert a currently live lease.</remarks>
public sealed record RecurringTriggerScheduleObservation(RecurringTriggerSchedule Schedule, int FailureCount,
    DateTimeOffset? RetryAt, bool OccurrenceInFlight);
