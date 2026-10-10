using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>Built-in strategy that preserves workflow execution and opens the incident.</summary>
[IncidentStrategy("ContinueWithIncidents", "1", DisplayName = "Continue with incidents")]
public sealed class ContinueWithIncidentsIncidentStrategy : IIncidentStrategy
{
    public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IncidentResolutionActions.ContinueWithIncidents());
    }
}
