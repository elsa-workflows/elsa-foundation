using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The write-once incident outcome rule and its two exceptions: a blocking incident waiting for intervention may be
/// resolved once, on the same activity execution, by an operator retry or by an operator decision (spec 260 FR-404).
/// </summary>
public sealed class IncidentStateTransitionValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(IncidentResolutionActionKinds.OperatorRetry, IncidentResolutionSystemSources.OperatorRetryAlteration)]
    [InlineData(IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration)]
    public void An_operator_pair_resolves_a_wait_for_intervention_on_the_same_activity(string actionKind, string systemSource)
    {
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(Waiting(), Resolved(actionKind, systemSource));
    }

    [Theory]
    [InlineData(IncidentResolutionActionKinds.OperatorRetry, IncidentResolutionSystemSources.OperatorRetryAlteration)]
    [InlineData(IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration)]
    public void An_operator_pair_on_a_different_activity_execution_is_refused(string actionKind, string systemSource)
    {
        Assert.Throws<InvalidOperationException>(() =>
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(Waiting(), Resolved(actionKind, systemSource, activityExecutionId: "other-activity")));
    }

    [Theory]
    [InlineData(IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorRetryAlteration)]
    [InlineData(IncidentResolutionActionKinds.OperatorRetry, IncidentResolutionSystemSources.OperatorDecisionAlteration)]
    [InlineData(IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.StructuralIntervention)]
    [InlineData("Contoso.Resolve", IncidentResolutionSystemSources.OperatorDecisionAlteration)]
    public void A_mixed_or_foreign_pair_is_refused(string actionKind, string? systemSource)
    {
        Assert.Throws<InvalidOperationException>(() =>
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(Waiting(), Resolved(actionKind, systemSource)));
    }

    [Fact]
    public void An_operator_decision_cannot_replace_an_open_incident_outcome()
    {
        var open = Incident(IncidentStatus.Open, new IncidentResolutionOutcome(
            IncidentResolutionActionKinds.ContinueWithIncidents, Now, new IncidentStrategyReference("ContinueWithIncidents", "1"), systemSource: null));

        Assert.Throws<InvalidOperationException>(() =>
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(open, Resolved(
                IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration)));
    }

    [Fact]
    public void An_open_incident_waiting_for_intervention_cannot_be_decided()
    {
        Assert.Throws<InvalidOperationException>(() =>
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(Incident(IncidentStatus.Open, Waiting().ResolutionOutcome), Resolved(
                IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration)));
    }

    [Theory]
    [InlineData(IncidentResolutionActionKinds.FaultWorkflow)]
    [InlineData(IncidentResolutionActionKinds.AbsorbFault)]
    [InlineData(IncidentResolutionActionKinds.SuppressIncident)]
    public void An_operator_decision_cannot_replace_any_other_committed_outcome(string actionKind)
    {
        var existing = Incident(IncidentStatus.Blocking, new IncidentResolutionOutcome(actionKind, Now, strategy: null, systemSource: "Other"));

        Assert.Throws<InvalidOperationException>(() =>
            IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, Resolved(
                IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration)));
    }

    [Fact]
    public void A_decided_incident_is_write_once_again()
    {
        var decided = Resolved(IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration);

        // An exact replay stays valid; a retry, a second decision or a return to waiting does not.
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(decided, decided);
        Assert.Throws<InvalidOperationException>(() => IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(decided, Resolved(
            IncidentResolutionActionKinds.OperatorRetry, IncidentResolutionSystemSources.OperatorRetryAlteration)));
        Assert.Throws<InvalidOperationException>(() => IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(decided, Resolved(
            IncidentResolutionActionKinds.OperatorDecision, IncidentResolutionSystemSources.OperatorDecisionAlteration, appliedAt: Now.AddMinutes(5))));
        Assert.Throws<InvalidOperationException>(() => IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(decided, Waiting()));
    }

    private static IncidentState Waiting() => Incident(IncidentStatus.Blocking, new IncidentResolutionOutcome(
        IncidentResolutionActionKinds.WaitForIntervention,
        Now,
        strategy: null,
        systemSource: IncidentResolutionSystemSources.StructuralIntervention));

    private static IncidentState Incident(IncidentStatus status, IncidentResolutionOutcome? outcome) => new(
        "incident-1", "wf-1", "activity-1", "node-1", IncidentSeverity.Error, status, outcome,
        "ActivityFaulted", "Activity failed.", Now, resolvedAt: null);

    private static IncidentState Resolved(
        string actionKind,
        string? systemSource,
        string activityExecutionId = "activity-1",
        DateTimeOffset? appliedAt = null) => new(
        "incident-1", "wf-1", activityExecutionId, "node-1", IncidentSeverity.Error, IncidentStatus.Resolved,
        new IncidentResolutionOutcome(actionKind, appliedAt ?? Now.AddMinutes(1), strategy: null, systemSource: systemSource,
            metadata: new Dictionary<string, string> { ["runtime.alteration.jobId"] = "job-1" }),
        "ActivityFaulted", "Activity failed.", Now, resolvedAt: appliedAt ?? Now.AddMinutes(1));
}
