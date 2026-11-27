namespace Kernel.Foundation.Contracts;

/// <summary>The immutable delivery identity of a schedule occurrence already accepted by its publication authority.</summary>
public sealed record AcceptedScheduleOccurrence(string DefinitionId, string ScheduleKey, ActorRef Schedule, long ArmGeneration,
    string TimerDeliveryId, long DueAtUnixMilliseconds);

/// <summary>Host-bound admission of a workflow birth for a previously accepted occurrence.</summary>
public interface IScheduleOccurrenceAdmissions
{
    /// <summary>Commits or replays the exact workflow birth, including a birth whose actor has since retired.</summary>
    ValueTask<Admission> AdmitAsync(AcceptedScheduleOccurrence occurrence, StartSpec workflow, CancellationToken cancellationToken = default);
}
