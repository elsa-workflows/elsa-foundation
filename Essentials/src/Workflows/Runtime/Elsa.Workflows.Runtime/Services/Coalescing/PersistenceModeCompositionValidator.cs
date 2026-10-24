using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// The persistence policy the persistence modes registered, and the service collection they registered it in, read again
/// at shell activation.
/// </summary>
internal sealed record PersistenceModeComposition(IServiceCollection Services, ServiceDescriptor Policy);

/// <summary>
/// Fails shell activation when a persistence policy registered after the persistence modes would take over theirs, or
/// when the host composes a second persistence-mode resolver, naming the registrations in conflict.
/// </summary>
/// <remarks>
/// <see cref="Extensions.PersistenceModeServiceCollectionExtensions.AddPersistenceModes"/> refuses a policy a host
/// registered before it. One registered after it would win by registration order and decide which checkpoints commit,
/// including the boundaries every mode keeps. A second resolver would win the same way and decide a run's mode. This
/// reads the composed collection once every feature has registered and resolves nothing.
/// </remarks>
internal sealed class PersistenceModeCompositionValidator(PersistenceModeComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var policies = Registrations<IRuntimeCheckpointPersistencePolicy>();
        if (policies.Length != 1 || !ReferenceEquals(policies[0], composition.Policy))
        {
            throw new InvalidOperationException(
                $"'{typeof(IRuntimeCheckpointPersistencePolicy)}' is registered as {string.Join(", ", policies.Select(Describe))}, " +
                $"but the persistence modes run on {Describe(composition.Policy)} alone, on which the commit boundaries of every mode depend. " +
                "Remove the other registration.");
        }

        var resolvers = Registrations<IRuntimePersistenceModeResolver>();
        if (resolvers.Length > 1)
        {
            throw new InvalidOperationException(
                $"'{typeof(IRuntimePersistenceModeResolver)}' is registered more than once: {string.Join(", ", resolvers.Select(Describe))}. " +
                "A runtime composition has one; replace the default instead of adding another.");
        }

        return Task.CompletedTask;
    }

    private ServiceDescriptor[] Registrations<TContract>() =>
        composition.Services.Where(descriptor => descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService).ToArray();

    /// <summary>Registers the check once per service collection, in the Prepare phase, before any startup work.</summary>
    internal static void Register(IServiceCollection services, ServiceDescriptor policy)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PersistenceModeComposition)))
            return;

        services.AddSingleton(new PersistenceModeComposition(services, policy));
        services.AddShellInitializer<PersistenceModeCompositionValidator>(LifecyclePhase.Prepare, 0);
    }

    internal static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
