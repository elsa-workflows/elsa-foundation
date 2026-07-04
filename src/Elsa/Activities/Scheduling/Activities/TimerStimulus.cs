using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// Derives the stimulus identity and recurring-schedule spec of a <see cref="Timer"/> interval start trigger
/// (W16). It maps an authored interval literal to the engine's opaque <c>(StimulusType, StimulusHash)</c>
/// routing pair — the same pair the recurring-trigger pump dispatches and the trigger index binds — plus the
/// recurrence descriptor the schedule store fires from. The publish-time trigger provider and schedule provider
/// both derive their values here so a published <see cref="Timer"/> and the pump's dispatched stimulus resolve
/// to the same routing key.
/// </summary>
public static class TimerStimulus
{
    /// <summary>The stimulus type shared by every interval timer trigger.</summary>
    public const string StimulusType = "Timer";

    /// <summary>
    /// Normalizes an authored interval literal to its canonical hash input. Only surrounding whitespace is
    /// trimmed; the literal is otherwise preserved verbatim, so two authors who wrote the same interval get the
    /// same routing key.
    /// </summary>
    public static string Normalize(string interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interval);
        return interval.Trim();
    }

    /// <summary>
    /// Computes the deterministic stimulus hash for an interval literal — a SHA-256 digest with the engine's
    /// <c>sha256:</c> prefix — stable across processes and machines.
    /// </summary>
    public static string Hash(string interval)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(interval)));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>Builds the trigger stimulus descriptor for an interval literal.</summary>
    public static TriggerStimulusDescriptor DescribeTrigger(string interval) =>
        new(StimulusType, Hash(interval), correlationScope: null);

    /// <summary>Builds the recurring-schedule descriptor for an interval literal.</summary>
    public static RecurringScheduleDescriptor DescribeSchedule(string interval) =>
        new(StimulusType, Hash(interval), RecurringScheduleKind.Interval, Normalize(interval));
}
