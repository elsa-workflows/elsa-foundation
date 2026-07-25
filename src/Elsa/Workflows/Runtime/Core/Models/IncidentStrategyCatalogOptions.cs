using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Configures the strategy selected when an executable does not pin another valid strategy.</summary>
public sealed class IncidentStrategyCatalogOptions
{
    public IncidentStrategyReference DefaultStrategy { get; init; } = IncidentStrategyBuiltIns.FaultReference;
}
