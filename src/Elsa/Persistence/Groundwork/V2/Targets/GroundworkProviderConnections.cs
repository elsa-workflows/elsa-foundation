using Groundwork.Kernel;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Resolves the provider connection for a Groundwork target the same way the storage session source
/// does: a keyed connection registered for the target, the ordinary default connection only for the
/// default target, and a clear failure otherwise.
/// </summary>
public static class GroundworkProviderConnections
{
    public static IStorageProviderConnection Resolve(IServiceProvider services, string? targetName, string consumer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        var name = GroundworkTargetNames.Normalize(targetName);
        if (services.GetKeyedService<IStorageProviderConnection>(name) is { } keyed)
            return keyed;
        if (GroundworkTargetNames.IsDefault(name) && services.GetService<IStorageProviderConnection>() is { } @default)
            return @default;
        throw new InvalidOperationException(
            $"{consumer} target '{name}' has no Groundwork provider connection. " +
            "Register a Groundwork provider feature with that target, or leave the target unset to share the default connection.");
    }
}
