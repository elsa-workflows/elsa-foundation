namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Current incident health is independent of execution lifecycle and historical incident totals.</summary>
public sealed record IncidentHealthCounts(int Total, int Active, int Blocking)
{
    public static IncidentHealthCounts From(IEnumerable<IncidentState> incidents)
    {
        var states = incidents.ToArray();
        return new(states.Length, states.Count(state => state.IsActive), states.Count(state => state.IsBlocking));
    }
}
