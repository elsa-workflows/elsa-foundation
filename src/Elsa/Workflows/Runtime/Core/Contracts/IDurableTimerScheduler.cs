using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Activity-facing seam that registers a durable timer. A suspending activity resolves this instead of the
/// raw <see cref="IDurableTimerStore"/> so the write path stays a single, testable operation and the store
/// contract can evolve independently of activities.
/// </summary>
public interface IDurableTimerScheduler
{
    /// <summary>Persists <paramref name="timer"/> so the hosted pump can fire it at its due time.</summary>
    ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default);
}
