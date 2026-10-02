using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>The service collection the runtime was composed in, read again at shell activation.</summary>
internal sealed record RuntimeSecretResolverComposition(IServiceCollection Services);

/// <summary>
/// Fails shell activation when the host composes more than one <see cref="IRuntimeSecretResolver"/>, naming every
/// registration, whatever order they were registered in. A host that composes none starts: a secret-bound activity
/// then parks with the missing-resolver activation failure.
/// </summary>
/// <remarks>
/// Framework constitution §2.6.2 requires a replacement-contract conflict to be prevented at registration time or
/// detected at startup. A registration sees only what was registered before it, so this check reads the composed
/// service collection once every feature has registered. It counts registrations rather than resolving them: this is
/// detection, not a contribution-style consumer of the contract, which §2.6.2 forbids.
/// </remarks>
internal sealed class RuntimeSecretResolverCompositionValidator(RuntimeSecretResolverComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = composition.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IRuntimeSecretResolver) && !descriptor.IsKeyedService)
            .ToArray();
        if (registrations.Length > 1)
            throw new MultipleRuntimeSecretResolversException(registrations.Select(Describe).ToArray());

        return Task.CompletedTask;
    }

    /// <summary>Registers the check once per service collection, in the Prepare phase, before any startup work.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RuntimeSecretResolverComposition)))
            return;

        services.AddSingleton(new RuntimeSecretResolverComposition(services));
        services.AddShellInitializer<RuntimeSecretResolverCompositionValidator>(LifecyclePhase.Prepare, 0);
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
