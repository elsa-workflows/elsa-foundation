using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Runtime-side registry mapping descriptor <c>Kind</c> → resolver instance. Populated at
/// startup via <c>OnActivityImplementationResolversInitializing</c>. Consumed by
/// <see cref="IActivityFactory"/> at construction time.
/// </summary>
public interface IActivityImplementationResolverRegistry
{
    void RegisterAll(IEnumerable<IActivityImplementationResolver> resolvers);

    /// <summary>
    /// Resolve the CLR type for the given descriptor by looking up the resolver registered
    /// for the descriptor's <c>Kind</c>. Throws <c>ActivityResolutionException</c> when no
    /// resolver is registered.
    /// </summary>
    Type Resolve(IImplementationDescriptor descriptor);
}
