namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Contributor interface for the activity-implementation resolver registry. A module implements
/// this and registers it via DI; the single <c>RegisterActivityImplementationResolvers</c> handler
/// resolves every <see cref="IActivityImplementationResolverSource"/> and adds their resolvers to
/// the <c>OnActivityImplementationResolversInitializing</c> event. The interface — not the event —
/// describes the intent.
/// </summary>
public interface IActivityImplementationResolverSource
{
    /// <summary>Return the resolver(s) this source contributes.</summary>
    IEnumerable<IActivityImplementationResolver> GetResolvers();
}
