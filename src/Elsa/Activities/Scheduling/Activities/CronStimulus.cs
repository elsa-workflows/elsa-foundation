using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// Derives the stimulus identity and recurring-schedule spec of a <see cref="Cron"/> start trigger (W16). It
/// maps an authored cron literal to the engine's opaque <c>(StimulusType, StimulusHash)</c> routing pair — the
/// same pair the recurring-trigger pump dispatches and the trigger index binds — plus the recurrence descriptor
/// the schedule store fires from. The publish-time trigger provider and schedule provider both derive their
/// values here so a published <see cref="Cron"/> and the pump's dispatched stimulus resolve to the same routing
/// key.
/// </summary>
public static class CronStimulus
{
    /// <summary>The stimulus type shared by every cron trigger.</summary>
    public const string StimulusType = "Cron";

    /// <summary>
    /// Normalizes an authored cron literal to its canonical hash input: trimmed, with internal whitespace runs
    /// collapsed to a single space, so <c>"0 * * * *"</c> and <c>"0   *  * * *"</c> resolve to the same routing
    /// key. The field values themselves are preserved verbatim.
    /// </summary>
    public static string Normalize(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return string.Join(' ', expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Computes the deterministic stimulus hash for a cron literal — a SHA-256 digest with the engine's
    /// <c>sha256:</c> prefix — stable across processes and machines.
    /// </summary>
    public static string Hash(string expression)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(expression)));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>Builds the trigger stimulus descriptor for a cron literal.</summary>
    public static TriggerStimulusDescriptor DescribeTrigger(string expression) =>
        new(StimulusType, Hash(expression), correlationScope: null);

    /// <summary>Builds the recurring-schedule descriptor for a cron literal.</summary>
    public static RecurringScheduleDescriptor DescribeSchedule(string expression) =>
        new(StimulusType, Hash(expression), RecurringScheduleKind.Cron, Normalize(expression));
}
