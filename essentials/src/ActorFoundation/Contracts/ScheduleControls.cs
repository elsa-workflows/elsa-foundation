namespace Kernel.Foundation.Contracts;

/// <summary>The desired operation for a host-bound schedule root.</summary>
public enum ScheduleControlKind { Set, Stop }

/// <summary>The disposition of a schedule control against the stored high-water mark.</summary>
public enum ScheduleControlOutcome { Applied, Duplicate, Stale, Conflict }

/// <summary>The disposition and, after the first Set, the exact schedule root reference.</summary>
public sealed record ScheduleControlResult(ScheduleControlOutcome Outcome, ActorRef? Actor = null);

/// <summary>Trusted provider transport bound to a schedule actor type by the host.</summary>
public interface IScheduleRootControls
{
    /// <summary>Applies one desired revision. A Set carries its exact managed header and a Stop has no header.</summary>
    ValueTask<ScheduleControlResult> ApplyScheduleControlAsync(BoundScope scope, string definition, string scheduleKey,
        long revision, ScheduleControlKind kind, string? setHeader = null, CancellationToken cancellationToken = default);
}
