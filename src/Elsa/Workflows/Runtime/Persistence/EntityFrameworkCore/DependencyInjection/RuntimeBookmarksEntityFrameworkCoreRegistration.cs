using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeBookmarksEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeBookmarksEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeBookmarksEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        var configured = new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName
        };

        var existingBackend = BookmarkStateStoreBackend.Find(services);
        existingBackend?.EnsureOwnsRegisteredContract(services);
        if (existingBackend is not null && existingBackend.Name == BookmarkStateStoreBackend.EntityFramework)
        {
            var existingOptions = services
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
                .SingleOrDefault();
            if (existingOptions is null ||
                !string.Equals(EfRelationalProviderBinding.Normalize(existingOptions.Provider), provider, StringComparison.Ordinal) ||
                !string.Equals(existingOptions.ConnectionString, options.ConnectionString, StringComparison.Ordinal) ||
                !string.Equals(existingOptions.ConnectionName, options.ConnectionName, StringComparison.Ordinal))
                throw new InvalidOperationException("Runtime bookmarks EF persistence is already registered with different provider options.");
            return services;
        }
        if (existingBackend is null && BookmarkStateStoreBackend.HasRegisteredContract(services))
        {
            var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore)).ToArray();
            if (descriptors.Any(descriptor => descriptor.ImplementationType != typeof(Elsa.Workflows.Runtime.Core.Services.InMemoryBookmarkStateStore)))
                throw new InvalidOperationException("An explicit IBookmarkStateStore is already registered; EF bookmark persistence refuses to replace it implicitly.");
            BookmarkStateStoreBackend.RemoveDefaultStimulusIndex(services);
            foreach (var descriptor in descriptors)
                services.Remove(descriptor);
        }
        else
        {
            existingBackend?.RemoveOwnedArtifacts(services);
        }

        services.AddSingleton(configured);
        switch (provider)
        {
            case "sqlite": AddContext<BookmarkStateSqliteDbContext>(services, configured, EfRelationalProviderBinding.UseSqlite); break;
            case "sqlserver": AddContext<BookmarkStateSqlServerDbContext>(services, configured, EfRelationalProviderBinding.UseSqlServer); break;
            case "postgresql": AddContext<BookmarkStatePostgreSqlDbContext>(services, configured, EfRelationalProviderBinding.UseNpgsql); break;
            case "mysql": AddContext<BookmarkStateMySqlDbContext>(services, configured, EfRelationalProviderBinding.UseMySql); break;
            default: throw new ArgumentException($"Unknown Runtime bookmarks EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }

        var descriptorState = ServiceDescriptor.Scoped<IBookmarkStateStore>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
        var descriptorIndex = ServiceDescriptor.Scoped<IBookmarkStimulusIndex>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
        services.Add(descriptorState);
        services.AddScoped<EfBookmarkStateStore>();
        services.Add(descriptorIndex);
        BookmarkStateStoreBackend.Register(services, new BookmarkStateStoreBackend(
            BookmarkStateStoreBackend.EntityFramework,
            descriptorState,
            descriptorIndex,
            RemoveEfArtifacts));
        return services;
    }

    public static IServiceCollection AddRuntimeBookmarkEntityFrameworkCore(this IServiceCollection services, RuntimeBookmarksEntityFrameworkCoreOptions options) =>
        services.AddRuntimeBookmarksEntityFrameworkCore(options);

    private static void AddContext<TContext>(IServiceCollection services, RuntimeBookmarksEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : BookmarkStateDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => bind(builder, ResolveConnectionString(provider, options), BookmarkStateEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.AddScoped<BookmarkStateDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private static string ResolveConnectionString(IServiceProvider provider, RuntimeBookmarksEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName) ?? throw new InvalidOperationException($"Runtime bookmarks EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
        var fallback = configuration?.GetConnectionString(BookmarkStateEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite") return BookmarkStateEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Runtime bookmarks EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private static void RemoveEfArtifacts(IServiceCollection services)
    {
        services.RemoveAll<BookmarkStateDbContext>();
        services.RemoveAll<BookmarkStateSqliteDbContext>();
        services.RemoveAll<BookmarkStateSqlServerDbContext>();
        services.RemoveAll<BookmarkStatePostgreSqlDbContext>();
        services.RemoveAll<BookmarkStateMySqlDbContext>();
        services.RemoveAll<EfBookmarkStateStore>();
        services.RemoveAll<RuntimeBookmarksEntityFrameworkCoreOptions>();
    }
}

public sealed class RuntimeBookmarksEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
