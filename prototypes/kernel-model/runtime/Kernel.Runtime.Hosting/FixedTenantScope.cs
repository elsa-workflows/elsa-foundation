using Kernel.Foundation.Contracts;

namespace Kernel.Runtime.Hosting;

internal static class FixedTenantScope
{
    // The standalone host supplies its configured tenant before calling ingress.
    internal static BoundScope Bind(string tenant) => new(tenant);
}
