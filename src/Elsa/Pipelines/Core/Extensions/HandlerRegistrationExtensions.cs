using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Elsa.Pipelines.Core.Extensions;

/// <summary>
/// Message-agnostic DI helper for scanning an assembly and registering every type that implements a
/// given handler interface. Shared by the command and request handler registration helpers.
/// </summary>
/// <remarks>
/// Each implementation is registered under <b>every closed generic</b> it implements (the dispatch
/// path — pipelines resolve the closed <c>IHandler&lt;TMessage,TResult&gt;</c> so only the one relevant
/// handler is constructed per dispatch) and, when a non-generic marker is supplied, under that marker
/// too (kept for scan/registration-count checks). Registration is idempotent per (service, impl) pair.
/// </remarks>
public static class HandlerRegistrationExtensions
{
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly, Type handlerGenericTypeDefinition, Type? serviceType)
    {
        foreach (var type in assembly.DefinedTypes)
        {
            var closedGenerics = type
                .GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == handlerGenericTypeDefinition)
                .ToArray();

            if (closedGenerics.Length == 0)
                continue;

            foreach (var closedGeneric in closedGenerics)
                services.TryAddEnumerable(ServiceDescriptor.Scoped(closedGeneric, type));

            if (serviceType is not null)
                services.TryAddEnumerable(ServiceDescriptor.Scoped(serviceType, type));
        }

        return services;
    }
}
