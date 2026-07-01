using Elsa.Events.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Events.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        // Register under the closed generic so the publisher can resolve only the handlers for a given
        // event type, and under the non-generic marker so scan-based registration checks keep working.
        services.AddScoped<IEventHandler<TEvent>, THandler>();
        services.AddScoped<IEventHandler, THandler>();
        return services;
    }

    public static IServiceCollection AddEventHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IEventHandler<>), serviceType: null);
        services.AddHandlersFromAssembly(assembly, typeof(IEventHandler<>), typeof(IEventHandler));
        return services;
    }
}
