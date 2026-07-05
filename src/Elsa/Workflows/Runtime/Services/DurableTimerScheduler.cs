using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IDurableTimerScheduler"/>. A thin, testable seam that forwards to the configured
/// <see cref="IDurableTimerStore"/>, isolating suspending activities from the store contract.
/// </summary>
public sealed class DurableTimerScheduler(IDurableTimerStore store) : IDurableTimerScheduler
{
    public ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        return store.SaveAsync(timer, cancellationToken);
    }
}
