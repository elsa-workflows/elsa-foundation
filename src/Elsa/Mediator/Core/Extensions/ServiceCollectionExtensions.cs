using Elsa.Mediator.Core.Contracts;
using Elsa.Pipelines.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Mediator.Core.Extensions;

public static class ServiceCollectionExtensions
{
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
        where TRequest : IRequest<TResponse>
    {
        return services.AddScoped<IRequestHandler, THandler>();
    }
}
