using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Contributor interface for the implementation-descriptor registry. A module that owns an
/// <c>ImplementationKind</c> implements this and registers it via DI; the single
/// <c>RegisterImplementationDescriptors</c> handler resolves every
/// <see cref="IImplementationDescriptorSource"/> and adds their registrations to the
/// <c>OnImplementationDescriptorsInitializing</c> event. The interface — not the event — describes
/// the intent.
/// </summary>
public interface IImplementationDescriptorSource
{
    /// <summary>Return the (Kind, DescriptorType) registration(s) this source contributes.</summary>
    IEnumerable<ImplementationDescriptorRegistration> GetRegistrations();
}
