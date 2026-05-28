using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Persistence-side registry mapping <c>ImplementationKind</c> string → CLR descriptor type.
/// Populated at startup via <c>OnImplementationDescriptorsInitializing</c> (the canonical
/// §2.6.1 Registry + StartUp Task sub-pattern). Consumed by the loading handler to resolve
/// the JSON deserialization target before deserializing the shadow column.
/// </summary>
public interface IImplementationDescriptorRegistry
{
    void Register(ImplementationDescriptorRegistration registration);

    void RegisterAll(IEnumerable<ImplementationDescriptorRegistration> registrations);

    /// <summary>
    /// Returns the CLR descriptor type for the given kind, or <c>null</c> if no module has
    /// contributed a mapping for that kind. The loading handler treats a null here as a
    /// hard error (the persisted row references a kind whose owning module isn't installed).
    /// </summary>
    Type? Resolve(string kind);
}
