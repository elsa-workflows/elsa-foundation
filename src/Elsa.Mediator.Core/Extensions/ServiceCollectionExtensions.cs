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
        return services.AddScoped<IDomainEventHandler, THandler>();
    }

    public static IServiceCollection AddDomainEventHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IDomainEventHandler<>), typeof(IDomainEventHandler));
        return services;
    }

    public static IServiceCollection AddRequestHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IRequestHandler<,>), typeof(IRequestHandler));
        return services;
    }

    public static IServiceCollection AddCommandHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(ICommandHandler<>), typeof(ICommandHandler));
        services.AddHandlersFromAssembly(assembly, typeof(ICommandHandler<,>), typeof(ICommandHandler));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="IRequestHandler{TRequest,TResponse}"/> with the service container.
    /// </summary>
    public static IServiceCollection AddRequestHandler<THandler, TRequest, TResponse>(this IServiceCollection services)
        where THandler : class, IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>?
    {
        return services.AddScoped<IRequestHandler, THandler>();
    }

    private static void AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly, Type handlerGenericTypeDefinition, Type? serviceType)
    {
        var types = assembly.DefinedTypes;

        foreach (var type in types)
        {
            var handlerServiceTypes = type
                .GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == handlerGenericTypeDefinition);

            if (!handlerServiceTypes.Any())
                continue;

            if (serviceType is not null)
            {
                services.AddScoped(serviceType, type);
                continue;
            }

            foreach (var handlerServiceType in handlerServiceTypes)
            {
                services.AddScoped(handlerServiceType, type);
            }
        }
    }
}
