using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>One bounded hint shared by timer producers and the shell's pump.</summary>
internal sealed class DurableTimerWakeup
{
    private readonly object _gate = new();
    private long _version;
    private DateTimeOffset? _deadline;
    internal event Action? Changed;

    internal (long Version, DateTimeOffset? Deadline) Snapshot
    {
        get { lock (_gate) return (_version, _deadline); }
    }

    public void TimerScheduled(DateTimeOffset dueTime)
    {
        lock (_gate)
        {
            _version++;
            _deadline = Earlier(_deadline, dueTime);
        }
        Changed?.Invoke();
    }

    internal void Reconcile(long version, DateTimeOffset? deadline)
    {
        lock (_gate)
            // Preserve a save racing the storage read.
            _deadline = _version == version ? deadline : Earlier(_deadline, deadline);
    }

    private static DateTimeOffset? Earlier(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null || first <= second ? first : second;
}

internal sealed class DurableTimerScheduledHandler(DurableTimerWakeup wakeup) : IEventHandler<DurableTimerScheduled>
{
    public Task Handle(DurableTimerScheduled notification, CancellationToken cancellationToken)
    {
        wakeup.TimerScheduled(notification.DueTime);
        return Task.CompletedTask;
    }
}
