namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Explicit incident-owner decisions, rather than labels inferred from arbitrary incident upserts.</summary>
public static class RuntimeHistoryIncidentFacts
{
    public static RuntimeHistorySourceFact Created(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentCreated, incident.CreatedAt, cause);

    public static RuntimeHistorySourceFact Resolved(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentResolved, incident.ResolvedAt ?? throw new ArgumentException("An actual resolution time is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact StrategyEvaluated(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentStrategyEvaluated,
            incident.ResolutionOutcome?.AppliedAt ?? throw new ArgumentException("An applied strategy decision is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact Suppressed(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentSuppressed, incident.ResolvedAt ?? throw new ArgumentException("An actual suppression time is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact DecisionApplied(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentDecisionApplied,
            incident.ResolutionOutcome?.AppliedAt ?? throw new ArgumentException("An applied incident decision is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact ProjectionReconciled(IncidentState incident, DateTimeOffset checkpointTime, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentProjectionReconciled, checkpointTime, cause);

    private static RuntimeHistorySourceFact New(IncidentState incident, RuntimeHistorySourceFactKind kind, DateTimeOffset at, RuntimeHistoryCausation? cause)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return new(kind, incident.IncidentId, at, incident.ActivityExecutionId, causation: cause,
            incidentId: incident.IncidentId, reasonCode: kind is RuntimeHistorySourceFactKind.IncidentStrategyEvaluated or RuntimeHistorySourceFactKind.IncidentDecisionApplied ? incident.ResolutionOutcome!.ActionKind : null);
    }
}
