using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core runtime operational-state family (R14-R18).</summary>
public static class RuntimeOperationalStateEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeOperationalStateEntityFrameworkCore(this IServiceCollection services, RuntimeOperationalStateEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = RuntimeOperationalStateStoreBackend.Find(services);
            if (existing?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(x => x.ImplementationInstance).OfType<RuntimeOperationalStateEntityFrameworkCoreOptions>().SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime operational-state EF persistence is already registered with different provider options.");
                return services;
            }

            RuntimeEfCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "operational state");

            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                RuntimeOperationalStateStoreBackend.EnsureNoUnownedRegistrations(services);

            BookmarkStateEfContextRegistration.EnsureCompatible(
                services,
                provider,
                options.ConnectionString,
                options.ConnectionName,
                RuntimeOperationalStateEfModule.DefaultSqliteConnectionString);
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime operational state");
            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);

            var artifacts = RuntimeArtifactStoreBackend.Find(services);
            var activity = RuntimeActivityExecutionStoreBackend.Find(services);
            var workflow = WorkflowExecutionStateStoreBackend.Find(services);
            var bookmarks = BookmarkStateStoreBackend.Find(services);
            var alterations = RuntimeWorkflowAlterationStoreBackend.Find(services);
            var scopes = WorkflowTestScopeStoreBackend.Find(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime operational state",
                artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework ? artifacts.Owns : null,
                activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework ? activity.Owns : null,
                workflow?.Name == WorkflowExecutionStateStoreBackend.EntityFramework ? workflow.Owns : null,
                bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework ? bookmarks.Owns : null,
                alterations?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework ? alterations.Owns : null,
                scopes?.Name == WorkflowTestScopeStoreBackend.EntityFramework ? scopes.Owns : null,
                existing?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? existing.Owns : null);

            var configured = new RuntimeOperationalStateEntityFrameworkCoreOptions
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
            services.TryAddEnumerable(ServiceDescriptor.Scoped<Elsa.Tasks.Core.IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(AddContext(services, configured, provider));

            var durableContract = ServiceDescriptor.Scoped<IDurableValueStateStore>(provider => provider.GetRequiredService<EfDurableValueStateStore>());
            var schedulerContract = ServiceDescriptor.Scoped<ISchedulerStateStore>(provider => provider.GetRequiredService<EfSchedulerStateStore>());
            var livenessContract = ServiceDescriptor.Scoped<IExecutionLivenessStateStore>(provider => provider.GetRequiredService<EfExecutionLivenessStateStore>());
            var holdContract = ServiceDescriptor.Scoped<IWorkflowHoldStateStore>(provider => provider.GetRequiredService<EfWorkflowHoldStateStore>());
            var incidentContract = ServiceDescriptor.Scoped<IIncidentStateStore>(provider => provider.GetRequiredService<EfIncidentStateStore>());
            var attentionContract = ServiceDescriptor.Scoped<IWorkflowRuntimeAttentionQuery>(provider =>
            {
                // Attention is complete only when both halves of the composition are EF-owned. A mixed
                // Groundwork/in-memory workflow backend must fail closed rather than silently reporting an
                // empty EF incident projection as all-clear.
                return provider.GetService<IWorkflowExecutionStateStore>() is EfWorkflowExecutionStateStore
                    ? provider.GetRequiredService<EfWorkflowRuntimeAttentionQuery>()
                    : new UnavailableWorkflowRuntimeAttentionQuery();
            });
            var recoveryContract = ServiceDescriptor.Scoped<IRuntimeRecoveryScanner>(provider => provider.GetRequiredService<InMemoryRuntimeRecoveryScanner>());
            services.RemoveAll<IDurableValueStateStore>();
            services.RemoveAll<ISchedulerStateStore>();
            services.RemoveAll<IExecutionLivenessStateStore>();
            services.RemoveAll<IWorkflowHoldStateStore>();
            services.RemoveAll<IIncidentStateStore>();
            services.RemoveAll<IWorkflowRuntimeAttentionQuery>();
            services.RemoveAll<IRuntimeRecoveryScanner>();
            services.AddScoped<EfDurableValueStateStore>();
            var durableConcrete = services.Last();
            services.AddScoped<EfSchedulerStateStore>();
            var schedulerConcrete = services.Last();
            services.AddScoped<EfExecutionLivenessStateStore>();
            var livenessConcrete = services.Last();
            services.AddScoped<EfWorkflowHoldStateStore>();
            var holdConcrete = services.Last();
            services.AddScoped<EfIncidentStateStore>();
            var incidentConcrete = services.Last();
            services.AddScoped<EfWorkflowRuntimeAttentionQuery>();
            var attentionConcrete = services.Last();
            services.AddScoped<InMemoryRuntimeRecoveryScanner>();
            var recoveryConcrete = services.Last();
            services.Add(durableContract);
            services.Add(schedulerContract);
            services.Add(livenessContract);
            services.Add(holdContract);
            services.Add(incidentContract);
            services.Add(attentionContract);
            services.Add(recoveryContract);
            owned.AddRange([durableConcrete, schedulerConcrete, livenessConcrete, holdConcrete, incidentConcrete, attentionConcrete, recoveryConcrete,
                durableContract, schedulerContract, livenessContract, holdContract, incidentContract, attentionContract, recoveryContract]);

            RuntimeOperationalStateStoreBackend.Register(services, new(
                RuntimeOperationalStateStoreBackend.EntityFramework,
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

    public static IServiceCollection AddRuntimeSchedulerEntityFrameworkCore(this IServiceCollection services, RuntimeOperationalStateEntityFrameworkCoreOptions options) => services.AddRuntimeOperationalStateEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeDurableValueStateEntityFrameworkCore(this IServiceCollection services, RuntimeDurableValueStateEntityFrameworkCoreOptions options) => services.AddRuntimeOperationalStateEntityFrameworkCore(options.ToOperationalOptions());
    public static IServiceCollection AddRuntimeSchedulerStateEntityFrameworkCore(this IServiceCollection services, RuntimeSchedulerStateEntityFrameworkCoreOptions options) => services.AddRuntimeOperationalStateEntityFrameworkCore(options.ToOperationalOptions());
    public static IServiceCollection AddRuntimeExecutionLivenessStateEntityFrameworkCore(this IServiceCollection services, RuntimeExecutionLivenessStateEntityFrameworkCoreOptions options) => services.AddRuntimeOperationalStateEntityFrameworkCore(options.ToOperationalOptions());
    public static IServiceCollection AddRuntimeWorkflowHoldStateEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowHoldStateEntityFrameworkCoreOptions options) => services.AddRuntimeOperationalStateEntityFrameworkCore(options.ToOperationalOptions());

    private static bool OptionsEqual(RuntimeOperationalStateEntityFrameworkCoreOptions left, RuntimeOperationalStateEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName) &&
        StringComparer.Ordinal.Equals(left.RecoveryContinuationSigningKey, right.RecoveryContinuationSigningKey);

    private static IReadOnlyCollection<ServiceDescriptor> AddContext(IServiceCollection services, RuntimeOperationalStateEntityFrameworkCoreOptions options, string provider) => provider switch
    {
        "sqlite" => AddContext<BookmarkStateSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite),
        "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer),
        "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql),
        "mysql" => AddContext<BookmarkStateMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql),
        _ => throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider))
    };

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(IServiceCollection services, RuntimeOperationalStateEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind) where TContext : BookmarkStateDbContext
    {
        var start = services.Count;
        services.AddDbContext<TContext>((provider, builder) => bind(builder, ResolveConnectionString(provider, options), RuntimeEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.TryAddScoped<BookmarkStateDbContext>(provider => provider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    private static string ResolveConnectionString(IServiceProvider provider, RuntimeOperationalStateEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString!;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
            return configuration?.GetConnectionString(options.ConnectionName!) ?? throw new InvalidOperationException($"Runtime operational-state EF connection '{options.ConnectionName}' was not found.");
        var fallback = configuration?.GetConnectionString(RuntimeOperationalStateEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback!;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite") return RuntimeOperationalStateEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("Runtime operational-state EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
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
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeOperationalStateEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}

public sealed class RuntimeDurableValueStateEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
    internal RuntimeOperationalStateEntityFrameworkCoreOptions ToOperationalOptions() => new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey };
}

public sealed class RuntimeSchedulerStateEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
    internal RuntimeOperationalStateEntityFrameworkCoreOptions ToOperationalOptions() => new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey };
}

public sealed class RuntimeExecutionLivenessStateEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
    internal RuntimeOperationalStateEntityFrameworkCoreOptions ToOperationalOptions() => new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey };
}

public sealed class RuntimeWorkflowHoldStateEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
    internal RuntimeOperationalStateEntityFrameworkCoreOptions ToOperationalOptions() => new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, RecoveryContinuationSigningKey = RecoveryContinuationSigningKey };
}
