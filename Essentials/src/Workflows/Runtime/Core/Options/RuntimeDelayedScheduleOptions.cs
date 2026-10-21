namespace Elsa.Workflows.Runtime.Core.Options;

/// <summary>
/// Limits on the delayed child schedules a structural activity may stage (R-7, spec 261). A delayed schedule is a child
/// schedule committed now that becomes deliverable at a due time; see <c>IRuntimeActivityExecutionContext.ScheduleChildActivity</c>.
/// </summary>
public sealed class RuntimeDelayedScheduleOptions
{
    /// <summary>The default for <see cref="MaxDelay"/>: 24 hours.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromHours(24);

    /// <summary>
    /// How far ahead of the runtime clock a due time may be. An evaluation that stages a later one faults with
    /// <c>runtime.delayed-schedule.too-far</c>. A compensation scope caps its handler retry waits at 24 hours, not at this
    /// value, so a host that composes compensation must not set it below the longest wait its scopes can reach.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = DefaultMaxDelay;
}
