using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Evaluates immutable incident facts into one executable incident-resolution action.</summary>
public interface IIncidentStrategy
{
    ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken);
}
