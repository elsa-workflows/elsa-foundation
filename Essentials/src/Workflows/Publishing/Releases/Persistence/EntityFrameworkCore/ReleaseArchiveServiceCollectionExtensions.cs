using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

public static class ReleaseArchiveServiceCollectionExtensions
{
    /// <summary>
    /// Selects one archive backend over a consumer's registered context. Installs no provider, schema or migration runner.
    /// The context must have a public constructor accepting its own DbContextOptions; writes use fresh owned instances.
    /// </summary>
    public static IServiceCollection AddReleaseArchiveStore<TContext>(this IServiceCollection services,
        Func<IServiceProvider, SourceScope> scopeFactory, ReleaseBundleLimits? limits = null, bool requireDurable = true) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        services.RemoveAll<IReleaseArchiveReader>();
        services.RemoveAll<IReleaseArchiveWriter>();
        services.RemoveAll<IReleaseArchiveStoreCapabilities>();
        services.RemoveAll<EfReleaseArchiveStore<TContext>>();
        services.AddScoped(provider => new EfReleaseArchiveStore<TContext>(
            provider.GetRequiredService<TContext>(), provider.GetRequiredService<IReleaseBundleCodec>(),
            scopeFactory(provider), provider.GetRequiredService<IPersistenceAccessContextAccessor>(), limits ?? new ReleaseBundleLimits(), requireDurable));
        services.AddScoped<IReleaseArchiveReader>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>());
        services.AddScoped<IReleaseArchiveWriter>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>());
        services.AddScoped<IReleaseArchiveStoreCapabilities>(provider => provider.GetRequiredService<EfReleaseArchiveStore<TContext>>());
        return services;
    }
}
