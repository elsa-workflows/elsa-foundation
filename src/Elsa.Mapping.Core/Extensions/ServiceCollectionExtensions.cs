using Elsa.Mapping.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Mapping.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMappingsFrom(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.DefinedTypes)
        {
            if (IsMappingType(type, out var serviceTypes))
            {
                foreach (var serviceType in serviceTypes)
                    services.AddScoped(serviceType, type);
            }
        }

        return services;
    }

    private static bool IsMappingType(Type type, out IEnumerable<Type> serviceTypes)
    {
        serviceTypes = type
            .GetInterfaces()
            .Where(x => x.IsGenericType && (x.GetGenericTypeDefinition() == typeof(IObjectMapping<,>)));

        return serviceTypes.Any() == true;
    }
}
