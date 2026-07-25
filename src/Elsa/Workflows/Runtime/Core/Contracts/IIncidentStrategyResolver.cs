using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Resolves the scoped implementation for one exact registered incident-strategy identity.</summary>
public interface IIncidentStrategyResolver
{
    IIncidentStrategy Resolve(IncidentStrategyReference reference);
}
