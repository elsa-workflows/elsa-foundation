namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Marker that this host delivers delayed child schedules (R-7, spec 261): something sweeps the post-commit outbox, so an
/// item that becomes available later is delivered once it is due. It is registered by the feature that registers the
/// outbox sweep. Without it, an evaluation that stages a delayed schedule faults with
/// <c>runtime.delayed-schedule.unavailable</c> instead of leaving the child waiting for ever.
/// </summary>
public interface IRuntimeDelayedScheduleCapability;
