using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services.Strategies;

/// <summary>Scoped implementation resolver; discovery remains descriptor-only through <see cref="IIncidentStrategyCatalog"/>.</summary>
public sealed class IncidentStrategyResolver : IIncidentStrategyResolver
{
    private readonly IIncidentStrategyRegistrationLookup _registrations;
    private readonly IServiceProvider _serviceProvider;

    internal IncidentStrategyResolver(
        IIncidentStrategyRegistrationLookup registrations,
        IServiceProvider serviceProvider)
    {
        _registrations = registrations;
        _serviceProvider = serviceProvider;
    }

    public IIncidentStrategy Resolve(IncidentStrategyReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_registrations.TryGetRegistration(reference, out var registration))
            throw new InvalidOperationException($"Incident strategy '{reference}' is not registered.");

        return _serviceProvider.GetRequiredService(registration.StrategyType) as IIncidentStrategy
               ?? throw new InvalidOperationException(
                   $"Registered incident strategy type '{registration.StrategyType.FullName ?? registration.StrategyType.Name}' does not implement {nameof(IIncidentStrategy)}.");
    }
}
