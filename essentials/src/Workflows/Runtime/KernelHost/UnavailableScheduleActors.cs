using Kernel.WorkflowActor;
using Models = Kernel.Model.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The schedule actors of a host that runs none. Such a host also gives its <see cref="KernelServing"/> the same reason, so a
/// Timer or Cron publish is refused at the serving check and nothing reaches here; should one still, registering a run or
/// setting a schedule throws with <paramref name="reason"/>. Stopping a schedule and unregistering a run do nothing, since
/// none runs, so unpublishing never fails on them. A host whose foundation hosts the schedule actor composes
/// <see cref="IngressScheduleActors"/> in its place.
/// </summary>
public sealed class UnavailableScheduleActors(string reason) : IScheduleActors
{
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Name why schedules are unavailable.", nameof(reason)) : reason;

    public ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default) =>
        run is null ? ValueTask.CompletedTask : Refuse();

    public ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey, CancellationToken cancellationToken = default) => Refuse();

    public ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private ValueTask Refuse() => ValueTask.FromException(new InvalidOperationException(Reason));
}
