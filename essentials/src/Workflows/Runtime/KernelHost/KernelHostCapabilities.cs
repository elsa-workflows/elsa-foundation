namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// What a kernel host's foundation runs. A workflow that needs a capability the host lacks is refused where it is mapped
/// (<see cref="NativeExecutableMapper.Map(Elsa.Workflows.Runtime.Core.Models.WorkflowExecutable, KernelHostCapabilities)"/>),
/// so publishing reports it as <c>definition_unsupported</c>, naming the node and what to change.
/// </summary>
/// <param name="Schedules">Schedule actors, which run Timer and Cron triggers.</param>
/// <param name="Timers">Timers, self-sends with a due time, which a Delay waits on.</param>
/// <param name="Children">Child workflows, which a Dispatch Workflow starts, waited or not.</param>
/// <param name="SelfSends">
/// Self-sends, with which a turn that reaches the activity cap continues in the next. Without them a host runs a burst on
/// to its next wait (<c>NativeExecutionOptions.SplitsBursts</c>), bounded by the state and history size limits.
/// </param>
public sealed record KernelHostCapabilities(bool Schedules = true, bool Timers = true, bool Children = true, bool SelfSends = true)
{
    /// <summary>A host whose foundation runs everything a published workflow may need.</summary>
    public static KernelHostCapabilities All { get; } = new();

    /// <summary>Why a Timer or Cron is refused on a host without schedule actors.</summary>
    public const string SchedulesUnavailable =
        "Timer and Cron triggers don't run on this host yet. Remove the Timer or Cron, or start the workflow from an HTTP endpoint, an event or a Dispatch.";

    /// <summary>Why a Delay is refused on a host without timers.</summary>
    public const string DelayUnavailable =
        "Delay doesn't run on this host yet. Remove it, or wait for an event or an HTTP request instead.";

    /// <summary>Why a Dispatch Workflow is refused on a host without child workflows.</summary>
    public const string DispatchUnavailable =
        "Dispatch Workflow doesn't run on this host yet. Remove it, or start the other workflow from an HTTP endpoint or an event.";

    /// <summary>Why this host refuses a node of this Native activity type, or null when it runs it.</summary>
    internal string? Refusal(Type activity) =>
        !Schedules && (activity == typeof(Kernel.Native.Timer) || activity == typeof(Kernel.Native.Cron)) ? SchedulesUnavailable :
        !Timers && activity == typeof(Kernel.Native.Delay) ? DelayUnavailable :
        !Children && activity == typeof(Kernel.Native.DispatchWorkflow) ? DispatchUnavailable : null;
}
