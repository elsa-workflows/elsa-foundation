using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Composition-time access to the host's <see cref="GroundworkTargetRegistry"/>. Provider leaves declare
/// targets here; lane bindings resolve them by name. The registry instance is shared between composition
/// and runtime, so a lane can never bind to a name no provider declared.
/// </summary>
public static class GroundworkTargetRegistration
{
    /// <summary>Gets the host's target registry, creating and registering it on first use.</summary>
    public static GroundworkTargetRegistry GroundworkTargets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkTargetRegistry))
            .Select(descriptor => descriptor.ImplementationInstance as GroundworkTargetRegistry)
            .FirstOrDefault(registry => registry is not null);
        if (existing is not null)
            return existing;

        var created = new GroundworkTargetRegistry();
        services.AddSingleton(created);
        return created;
    }

    /// <summary>
    /// Declares one admitted physical store under <paramref name="targetName"/>. An exact repeat is
    /// idempotent and reports <see cref="GroundworkTargetDeclarationResult.AlreadyDeclared"/>; a second,
    /// different connection under the same name throws instead of being discarded.
    /// </summary>
    public static GroundworkTargetDeclarationResult DeclareGroundworkTarget(
        this IServiceCollection services,
        string? targetName,
        string providerIdentity,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .GroundworkTargets()
            .Declare(GroundworkTargetDeclaration.Create(targetName, providerIdentity, connectionString));
    }
}
