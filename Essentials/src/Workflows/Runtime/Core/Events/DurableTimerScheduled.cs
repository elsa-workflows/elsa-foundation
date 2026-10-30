using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Events;

/// <summary>A durable timer save succeeded; its stored deadline may advance the local pump's wakeup.</summary>
/// <remarks>Published deferred after persistence. This is a hint, never authority to execute a timer.</remarks>
public sealed class DurableTimerScheduled(DateTimeOffset dueTime) : IEvent
{
    public DateTimeOffset DueTime { get; } = dueTime;
}
