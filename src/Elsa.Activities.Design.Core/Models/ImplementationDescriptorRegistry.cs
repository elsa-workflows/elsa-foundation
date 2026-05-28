using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Default in-memory implementation of <see cref="IImplementationDescriptorRegistry"/>.
/// Single instance per host (registered as singleton). Duplicate <c>Kind</c> registrations
/// throw — same key must not produce two different descriptor types.
/// </summary>
public sealed class ImplementationDescriptorRegistry : IImplementationDescriptorRegistry
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public void Register(ImplementationDescriptorRegistration registration)
    {
        lock (_gate)
        {
            if (_registrations.TryGetValue(registration.Kind, out var existing) && existing != registration.DescriptorType)
                throw new InvalidOperationException(
                    $"Implementation-descriptor kind '{registration.Kind}' is already registered with type '{existing.FullName}'; refusing to overwrite with '{registration.DescriptorType.FullName}'.");

            _registrations[registration.Kind] = registration.DescriptorType;
        }
    }

    public void RegisterAll(IEnumerable<ImplementationDescriptorRegistration> registrations)
    {
        foreach (var registration in registrations)
            Register(registration);
    }

    public Type? Resolve(string kind)
    {
        lock (_gate)
        {
            return _registrations.TryGetValue(kind, out var type) ? type : null;
        }
    }
}
