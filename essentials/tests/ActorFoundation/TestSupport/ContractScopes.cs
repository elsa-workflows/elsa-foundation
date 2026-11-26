using Kernel.Foundation.Contracts;

namespace Elsa.ActorFoundation.TestSupport;

/// <summary>Creates distinct trusted bindings for in-process contract fixtures.</summary>
public static class ContractScopes
{
    public static BoundScope Bind(string tenant) => new(tenant);
}
