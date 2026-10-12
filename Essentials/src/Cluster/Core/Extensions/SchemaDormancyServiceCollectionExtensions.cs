using Elsa.Cluster.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Core.Extensions;

/// <summary>
/// How the source the shared dormancy check answers from is composed (spec 182, FR-003). It is a replacement contract:
/// composing the same source again is a no-op, and composing a different one beside it fails here, never resolved by
/// last-write-wins. <c>Elsa.Cluster.InProcess</c>'s <c>TryAddSchemaDormancyCheck</c> composes the default check.
/// </summary>
public static class SchemaDormancyServiceCollectionExtensions
{
    /// <summary>Composes <typeparamref name="TSource"/> as this container's observation of schema finalization.</summary>
    public static IServiceCollection AddObservedSchemaFinalization<TSource>(this IServiceCollection services)
        where TSource : class, IObservedSchemaFinalization
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(IObservedSchemaFinalization) && !descriptor.IsKeyedService).ToArray();
        if (existing.Any(descriptor => descriptor.ImplementationType == typeof(TSource)))
            return services;
        if (existing.Length > 0)
            throw new InvalidOperationException(
                $"{nameof(IObservedSchemaFinalization)} is a replacement contract: '{typeof(TSource).FullName}' cannot be composed beside " +
                $"'{Describe(existing[0])}'. A container has exactly one, and a second one is never resolved by last-write-wins. Remove one of them.");
        return services.AddSingleton<IObservedSchemaFinalization, TSource>();
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "a factory registration";
}
