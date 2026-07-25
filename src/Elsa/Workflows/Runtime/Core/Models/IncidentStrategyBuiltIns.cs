using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Stable identities and discovery descriptors for the built-in incident strategies.</summary>
public static class IncidentStrategyBuiltIns
{
    public static readonly IncidentStrategyReference FaultReference = new("Fault", "1");
    public static readonly IncidentStrategyReference ContinueWithIncidentsReference = new("ContinueWithIncidents", "1");

    public static readonly IncidentStrategyDescriptor Fault =
        new(FaultReference, "Fault", "Faults the containing workflow while keeping the incident blocking.");

    public static readonly IncidentStrategyDescriptor ContinueWithIncidents =
        new(ContinueWithIncidentsReference, "Continue with incidents", "Keeps the workflow running and opens the incident for visibility.");
}
