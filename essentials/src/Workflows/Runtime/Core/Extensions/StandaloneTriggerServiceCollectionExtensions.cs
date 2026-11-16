using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

public static class StandaloneTriggerServiceCollectionExtensions
{
    /// <summary>Installs one producer-bound signal port. The source module separately supplies its trusted scoped context.</summary>
    public static IServiceCollection AddStandaloneTriggerIngress<TSource>(this IServiceCollection services,
        StandaloneTriggerSourceDescriptor descriptor) where TSource : class
        => new StandaloneTriggerSourceRegistrar(services).Register<TSource>(descriptor);
}

internal sealed class StandaloneTriggerSourceRegistrar(IServiceCollection services)
{
    internal IServiceCollection Register<TSource>(StandaloneTriggerSourceDescriptor descriptor) where TSource : class
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TypeKey); ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.SourceFamily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(descriptor.SchemaVersion);
        ArgumentNullException.ThrowIfNull(descriptor.PayloadSchema);
        var registrations = services.Where(x => x.ServiceType == typeof(StandaloneTriggerSourceDescriptor) && x.IsKeyedService).ToArray();
        var existing = registrations.Where(x => Equals(x.ServiceKey, typeof(TSource)))
            .Select(x => x.KeyedImplementationInstance).OfType<StandaloneTriggerSourceDescriptor>().ToArray();
        if (registrations.Count(x => Equals(x.ServiceKey, typeof(TSource))) != existing.Length || existing.Length > 1 || existing.Length == 1 && !JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(existing[0]), JsonSerializer.SerializeToElement(descriptor)))
            throw new InvalidOperationException("A source producer already owns a different standalone trigger registration.");
        if (registrations.Any(x => !Equals(x.ServiceKey, typeof(TSource)) &&
            x.KeyedImplementationInstance is StandaloneTriggerSourceDescriptor other && other.SourceFamily == descriptor.SourceFamily))
            throw new InvalidOperationException("A standalone source family already belongs to a different registered producer.");
        if (existing.Length == 0)
        {
            services.AddKeyedSingleton<StandaloneTriggerSourceDescriptor>(typeof(TSource), descriptor);
            services.AddSingleton(descriptor);
            services.AddScoped(provider =>
            {
                var contexts = provider.GetServices<IStandaloneTriggerSourceContext<TSource>>().ToArray();
                return new StandaloneTriggerSourceCapability(descriptor, contexts.Length == 1 && contexts[0].SupportsResourceAuthorization);
            });
        }
        services.TryAddScoped<IStandaloneTriggerIngress<TSource>>(provider => provider.GetRequiredService<IStandaloneTriggerIngressFactory>()
            .Create<TSource>());
        return services;
    }
}
