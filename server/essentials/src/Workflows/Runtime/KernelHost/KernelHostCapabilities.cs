namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// What a kernel host's foundation runs. A workflow that needs a capability the host lacks is refused where it is mapped
/// (<see cref="NativeExecutableMapper.Map(Elsa.Workflows.Runtime.Core.Models.WorkflowExecutable, KernelHostCapabilities)"/>),
/// so publishing reports it as <c>definition_unsupported</c>, naming the node and what to change.
/// </summary>
/// <param name="Schedules">Schedule actors, which run Timer and Cron triggers.</param>
/// <param name="Timers">Timers, self-sends with a due time, which a Delay waits on.</param>
/// <param name="Children">Child workflows, which a Dispatch Workflow starts, waited or not.</param>
/// <param name="HttpWaits">
/// An HTTP edge that serves wait routes: requests to an HttpEndpoint a running workflow waits at. Without them only an
/// HttpEndpoint that starts a run is served, and any other is refused.
/// </param>
/// <param name="SelfSends">
/// Self-sends, with which a turn that reaches the activity cap continues in the next. The host sets
/// <c>NativeExecutionOptions.SelfSends</c> from it: without them the Native model runs a burst on to its next wait, within its
/// bounds, past which the workflow faults (<c>native.burst.limit</c>).
/// </param>
public sealed record KernelHostCapabilities(bool Schedules = true, bool Timers = true, bool Children = true, bool SelfSends = true,
    bool HttpWaits = true)
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

    /// <summary>
    /// Why an HTTP Endpoint that waits is refused on a host whose edge serves no wait routes: one anywhere but where the
    /// workflow begins, or one there whose start-a-workflow option is off.
    /// </summary>
    public const string HttpWaitUnavailable =
        "An HTTP Endpoint that waits for a request doesn't run on this host yet: only one that starts the workflow does. " +
        "Turn on its start-a-workflow option and put it where the workflow begins, or wait for an event instead.";

    /// <summary>The first node of <paramref name="workflow"/>, by id, that needs what this host lacks, with why; or null.</summary>
    internal (string Node, string Reason)? Refusal(Kernel.Native.Workflow workflow)
    {
        var starts = HttpWaits ? null : Kernel.Native.NativeStartTriggers.StartEndpoints(workflow).ToHashSet();
        foreach (var node in workflow.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var activity = node.Type;
            var reason = !Schedules && (activity == typeof(Kernel.Native.Timer) || activity == typeof(Kernel.Native.Cron)) ? SchedulesUnavailable :
                !Timers && activity == typeof(Kernel.Native.Delay) ? DelayUnavailable :
                !Children && activity == typeof(Kernel.Native.DispatchWorkflow) ? DispatchUnavailable :
                starts is not null && activity == typeof(Kernel.Native.HttpEndpoint) && !starts.Contains(node) ? HttpWaitUnavailable : null;
            if (reason is not null) return (node.Id, reason);
        }
        return null;
    }
}
