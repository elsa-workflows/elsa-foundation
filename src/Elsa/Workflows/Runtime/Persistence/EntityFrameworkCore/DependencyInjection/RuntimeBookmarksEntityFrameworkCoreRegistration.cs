using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeBookmarksEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeBookmarksEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeBookmarksEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var configured = new RuntimeBookmarksEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            };
            BookmarkStateEfContextRegistration.EnsureCompatible(
                services,
                provider,
                options.ConnectionString,
                options.ConnectionName,
                BookmarkStateEfModule.DefaultSqliteConnectionString);

            var existingBackend = BookmarkStateStoreBackend.Find(services);
            existingBackend?.EnsureOwnsRegisteredContract(services);
            if (existingBackend is not null && existingBackend.Name == BookmarkStateStoreBackend.EntityFramework)
            {
                existingBackend.EnsureOwnsRegisteredAuxiliaryContracts(services);
                var siblingArtifactsBackend = RuntimeArtifactStoreBackend.Find(services);
                if (siblingArtifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework)
                    siblingArtifactsBackend.EnsureOwnsRegisteredContracts(services);
                var siblingActivityBackend = RuntimeActivityExecutionStoreBackend.Find(services);
                if (siblingActivityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                    siblingActivityBackend.EnsureOwnsRegisteredContracts(services);
                var existingOptions = services
                    .Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (existingOptions is null ||
                    !string.Equals(EfRelationalProviderBinding.Normalize(existingOptions.Provider), provider, StringComparison.Ordinal) ||
                    !string.Equals(existingOptions.ConnectionString, options.ConnectionString, StringComparison.Ordinal) ||
                    !string.Equals(existingOptions.ConnectionName, options.ConnectionName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Runtime bookmarks EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime bookmarks",
                    existingBackend.Owns,
                    siblingArtifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework
                        ? siblingArtifactsBackend.Owns
                        : null,
                    siblingActivityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework
                        ? siblingActivityBackend.Owns
                        : null);
                return services;
            }

            var artifactsBackend = RuntimeArtifactStoreBackend.Find(services);
            var artifactsOwnContext = artifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework;
            if (artifactsOwnContext)
                artifactsBackend!.EnsureOwnsRegisteredContracts(services);
            var activityBackend = RuntimeActivityExecutionStoreBackend.Find(services);
            var activityOwnContext = activityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework;
            if (activityOwnContext)
                activityBackend!.EnsureOwnsRegisteredContracts(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime bookmarks",
                artifactsOwnContext ? artifactsBackend!.Owns : null,
                activityOwnContext ? activityBackend!.Owns : null);

            if (existingBackend is null && BookmarkStateStoreBackend.HasRegisteredContract(services))
            {
                BookmarkStateStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(services);
                BookmarkStateStoreBackend.RemoveDefaultStimulusIndex(services);
                BookmarkStateStoreBackend.RemoveDefaultStateStore(services);
            }
            var commitExistingBackendRemoval = existingBackend?.PrepareRemoveOwnedArtifacts(services);

            var ownedArtifacts = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            ownedArtifacts.Add(optionsDescriptor);
            if (artifactsOwnContext)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(artifactsBackend!.Owns));
            if (activityOwnContext)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(activityBackend!.Owns));
            if (!artifactsOwnContext && !activityOwnContext)
                switch (provider)
                {
                    case "sqlite": ownedArtifacts.AddRange(AddContext<BookmarkStateSqliteDbContext>(services, configured, EfRelationalProviderBinding.UseSqlite)); break;
                    case "sqlserver": ownedArtifacts.AddRange(AddContext<BookmarkStateSqlServerDbContext>(services, configured, EfRelationalProviderBinding.UseSqlServer)); break;
                    case "postgresql": ownedArtifacts.AddRange(AddContext<BookmarkStatePostgreSqlDbContext>(services, configured, EfRelationalProviderBinding.UseNpgsql)); break;
                    case "mysql": ownedArtifacts.AddRange(AddContext<BookmarkStateMySqlDbContext>(services, configured, EfRelationalProviderBinding.UseMySql)); break;
                }

            var descriptorState = ServiceDescriptor.Scoped<IBookmarkStateStore>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
            var descriptorIndex = ServiceDescriptor.Scoped<IBookmarkStimulusIndex>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
            var storeDescriptor = ServiceDescriptor.Scoped<EfBookmarkStateStore, EfBookmarkStateStore>();
            services.Add(descriptorState);
            services.Add(storeDescriptor);
            ownedArtifacts.Add(storeDescriptor);
            services.Add(descriptorIndex);
            BookmarkStateStoreBackend.Register(services, new BookmarkStateStoreBackend(
                BookmarkStateStoreBackend.EntityFramework,
                descriptorState,
                descriptorIndex,
                collection => RemoveEfArtifacts(collection, ownedArtifacts),
                ownedArtifacts));
            commitExistingBackendRemoval?.Invoke(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    public static IServiceCollection AddRuntimeBookmarkEntityFrameworkCore(this IServiceCollection services, RuntimeBookmarksEntityFrameworkCoreOptions options) =>
        services.AddRuntimeBookmarksEntityFrameworkCore(options);

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(IServiceCollection services, RuntimeBookmarksEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : BookmarkStateDbContext
    {
        var start = services.Count;
        services.AddDbContext<TContext>((provider, builder) => bind(builder, ResolveConnectionString(provider, options), BookmarkStateEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.AddScoped<BookmarkStateDbContext>(provider => provider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    private static string ResolveConnectionString(IServiceProvider provider, RuntimeBookmarksEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var namedConnection = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(namedConnection))
                throw new InvalidOperationException($"Runtime bookmarks EF connection '{options.ConnectionName}' was not found or was empty in ConnectionStrings.");
            return namedConnection;
        }
        var fallback = configuration?.GetConnectionString(BookmarkStateEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return BookmarkStateEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Runtime bookmarks EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private static void RemoveEfArtifacts(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> ownedArtifacts)
    {
        if (ownedArtifacts.Any(descriptor => services.Count(candidate => ReferenceEquals(candidate, descriptor)) != 1))
            throw new InvalidOperationException("Runtime bookmarks EF persistence no longer exclusively owns its auxiliary registrations.");

        foreach (var descriptor in ownedArtifacts.Where(descriptor =>
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true))
        {
            // Artifact EF may reuse this context and records the same descriptor as a sibling owner.
            // Keep it alive while replacing only the bookmark backend; the artifact backend remains valid.
            services.Remove(descriptor);
        }
    }
}

public sealed class RuntimeBookmarksEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
