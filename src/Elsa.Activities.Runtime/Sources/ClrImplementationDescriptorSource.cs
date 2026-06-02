using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Resolvers;

namespace Elsa.Activities.Runtime.Sources;

/// <summary>
/// Contributes the (Kind, DescriptorType) mapping for CLR-backed activities to the
/// persistence-side descriptor registry. The kind value matches
/// <see cref="ClrActivityImplementationResolver.KindValue"/> and
/// <see cref="ClrImplementationDescriptor.Kind"/> — three places agree on the same string,
/// owned by this module.
/// </summary>
public sealed class ClrImplementationDescriptorSource : IImplementationDescriptorSource
{
    public IEnumerable<ImplementationDescriptorRegistration> GetRegistrations()
    {
        yield return new ImplementationDescriptorRegistration(
            ClrImplementationDescriptor.KindValue,
            typeof(ClrImplementationDescriptor));
    }
}
