using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Combines the per-outcome disclosure decisions of one admission of retained tool outcomes.</summary>
internal static class AgentRetainedOutcomeAdmission
{
    /// <summary>
    /// The admission's refusal, or null when every outcome is admitted. A definite refusal (below 500: provenance, denial or
    /// host policy) wins over an outcome that could not be decided; otherwise the first 5xx outcome passes through unchanged,
    /// so callers keep a paused continuation for a retry, exactly as for any other 5xx admission failure.
    /// </summary>
    internal static AgentError? Combine(IEnumerable<AgentError?> outcomes)
    {
        AgentError? undecided = null;
        foreach (var outcome in outcomes)
        {
            if (outcome is null) continue;
            if (outcome.StatusCode < 500) return outcome;
            undecided ??= outcome;
        }
        return undecided;
    }
}
