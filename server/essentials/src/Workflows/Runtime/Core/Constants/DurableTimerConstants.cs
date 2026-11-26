namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>
/// Shared identifiers for the durable-timer subsystem, referenced by the <c>Delay</c> activity, the
/// durable timer store, and the hosted timer pump so the three agree on the stimulus that links a timer
/// to its bookmark.
/// </summary>
public static class DurableTimerConstants
{
    /// <summary>
    /// The stimulus type stamped on every durable-timer bookmark and dispatched by the pump. A per-timer
    /// unique stimulus hash disambiguates individual timers within this shared type.
    /// </summary>
    public const string TimerStimulusType = "DurableTimer";

    /// <summary>The <c>requestedBy</c> attribution the pump uses when dispatching a timer resume.</summary>
    public const string PumpRequestedBy = "runtime.timer";
}
