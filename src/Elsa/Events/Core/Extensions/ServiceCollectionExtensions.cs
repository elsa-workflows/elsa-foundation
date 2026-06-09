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
        return services.AddScoped<IEventHandler, THandler>();
    }

    public static IServiceCollection AddEventHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IEventHandler<>), typeof(IEventHandler));
        return services;
    }
}
