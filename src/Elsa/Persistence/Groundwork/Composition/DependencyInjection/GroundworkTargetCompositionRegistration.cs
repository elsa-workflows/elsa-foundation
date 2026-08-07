using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Composition-time access to the host's <see cref="GroundworkTargetComposition"/>. The neutral target
/// registry feature deposits configured targets here; provider packages deposit their leaves. Either order
/// works, which matters because shell feature ordering is not something a feature may assume.
/// </summary>
public static class GroundworkTargetCompositionRegistration
{
    /// <summary>Gets the host's target composition, creating and registering it on first use.</summary>
    public static GroundworkTargetComposition GroundworkTargetComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkTargetComposition))
            .Select(descriptor => descriptor.ImplementationInstance as GroundworkTargetComposition)
            .FirstOrDefault(composition => composition is not null);
        if (existing is not null)
            return existing;

        var created = new GroundworkTargetComposition();
        services.AddSingleton(created);
        return created;
    }

    /// <summary>Composes a provider leaf, opening every already-configured target it can serve.</summary>
    public static IServiceCollection AddGroundworkProviderLeaf(
        this IServiceCollection services,
        IGroundworkProviderLeaf leaf)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GroundworkTargetComposition().AddProviderLeaf(services, leaf);
        return services;
    }

    /// <summary>Configures one target, opening it now when its provider leaf is already composed.</summary>
    public static IServiceCollection AddGroundworkTargetConfiguration(
        this IServiceCollection services,
        string? targetName,
        GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GroundworkTargetComposition().RequestTarget(services, targetName, entry);
        return services;
    }
}
