using System.Security.Cryptography;
using System.Text;
using Kernel.Foundation.Contracts;

namespace Elsa.ActorFoundation.Hosting;

/// <summary>Binds the configured tenant of an Embedded foundation host.</summary>
public static class EmbeddedTenant
{
    private const string IsolatedPrefix = "elsa-private-";
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

    /// <summary>Derives a stable, separate actor tenant for host-owned work that ordinary ingress must never address.</summary>
    internal static BoundScope Isolate(BoundScope configured, string purpose)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (!ReferenceEquals(configured, Volatile.Read(ref _scope)))
            throw new InvalidOperationException("Only the configured Embedded tenant can have an isolated actor scope.");
        if (configured.Tenant.StartsWith(IsolatedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("The configured tenant uses the private actor namespace.", nameof(configured));
        var identity = Encoding.UTF8.GetBytes($"{configured.Tenant}/{purpose}");
        var digest = SHA256.HashData(identity);
        return new BoundScope(IsolatedPrefix + Convert.ToHexStringLower(digest.AsSpan(0, 25)));
    }
}
