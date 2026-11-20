namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>The fault codes of an evaluation that staged a delayed child schedule the runtime refuses (R-7, spec 261).</summary>
public static class RuntimeDelayedScheduleFaultCodes
{
    /// <summary>The due time is more than <c>RuntimeDelayedScheduleOptions.MaxDelay</c> ahead of the runtime clock.</summary>
    public const string TooFar = "runtime.delayed-schedule.too-far";

    /// <summary>The host has no outbox sweep (<c>IRuntimeDelayedScheduleCapability</c>), so nothing would deliver the child.</summary>
    public const string Unavailable = "runtime.delayed-schedule.unavailable";
}
