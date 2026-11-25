using Kernel.WorkflowActor;
using Models = Kernel.Model.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The schedule actors of a host that runs none: every call throws with <paramref name="reason"/>, so a Timer or Cron
/// publish fails at serving with that reason and the switch reverts. A host whose foundation hosts the schedule actor
/// composes <see cref="IngressScheduleActors"/> in its place.
/// </summary>
public sealed class UnavailableScheduleActors(string reason) : IScheduleActors
{
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Name why schedules are unavailable.", nameof(reason)) : reason;

    public ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default) => Refuse();

    public ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey, CancellationToken cancellationToken = default) => Refuse();

    public ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default) => Refuse();

    private ValueTask Refuse() => ValueTask.FromException(new InvalidOperationException(Reason));
}
