using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Pipelines.Core.Extensions;

/// <summary>
/// Message-agnostic DI helper for scanning an assembly and registering every type that
/// implements a given handler interface. Shared by the command, request, and event handler
/// registration helpers.
/// </summary>
public static class HandlerRegistrationExtensions
{
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly, Type handlerGenericTypeDefinition, Type? serviceType)
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

        return services;
    }
}
