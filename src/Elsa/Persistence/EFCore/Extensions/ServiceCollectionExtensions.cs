using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Persistence.EFCore.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntitySavingHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IEntitySavingHandler<,>));
        return services;
    }

    public static IServiceCollection AddEntitySavingHandler<TDbContext, TEntity, THandler>(this IServiceCollection services)
        where TEntity : Entity
        where TDbContext : DbContext
        where THandler : class, IEntitySavingHandler<TDbContext, TEntity>

    {
        return services.AddScoped<IEntitySavingHandler<TDbContext, TEntity>, THandler>();
    }


    public static IServiceCollection AddEntityLoadingHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly, typeof(IEntityLoadingHandler<,>));
        return services;
    }

    public static IServiceCollection AddEntityLoadingHandler<TDbContext, TEntity, THandler>(this IServiceCollection services)
        where TDbContext : DbContext
        where TEntity : Entity
        where THandler : class, IEntityLoadingHandler<TDbContext, TEntity>

    {
        return services.AddScoped<IEntityLoadingHandler<TDbContext, TEntity>, THandler>();
    }

    private static void AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly, Type handlerGenericTypeDefinition)
    {
        var types = assembly.DefinedTypes;

        foreach (var type in types)
        {
            var handlerServiceTypes = type
                .GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == handlerGenericTypeDefinition);

            if (!handlerServiceTypes.Any())
                continue;

            foreach (var handlerServiceType in handlerServiceTypes)
            {
                services.AddScoped(handlerServiceType, type);
            }
        }
    }
}
