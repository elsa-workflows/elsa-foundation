using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Mediator.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainEventHandler<TDomainEvent, THandler>(this IServiceCollection services)
        where TDomainEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TDomainEvent>
    {
        return services.AddScoped<IDomainEventHandler<TDomainEvent>, THandler>();
    }

    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        AddDomainEventHandlerTypesFromAssembly(services, assembly);
        return services;
    }

    private static void AddDomainEventHandlerTypesFromAssembly(IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes();
        foreach (var type in types)
        {
            var domainEventHandlerServiceTypes = type
                .GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>));

            if (!domainEventHandlerServiceTypes.Any())
                continue;

            foreach(var handlerServiceType in domainEventHandlerServiceTypes)
                services.AddScoped(handlerServiceType, type);
        }
    }
}
