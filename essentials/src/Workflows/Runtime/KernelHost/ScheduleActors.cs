using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Models = Kernel.Model.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// How the kernel coordinator reaches the schedule actors of the recurring start triggers it serves: each trigger's
/// schedule (<see cref="SetAsync"/>, <see cref="StopAsync"/>) and the run each occurrence starts (<see cref="RegisterRunAsync"/>).
/// A failure throws, which fails the step that called it.
/// </summary>
public interface IScheduleActors
{
    /// <summary>Whether the adapter requires the host to own each schedule at the publication transaction boundary.</summary>
    bool RequiresPublicationClaims => false;

    /// <summary>Registers the run each occurrence starts on the trigger's start key; null removes it.</summary>
    ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default);

    /// <summary>Creates the trigger's schedule actor, or applies <paramref name="revision"/> to it.</summary>
    ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey, CancellationToken cancellationToken = default);

    /// <summary>Disarms the trigger's schedule at <paramref name="revision"/>.</summary>
    ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default);
}

/// <summary>Selects the Runtime publication transaction as the source of schedule controls.
/// The shell's reconciler applies the committed controls to the managed Actor Foundation schedule owner.
/// </summary>
public sealed class RuntimeManagedScheduleActors : IScheduleActors
{
    public bool RequiresPublicationClaims => true;

    public ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default) => RejectDirectControl();

    public ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey,
        CancellationToken cancellationToken = default) => RejectDirectControl();

    public ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default) => RejectDirectControl();

    private static ValueTask RejectDirectControl() => ValueTask.FromException(
        new InvalidOperationException("Managed schedule controls must come from the Runtime publication transaction."));
}

/// <summary>The schedule actors on the host's ingress, in one bound scope (<see cref="ScheduleActor"/>).</summary>
public sealed class IngressScheduleActors(IIngress ingress, BoundScope scope, ICronCalendar? calendar = null) : IScheduleActors
{
    public ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default) =>
        ScheduleActor.RegisterRunAsync(ingress, scope, startKey, run, cancellationToken);

    public async ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey, CancellationToken cancellationToken = default) =>
        Admitted(await ScheduleActor.SetAsync(ingress, scope, key, revision, schedule, startKey, calendar, cancellationToken), key, "set");

    public async ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default) =>
        Admitted(await ScheduleActor.StopAsync(ingress, scope, key, revision, cancellationToken), key, "stop");

    private static void Admitted(Admission admission, string key, string what)
    {
        if (admission.Outcome == AdmissionOutcome.Refused)
            throw new InvalidOperationException($"The schedule actor '{key}' refused its {what} ({admission.Reason}).");
    }
}
