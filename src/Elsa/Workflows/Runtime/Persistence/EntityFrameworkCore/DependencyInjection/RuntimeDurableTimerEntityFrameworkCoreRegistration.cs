using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core durable timer store (R24).</summary>
public static class RuntimeDurableTimerEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeDurableTimerEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeDurableTimerEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = DurableTimerStoreBackend.Find(services);
            if (existing?.Name == DurableTimerStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(x => x.ImplementationInstance)
                    .OfType<RuntimeDurableTimerEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime durable-timer EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime durable timers",
                    existing.Owns,
                    RuntimeOperationalStateStoreBackend.Find(services) is { } existingOperational && existingOperational.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                        ? existingOperational.Owns
                        : null);
                return services;
            }

            RuntimeEfCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "durable timers");

            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                DurableTimerStoreBackend.EnsureNoUnownedRegistrations(services);

            BookmarkStateEfContextRegistration.EnsureCompatible(
                services,
                provider,
                options.ConnectionString,
                options.ConnectionName,
                RuntimeOperationalStateEfModule.DefaultSqliteConnectionString);
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(
                services,
                options.RecoveryContinuationSigningKey,
                "Runtime durable timers");

            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in DurableTimerStoreBackend.CaptureTimerSurfaceRegistrations(services).ToArray())
                services.Remove(descriptor);

            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                operational.EnsureOwnsRegisteredContracts(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime durable timers",
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);

            var configured = new RuntimeDurableTimerEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(configuredOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey))
                    configuredOptions.SigningKey = options.RecoveryContinuationSigningKey;
                configuredOptions.AllowEphemeralDevelopmentKey = false;
            });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());

            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(AddContext(services, configured, provider));
            foreach (var descriptor in BookmarkStateEfContextRegistration.ContextRegistrations(services, provider))
                if (!owned.Contains(descriptor))
                    owned.Add(descriptor);

            services.AddScoped<EfDurableTimerStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IDurableTimerStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfDurableTimerStore>());
            services.Add(contract);
            owned.Add(concrete);
            owned.Add(contract);
            DurableTimerStoreBackend.Register(
                services,
                new DurableTimerStoreBackend(
                    DurableTimerStoreBackend.EntityFramework,
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

    public static IServiceCollection AddRuntimeDurableTimersEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeDurableTimerEntityFrameworkCoreOptions options) =>
        services.AddRuntimeDurableTimerEntityFrameworkCore(options);

    public static IServiceCollection AddDurableTimerEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeDurableTimerEntityFrameworkCoreOptions options) =>
        services.AddRuntimeDurableTimerEntityFrameworkCore(options);

    private static bool OptionsEqual(
        RuntimeDurableTimerEntityFrameworkCoreOptions left,
        RuntimeDurableTimerEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName) &&
        StringComparer.Ordinal.Equals(left.RecoveryContinuationSigningKey, right.RecoveryContinuationSigningKey);

    private static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        RuntimeDurableTimerEntityFrameworkCoreOptions options,
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
        RuntimeDurableTimerEntityFrameworkCoreOptions options,
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
        RuntimeDurableTimerEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString!;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName!) ??
                   throw new InvalidOperationException($"Runtime durable-timer EF connection '{options.ConnectionName}' was not found.");
        var fallback = configuration?.GetConnectionString(RuntimeOperationalStateEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback!;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return RuntimeOperationalStateEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Runtime durable-timer EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private static void RemoveOwned(
        IServiceCollection services,
        IReadOnlyCollection<ServiceDescriptor> owned)
    {
        foreach (var descriptor in owned.Where(descriptor =>
                     services.Contains(descriptor) &&
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeDurableTimerEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}
