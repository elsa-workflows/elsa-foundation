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
    }
}
