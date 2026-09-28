using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Cluster.Core.Extensions;

/// <summary>
/// How the shared dormancy check and its source are composed (spec 182, FR-003). Both are replacement contracts: the
/// default check is added unless one is already composed, a replacement or a source is refused when a different one
/// already is, and nothing is resolved by last-write-wins.
/// </summary>
public static class SchemaDormancyServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default <see cref="SchemaDormancyCheck"/> unless a check is already composed. A feature whose operations
    /// need new-version data calls this, so asking the check never depends on who composed it.
    /// </summary>
    public static IServiceCollection TryAddSchemaDormancyCheck(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        services.TryAddSingleton<ISchemaDormancyCheck, SchemaDormancyCheck>();
        return services;
    }

    /// <summary>
    /// Composes <typeparamref name="TSource"/> as this container's observation of schema finalization, the source the
    /// check answers from. Composing it again is a no-op; composing it beside a different source fails here.
    /// </summary>
    public static IServiceCollection AddObservedSchemaFinalization<TSource>(this IServiceCollection services)
        where TSource : class, IObservedSchemaFinalization
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddReplacement<IObservedSchemaFinalization, TSource>(services);
    }

    /// <summary>
    /// Replaces the default check with <typeparamref name="TCheck"/>. The default yields; any other check composed
    /// before fails here.
    /// </summary>
    public static IServiceCollection AddSchemaDormancyCheck<TCheck>(this IServiceCollection services)
        where TCheck : class, ISchemaDormancyCheck
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var yielding in services.Where(descriptor => descriptor.ServiceType == typeof(ISchemaDormancyCheck) && descriptor.ImplementationType == typeof(SchemaDormancyCheck)).ToArray())
            services.Remove(yielding);
        return AddReplacement<ISchemaDormancyCheck, TCheck>(services);
    }

    private static IServiceCollection AddReplacement<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService).ToArray();
        if (existing.Any(descriptor => descriptor.ImplementationType == typeof(TImplementation)))
            return services;
        if (existing.Length > 0)
            throw new InvalidOperationException(
                $"{typeof(TContract).Name} is a replacement contract: '{typeof(TImplementation).FullName}' cannot be composed beside " +
                $"'{Describe(existing[0])}'. A container has exactly one, and a second one is never resolved by last-write-wins. Remove one of them.");
        services.AddSingleton<TContract, TImplementation>();
        return services;
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "a factory registration";
}
