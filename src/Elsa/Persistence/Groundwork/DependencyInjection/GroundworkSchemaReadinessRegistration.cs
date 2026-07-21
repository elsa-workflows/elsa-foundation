using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the <see cref="GroundworkSchemaReadinessTask"/> startup guard. Provider leaf
/// registrations call this after their admission initializer so the guard validates the admitted
/// publication in the <see cref="LifecyclePhase.Start"/> phase, strictly after every
/// <see cref="LifecyclePhase.Prepare"/> provider initializer has run. Registration is idempotent.
/// </summary>
public static class GroundworkSchemaReadinessRegistration
{
    public static IServiceCollection AddGroundworkSchemaReadinessGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkSchemaReadinessTask)))
            return services;

        services.TryAddSingleton<GroundworkSchemaReadinessTask>();
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<GroundworkSchemaReadinessTask>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(GroundworkSchemaReadinessTask),
            LifecyclePhase.Start,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(GroundworkSchemaReadinessRegistration)}.{nameof(AddGroundworkSchemaReadinessGuard)}"));
        return services;
    }
}
