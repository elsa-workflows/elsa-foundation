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

    /// <summary>
    /// Ensures an Append creates the incident. Append is create-only: an existing incident with the same identity is a
    /// conflict, even when its content is identical, because a replayed checkpoint is resolved by its commit marker first.
    /// </summary>
    public static void EnsureAppendTargetIsAbsent(IncidentState? existing, IncidentState candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (existing is not null)
            throw AppendConflict(candidate);
    }

    /// <summary>The Append conflict, for a store whose atomic create-only insert reports the conflict itself.</summary>
    public static InvalidOperationException AppendConflict(IncidentState candidate) =>
        new($"Incident '{candidate.IncidentId}' already exists for workflow execution '{candidate.WorkflowExecutionId}' and cannot be appended again.");

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
        Elsa.Primitives.Models.IncidentStrategyReference? existing,
        Elsa.Primitives.Models.IncidentStrategyReference? candidate) =>
        existing is null
            ? candidate is null
            : existing.Equals(candidate);
}
