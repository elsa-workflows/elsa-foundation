namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Explicit incident-owner decisions; arbitrary incident upserts are not lifecycle events.</summary>
public static class RuntimeHistoryIncidentFacts
{
    public static RuntimeHistorySourceFact Created(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentCreated, incident.CreatedAt, cause);

    public static RuntimeHistorySourceFact Resolved(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentResolved,
            incident.Status == IncidentStatus.Resolved && incident.ResolvedAt is { } at ? at :
                throw new ArgumentException("An actual resolved incident is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact StrategyEvaluated(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentStrategyEvaluated,
            incident.ResolutionOutcome?.AppliedAt ?? throw new ArgumentException("An applied strategy decision is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact Suppressed(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentSuppressed,
            incident.Status == IncidentStatus.Suppressed && incident.ResolvedAt is { } at ? at :
                throw new ArgumentException("An actual suppressed incident is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact DecisionApplied(IncidentState incident, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentDecisionApplied,
            incident.ResolutionOutcome?.AppliedAt ?? throw new ArgumentException("An applied incident decision is required.", nameof(incident)), cause);

    public static RuntimeHistorySourceFact ProjectionReconciled(IncidentState incident, DateTimeOffset checkpointTime, RuntimeHistoryCausation? cause = null) =>
        New(incident, RuntimeHistorySourceFactKind.IncidentProjectionReconciled, checkpointTime, cause);

    private static RuntimeHistorySourceFact New(IncidentState incident, RuntimeHistorySourceFactKind kind, DateTimeOffset at, RuntimeHistoryCausation? cause)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var reason = kind is RuntimeHistorySourceFactKind.IncidentStrategyEvaluated or RuntimeHistorySourceFactKind.IncidentDecisionApplied
            ? incident.ResolutionOutcome!.ActionKind : null;
        // Custom native action kinds have no length limit. Omit an over-budget diagnostic label rather than reject a valid decision.
        return new(kind, incident.IncidentId, at, incident.ActivityExecutionId, causation: cause,
            incidentId: incident.IncidentId, reasonCode: reason?.Length <= 128 ? reason : null);
    }
}
