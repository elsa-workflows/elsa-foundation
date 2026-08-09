using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers a provider leaf's per-target admission behind the single shared
/// <see cref="GroundworkTargetAdmissionInitializer"/>. Provider registrations call this instead of
/// registering their own initializer as a hosted service and shell initializer.
/// </summary>
public static class GroundworkTargetAdmissionRegistration
{
    /// <summary>
    /// Contributes the keyed <typeparamref name="TAdmission"/> instance for <paramref name="targetName"/> to the
    /// admission driver, registering the driver itself on first use.
    /// </summary>
    public static IServiceCollection AddGroundworkTargetAdmission<TAdmission>(
        this IServiceCollection services,
        string targetName)
        where TAdmission : class, IGroundworkTargetAdmission
    {
        ArgumentNullException.ThrowIfNull(services);
        var target = GroundworkTargetNames.Normalize(targetName);
        EnsureAdmissionDriver(services);
        services.AddSingleton<IGroundworkTargetAdmission>(serviceProvider =>
            serviceProvider.GetRequiredKeyedService<TAdmission>(target));
        return services;
    }

    private static void EnsureAdmissionDriver(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkTargetAdmissionInitializer)))
            return;

        services.AddSingleton<GroundworkTargetAdmissionInitializer>();

        // Plain hosts and tests drive admission through the hosted service; shell-composed hosts drive it
        // through the Prepare phase, where shell-scoped hosted services do not run.
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkTargetAdmissionInitializer>());
        services.AddSingleton<IShellInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkTargetAdmissionInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(GroundworkTargetAdmissionInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(GroundworkTargetAdmissionRegistration)}.{nameof(AddGroundworkTargetAdmission)}"));
    }
}
