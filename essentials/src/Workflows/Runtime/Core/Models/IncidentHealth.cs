namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Incident-health membership for a bounded workflow-execution history query.</summary>
public enum IncidentHealth
{
    /// <summary>At least one incident is not resolved or suppressed.</summary>
    Active = 0,

    /// <summary>At least one incident is currently blocking.</summary>
    Blocking = 1,

    /// <summary>No incident is active; resolved and suppressed incidents do not make a run active.</summary>
    None = 2
}
