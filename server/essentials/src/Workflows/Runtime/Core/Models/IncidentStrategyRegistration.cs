using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Atomically contributed descriptor and implementation identity for one incident strategy.</summary>
public sealed class IncidentStrategyRegistration
{
    public IncidentStrategyRegistration(IncidentStrategyDescriptor descriptor, Type strategyType)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(strategyType);
        if (!typeof(IIncidentStrategy).IsAssignableFrom(strategyType) || strategyType.IsAbstract || strategyType.IsInterface)
        {
            throw new ArgumentException(
                $"Incident strategy type '{strategyType.FullName ?? strategyType.Name}' must be a concrete {nameof(IIncidentStrategy)} implementation.",
                nameof(strategyType));
        }

        Descriptor = descriptor;
        StrategyType = strategyType;
    }

    public IncidentStrategyDescriptor Descriptor { get; }
    public Type StrategyType { get; }
}
