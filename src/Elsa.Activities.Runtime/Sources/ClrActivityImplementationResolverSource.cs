using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Resolvers;

namespace Elsa.Activities.Runtime.Sources;

/// <summary>
/// Contributes the CLR resolver to the runtime-side resolver registry.
/// </summary>
public sealed class ClrActivityImplementationResolverSource(ClrActivityImplementationResolver resolver)
    : IActivityImplementationResolverSource
{
    public IEnumerable<IActivityImplementationResolver> GetResolvers()
    {
        yield return resolver;
    }
}
