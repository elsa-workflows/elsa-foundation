using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Workflows.Extensions;

public static class SecretsWorkflowsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SecretValueRuntimeResolver"/> as the container's <see cref="IRuntimeSecretResolver"/>, so a
    /// workflow activity's secret-bound inputs are resolved through the Secrets module when the activity is activated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IRuntimeSecretResolver"/> is a replacement contract (framework constitution §2.6.2): a container has at
    /// most one, and the runtime registers none. When another <see cref="IRuntimeSecretResolver"/> is already registered
    /// this throws, naming both, as an early diagnostic rather than adding a second registration. One registered after
    /// this one is not seen here; the activities runtime's startup check fails shell activation for it, naming both
    /// (<see cref="Elsa.Workflows.Runtime.Core.Exceptions.MultipleRuntimeSecretResolversException"/>). A host that
    /// supplies its own resolver composes it instead of this, not beside it.
    /// </para>
    /// <para>
    /// Calling this again on a container where it already registered the resolver adds nothing, so a host that composes
    /// the bridge and a shell feature that composes it again keep one registration.
    /// </para>
    /// <para>
    /// Scoped, because <see cref="Elsa.Secrets.Core.Contracts.ISecretValueResolver"/> is scoped: the durable secret
    /// repositories behind it are bound to one access context.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another <see cref="IRuntimeSecretResolver"/> is already registered.</exception>
    public static IServiceCollection AddSecretsWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRuntimeSecretResolver) && !descriptor.IsKeyedService)
            .ToArray();
        var competitors = registered.Where(descriptor => descriptor.ImplementationType != typeof(SecretValueRuntimeResolver)).ToArray();
        if (competitors.Length > 0)
            throw new InvalidOperationException(
                $"{nameof(IRuntimeSecretResolver)} is already registered as {string.Join(", ", competitors.Select(Describe))}, so " +
                $"'{typeof(SecretValueRuntimeResolver).FullName}' cannot be registered beside it. {nameof(IRuntimeSecretResolver)} is a " +
                "replacement contract with at most one implementation per container, and a second one is never resolved by " +
                "last-write-wins. Compose either the Secrets workflow bridge or the other resolver, not both.");

        if (registered.Length == 0)
            services.AddScoped<IRuntimeSecretResolver, SecretValueRuntimeResolver>();

        return services;
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
