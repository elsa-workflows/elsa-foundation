using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Read-only, descriptor-only discovery of incident strategies available at startup.</summary>
public interface IIncidentStrategyCatalog
{
    IReadOnlyCollection<IncidentStrategyDescriptor> List();
    IncidentStrategyReference DefaultStrategy { get; }
    bool TryGet(IncidentStrategyReference reference, out IncidentStrategyDescriptor descriptor);
}
