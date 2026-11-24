using Elsa.ActorFoundation.Hosting;
using Kernel.Foundation.Contracts;

namespace Kernel.Runtime.Hosting;

internal static class FixedTenantScope
{
    private static BoundScope? _scope;

    internal static BoundScope Bind(string tenant)
    {
        var configured = new EmbeddedTenant(tenant).Scope;
        var existing = Interlocked.CompareExchange(ref _scope, configured, null);
        if (existing is null) return configured;
        if (existing.Tenant == configured.Tenant) return existing;
        throw new InvalidOperationException("The standalone host is already bound to its configured tenant.");
    }
}
