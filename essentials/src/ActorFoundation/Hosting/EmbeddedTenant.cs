using Kernel.Foundation.Contracts;

namespace Elsa.ActorFoundation.Hosting;

/// <summary>Binds the configured tenant of an Embedded foundation host.</summary>
public static class EmbeddedTenant
{
    private static BoundScope? _scope;

    /// <summary>Binds the process to one configured tenant for ingress and reads.</summary>
    /// <remarks>Call during host composition with the tenant from trusted configuration.</remarks>
    public static BoundScope Bind(string configuredTenant)
    {
        var configured = new BoundScope(configuredTenant);
        var existing = Interlocked.CompareExchange(ref _scope, configured, null);
        if (existing is null) return configured;
        if (existing.Tenant == configured.Tenant) return existing;
        throw new InvalidOperationException("The Embedded host is already bound to its configured tenant.");
    }
}
