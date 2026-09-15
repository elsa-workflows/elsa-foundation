using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core scheduler-poison store (R23).</summary>
/// <remarks>
/// This registration owns only <see cref="IWorkflowSchedulerPoisonStore"/>. The aggregate Runtime EF context can
/// be shared with R14-R18 and R20-R24, but scheduler poison is intentionally not added to the operational-state
/// backend contract set.
/// </remarks>
public static class RuntimeSchedulerPoisonEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeSchedulerPoisonEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = WorkflowSchedulerPoisonStoreBackend.Find(services);
            if (existing?.Name == WorkflowSchedulerPoisonStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(x => x.ImplementationInstance)
                    .OfType<RuntimeSchedulerPoisonEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime scheduler-poison EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime scheduler poison",
                    existing.Owns,
                    RuntimeOperationalStateStoreBackend.Find(services) is { } existingOperational && existingOperational.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                        ? existingOperational.Owns
                        : null);
                return services;
            }

            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                WorkflowSchedulerPoisonStoreBackend.EnsureNoUnownedRegistrations(services);

            BookmarkStateEfContextRegistration.EnsureCompatible(
                services,
                provider,
                options.ConnectionString,
                options.ConnectionName,
                RuntimeOperationalStateEfModule.DefaultSqliteConnectionString);

            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in WorkflowSchedulerPoisonStoreBackend.CaptureSurfaceRegistrations(services).ToArray())
                services.Remove(descriptor);

            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                operational.EnsureOwnsRegisteredContracts(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime scheduler poison",
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);

            var configured = new RuntimeSchedulerPoisonEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            };
            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(AddContext(services, configured, provider));
            foreach (var descriptor in BookmarkStateEfContextRegistration.ContextRegistrations(services, provider))
                if (!owned.Contains(descriptor))
                    owned.Add(descriptor);

            services.AddScoped<EfWorkflowSchedulerPoisonStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IWorkflowSchedulerPoisonStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfWorkflowSchedulerPoisonStore>());
            services.Add(contract);
            owned.Add(concrete);
            owned.Add(contract);
            WorkflowSchedulerPoisonStoreBackend.Register(
                services,
                new WorkflowSchedulerPoisonStoreBackend(
                    WorkflowSchedulerPoisonStoreBackend.EntityFramework,
                    owned,
                    collection => RemoveOwned(collection, owned)));
            commitExistingRemoval?.Invoke(services);
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

    public static IServiceCollection AddRuntimeSchedulerPoisonStoreEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options) =>
        services.AddRuntimeSchedulerPoisonEntityFrameworkCore(options);

    public static IServiceCollection AddWorkflowSchedulerPoisonEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options) =>
        services.AddRuntimeSchedulerPoisonEntityFrameworkCore(options);

    private static bool OptionsEqual(
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions left,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName);

    private static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options,
        string provider) => provider switch
    {
        "sqlite" => AddContext<BookmarkStateSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite),
        "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer),
        "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql),
        "mysql" => AddContext<BookmarkStateMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql),
        _ => throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider))
    };

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(
        IServiceCollection services,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : BookmarkStateDbContext
    {
        var start = services.Count;
        services.AddDbContext<TContext>((provider, builder) => bind(
            builder,
            ResolveConnectionString(provider, options),
            RuntimeOperationalStateEfModule.HistoryModuleName,
            typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.TryAddScoped<BookmarkStateDbContext>(provider => provider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    private static string ResolveConnectionString(
        IServiceProvider provider,
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString!;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName!) ??
                   throw new InvalidOperationException($"Runtime scheduler-poison EF connection '{options.ConnectionName}' was not found.");
        var fallback = configuration?.GetConnectionString(RuntimeOperationalStateEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback!;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return RuntimeOperationalStateEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Runtime scheduler-poison EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private static void RemoveOwned(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> owned)
    {
        foreach (var descriptor in owned.Where(descriptor =>
                     services.Contains(descriptor) &&
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     DurableTimerStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeSchedulerPoisonEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
