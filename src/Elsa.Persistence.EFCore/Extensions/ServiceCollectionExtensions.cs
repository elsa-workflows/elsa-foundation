using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Persistence.EFCore.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureQueries<TDbContext>(this IServiceCollection services)
            where TDbContext: DbContext
        {
            var dbContextType = typeof(TDbContext);
            var dbSets = GetDbSetProperties<TDbContext>();

            foreach (var dbSet in dbSets)
            {
                var entityType = dbSet.PropertyType.GetGenericArguments()[0];
                var serviceDescriptor = new ServiceDescriptor(
                    typeof(IQueries<>).MakeGenericType(entityType)    ,
                    typeof(EFCoreQueries<,>).MakeGenericType(dbContextType, entityType),
                    ServiceLifetime.Scoped
                );
                services.Add(serviceDescriptor);
            }

            return services;
        }

        public static IServiceCollection ConfigureCommands<TDbContext>(this IServiceCollection services, CommandsOptions? options = null)
            where TDbContext : DbContext
        {
            var dbSets = GetDbSetProperties<TDbContext>();
            foreach (var dbSet in dbSets)
            {
                var entityType = dbSet.PropertyType.GetGenericArguments()[0];
                services.ConfigureCommands<TDbContext>(entityType ,options);
            }

            return services;
        }

        private static IEnumerable<PropertyInfo> GetDbSetProperties<TDbContext>()
        {
            var dbContextType = typeof(TDbContext);
            return dbContextType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.PropertyType.IsGenericType && x.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));
        }

        public static IServiceCollection ConfigureCommands<TDbContext>(this IServiceCollection services, Type entityType, CommandsOptions? options = null)
            where TDbContext : DbContext
        {
            var supportedCommandTypes = options?.GetSupportedCommands(entityType) ?? CommandsOptions.AllCommands;

            foreach (var commandType in supportedCommandTypes)
            {
                switch (commandType)
                {
                    case CommandType.Add:
                        ConfigureCommand<TDbContext>(services, typeof(IAddCommand<>), typeof(EFCoreAddCommand<,>), entityType);
                        break;
                    case CommandType.Update:
                        ConfigureCommand<TDbContext>(services, typeof(IUpdateCommand<>), typeof(EFCoreUpdateCommand<,>), entityType);
                        break;
                    case CommandType.Save:
                        ConfigureCommand<TDbContext>(services, typeof(ISaveCommand<>), typeof(EFCoreSaveCommand<,>), entityType);
                        break;
                    case CommandType.Delete:
                        ConfigureCommand<TDbContext>(services, typeof(IDeleteCommand<>), typeof(EFCoreDeleteCommand<,>), entityType);
                        break;
                    case CommandType.BulkInsert:
                        ConfigureCommand<TDbContext>(services, typeof(IBulkInsertCommand<>), typeof(EFCoreBulkInsert<,>), entityType);
                        break;
                    case CommandType.BulkUpsert:
                        services.AddScoped<IUpsertCommandGenerator, UpsertCommandGenerator>();
                        ConfigureCommand<TDbContext>(services, typeof(IBulkUpsertCommand<>), typeof(EFCoreBulkUpsert<,>), entityType);
                        break;
                }
            }

            return services;
        }

        static void ConfigureCommand<TDbContext>(IServiceCollection serviceCollection, Type genericServiceType, Type genericImplementationType, Type entityType)
            where TDbContext : DbContext
        {
            var serviceDescriptor = new ServiceDescriptor(
                genericServiceType.MakeGenericType(entityType),
                genericImplementationType.MakeGenericType(typeof(TDbContext), entityType),
                ServiceLifetime.Scoped
            );
            serviceCollection.Add(serviceDescriptor);
        }
    }
}
