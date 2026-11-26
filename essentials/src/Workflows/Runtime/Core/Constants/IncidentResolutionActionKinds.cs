namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>Stable classifications persisted for incident-resolution outcomes.</summary>
public static class IncidentResolutionActionKinds
{
    public const string FaultWorkflow = "FaultWorkflow";
    public const string ContinueWithIncidents = "ContinueWithIncidents";
    public const string WaitForIntervention = "WaitForIntervention";
    public const string AbsorbFault = "AbsorbFault";
    public const string SuppressIncident = "SuppressIncident";
    /// <summary>
    /// The incident's faulted activity was retried by an operator. One of the two action kinds that may replace a
    /// committed <see cref="WaitForIntervention"/> outcome, and only by resolving the incident.
    /// </summary>
    public const string OperatorRetry = "OperatorRetry";
    /// <summary>
    /// An operator answered the intervention wait with one of the decisions the requesting structural parent offered
    /// (<c>DecideIntervention/1</c>). The other action kind that may replace a committed <see cref="WaitForIntervention"/>
    /// outcome, and only by resolving the incident.
    /// </summary>
    public const string OperatorDecision = "OperatorDecision";
}
