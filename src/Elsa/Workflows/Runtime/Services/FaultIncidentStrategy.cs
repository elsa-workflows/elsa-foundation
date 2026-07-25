using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Strategies;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Built-in default strategy that faults the containing workflow.</summary>
[IncidentStrategy("Fault", "1", DisplayName = "Fault")]
public sealed class FaultIncidentStrategy : IIncidentStrategy
{
    public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IncidentResolutionActions.FaultWorkflow());
    }
}
