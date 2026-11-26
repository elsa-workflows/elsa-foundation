using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Core.Extensions;

public static class PersistenceCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-neutral current persistence context. A host may replace the accessor
    /// with a scoped tenant-aware implementation; the default always remains scoped and nonblank.
    /// </summary>
    public static IServiceCollection AddPersistenceCore(
        this IServiceCollection services,
        string defaultScope = PersistenceScope.DefaultValue)
    {
        ArgumentNullException.ThrowIfNull(services);
        var scope = new PersistenceScope(defaultScope);
        services.TryAddScoped(_ =>
            new DefaultPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(scope)));
        services.TryAddScoped<IPersistenceAccessContextAccessor>(serviceProvider =>
            serviceProvider.GetRequiredService<DefaultPersistenceAccessContextAccessor>());
        services.TryAddScoped<IPersistenceAccessContextBinder>(serviceProvider =>
            serviceProvider.GetRequiredService<DefaultPersistenceAccessContextAccessor>());
        services.TryAddSingleton<IPersistenceOperationScopeFactory, PersistenceOperationScopeFactory>();
        services.TryAddSingleton<IPersistenceScopeSource>(_ => new SinglePersistenceScopeSource(scope));
        services.TryAddSingleton<IPersistenceScopeRunner, PersistenceScopeRunner>();
        return services;
    }

    private sealed class DefaultPersistenceAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor, IPersistenceAccessContextBinder
    {
        private PersistenceAccessContext _current = current;
        private int _bound;

        public PersistenceAccessContext Current => Volatile.Read(ref _current);

        public void Bind(PersistenceAccessContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (Interlocked.CompareExchange(ref _bound, 1, 0) != 0)
                throw new InvalidOperationException("The persistence access context has already been bound for this scope.");
            Volatile.Write(ref _current, context);
        }
    }
}
