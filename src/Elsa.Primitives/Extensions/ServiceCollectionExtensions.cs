using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;

namespace Elsa.Primitives.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAsInterfaces<TImplementation>(this IServiceCollection services, ServiceLifetime serviceLifetime = ServiceLifetime.Scoped)
            where TImplementation : class
        {
            var interfaces = typeof(TImplementation).GetInterfaces();
            foreach(var serviceType in interfaces)
            {
                services.Add(
                    new ServiceDescriptor(serviceType, typeof(TImplementation), serviceLifetime)
                );
            }

            return services;
        }

        public static  IServiceCollection AddSingletonList<TInstance, TService>(this IServiceCollection services, IEnumerable<TInstance> instances, Func<IServiceProvider, TInstance, TService> factory)
            where TInstance : class
            where TService : class
        {
            foreach (var instance in instances)
            {
                services.AddSingleton(sp =>
                {
                    return factory.Invoke(sp, instance);
                });
            }

            return services;
        }

        public static IServiceCollection AddSingletonList<T>(this IServiceCollection services, IEnumerable<T> instances)
            where T : class
        {
            foreach (var instance in instances)
                services.AddSingleton(instance);

            return services;
        }
    }
}
