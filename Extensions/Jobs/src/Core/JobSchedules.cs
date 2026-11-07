namespace Elsa.Jobs.Core;

/// <summary>UTC recurrence expression, interpreted by the installed calculator; no workflow trigger identity.</summary>
public sealed record JobRecurrence(
    JobRecurrenceKind Kind,
    string Expression);

/// <summary>Pinned request template and recurrence; an awaited result address cannot be reused for recurring jobs.</summary>
public sealed record JobScheduleDefinition(
    string ScheduleId,
    JobRequest Template,
    JobRecurrence Recurrence,
    DateTimeOffset FirstOccurrence,
    JobMissedOccurrencePolicy MissedPolicy);

/// <summary>Retained schedule metadata. An unavailable definition remains protected with its exact identity and owed cursor.</summary>
public sealed record JobScheduleSnapshot(
    JobScheduleDefinition? Definition,
    long Revision,
    DateTimeOffset NextOccurrence,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ScheduleId,
    JobDiagnostic? UnavailableDiagnostic = null);

/// <summary>Creates or revision-checks a schedule without discarding accepted jobs or unmaterialized due responsibility.</summary>
public sealed record JobSaveScheduleRequest(
    JobScope Scope,
    JobScheduleDefinition Definition,
    long ExpectedRevision,
    JobOperation Operation);

/// <summary>Revision-checked schedule control; disabling retains the due cursor and all accepted invocations.</summary>
public sealed record JobSetScheduleEnabledRequest(
    JobScope Scope,
    string ScheduleId,
    bool Enabled,
    long ExpectedRevision,
    JobOperation Operation);

/// <summary>Bounded materialization under provider time, serialized with pause and schedule updates.</summary>
public sealed record JobMaterializationRequest(
    JobScope Scope,
    int MaximumOccurrences);

/// <summary>Confirms accepted JobId and any coalesced unmaterialized interval; never deletes an accepted backlog.</summary>
public sealed record JobOccurrenceReceipt(
    string ScheduleId,
    long ScheduleRevision,
    DateTimeOffset ScheduledAt,
    string JobId,
    long CoalescedCount,
    DateTimeOffset? CoalescedThrough);

/// <summary>Mutation confirmation for a bounded materialization transaction.</summary>
public sealed record JobMaterializationResult(
    JobCommandStatus Status,
    IReadOnlyList<JobOccurrenceReceipt> Occurrences);

/// <summary>Initial recurrence forms; both use UTC instants.</summary>
public enum JobRecurrenceKind
{
    Interval = 0,
    Cron = 1
}

/// <summary>Applies only to unmaterialized schedule responsibility, never accepted jobs.</summary>
public enum JobMissedOccurrencePolicy
{
    CoalesceOne = 0,
    ReplayEveryOccurrence = 1
}

/// <summary>Replacement contract: pure UTC recurrence arithmetic; schedule authority owns cursors, missed policy and acceptance.</summary>
public interface IJobRecurrenceCalculator
{
    /// <exception cref="JobException">The recurrence is invalid, or its arithmetic is temporarily unavailable; unavailable arithmetic retains retryable schedule responsibility.</exception>
    DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after);
}
