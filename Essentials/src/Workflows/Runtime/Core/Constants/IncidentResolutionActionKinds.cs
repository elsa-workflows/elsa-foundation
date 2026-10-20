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
    /// The incident's faulted activity was retried by an operator. The only action kind that may replace a committed
    /// <see cref="WaitForIntervention"/> outcome, and only by resolving the incident.
    /// </summary>
    public const string OperatorRetry = "OperatorRetry";
}
