namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Validates durable incident-state transitions that must preserve prior resolution evidence.</summary>
public static class IncidentStateTransitionValidator
{
    /// <summary>
    /// Ensures a committed resolution outcome and its lifecycle effect cannot be changed. An exact replay remains valid.
    /// </summary>
    public static void EnsureResolutionOutcomeIsWriteOnce(IncidentState? existing, IncidentState candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existingOutcome = existing?.ResolutionOutcome;
        if (existingOutcome is null)
            return;

        if (!OutcomesAreIdentical(existingOutcome, candidate.ResolutionOutcome) ||
            existing!.Status != candidate.Status ||
            existing.ResolvedAt != candidate.ResolvedAt)
        {
            throw new InvalidOperationException(
                $"Incident '{candidate.IncidentId}' has a committed resolution outcome and lifecycle effect that cannot be changed.");
        }
    }

    private static bool OutcomesAreIdentical(
        IncidentResolutionOutcome existing,
        IncidentResolutionOutcome? candidate)
    {
        if (candidate is null ||
            !StringComparer.Ordinal.Equals(existing.ActionKind, candidate.ActionKind) ||
            existing.AppliedAt != candidate.AppliedAt ||
            !StringComparer.Ordinal.Equals(existing.SystemSource, candidate.SystemSource) ||
            !ReferencesAreIdentical(existing.Strategy, candidate.Strategy) ||
            existing.Metadata.Count != candidate.Metadata.Count)
        {
            return false;
        }

        return existing.Metadata.All(pair =>
            candidate.Metadata.TryGetValue(pair.Key, out var value) &&
            StringComparer.Ordinal.Equals(pair.Value, value));
    }

    private static bool ReferencesAreIdentical(
        Elsa.Workflows.Primitives.Models.IncidentStrategyReference? existing,
        Elsa.Workflows.Primitives.Models.IncidentStrategyReference? candidate) =>
        existing is null
            ? candidate is null
            : existing.Equals(candidate);
}
