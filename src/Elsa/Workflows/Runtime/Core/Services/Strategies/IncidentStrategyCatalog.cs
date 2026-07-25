using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Strategies;

/// <summary>Startup-built, descriptor-only incident-strategy registry.</summary>
public sealed class IncidentStrategyCatalog : IIncidentStrategyCatalog, IIncidentStrategyRegistrationLookup
{
    private readonly IReadOnlyCollection<IncidentStrategyDescriptor> _descriptors;
    private readonly IReadOnlyDictionary<IncidentStrategyReference, IncidentStrategyRegistration> _registrations;

    public IncidentStrategyCatalog(
        IEnumerable<IncidentStrategyRegistration> registrations,
        IncidentStrategyCatalogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        options ??= new IncidentStrategyCatalogOptions();
        ArgumentNullException.ThrowIfNull(options.DefaultStrategy);

        var byReference = new Dictionary<IncidentStrategyReference, IncidentStrategyRegistration>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            IncidentStrategyIdentity.ValidateRegisteredDescriptor(registration.Descriptor);
            if (!byReference.TryAdd(registration.Descriptor.Reference, registration))
            {
                var prior = byReference[registration.Descriptor.Reference];
                throw new InvalidOperationException(
                    $"Incident strategy '{registration.Descriptor.Reference}' has duplicate registrations: {StrategyTypeIdentity(prior.StrategyType)}, {StrategyTypeIdentity(registration.StrategyType)}.");
            }
        }

        if (!byReference.TryGetValue(options.DefaultStrategy, out var defaultRegistration))
            throw new InvalidOperationException($"Configured default incident strategy '{options.DefaultStrategy}' is not registered.");

        _registrations = byReference;
        _descriptors = Array.AsReadOnly(byReference.Values
            .Select(registration => registration.Descriptor)
            .OrderBy(descriptor => descriptor.Reference.Alias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Reference.Version, StringComparer.Ordinal)
            .ToArray());
        DefaultStrategy = defaultRegistration.Descriptor.Reference;
    }

    public IncidentStrategyReference DefaultStrategy { get; }

    public IReadOnlyCollection<IncidentStrategyDescriptor> List() => _descriptors;

    public bool TryGet(IncidentStrategyReference reference, out IncidentStrategyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_registrations.TryGetValue(reference, out var registration))
        {
            descriptor = registration.Descriptor;
            return true;
        }

        descriptor = null!;
        return false;
    }

    bool IIncidentStrategyRegistrationLookup.TryGetRegistration(
        IncidentStrategyReference reference,
        out IncidentStrategyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _registrations.TryGetValue(reference, out registration!);
    }

    private static string StrategyTypeIdentity(Type type) => type.FullName ?? type.Name;
}
