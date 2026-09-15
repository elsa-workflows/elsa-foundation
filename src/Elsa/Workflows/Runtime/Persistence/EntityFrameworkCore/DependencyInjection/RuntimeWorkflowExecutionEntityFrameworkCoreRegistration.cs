using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeWorkflowExecutionEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowExecutionEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = WorkflowExecutionStateStoreBackend.Find(services);
            if (existing?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                var prior = services.Select(x => x.ImplementationInstance).OfType<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>().SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options)) throw new InvalidOperationException("Runtime workflow execution EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(services, provider, "Runtime workflow executions", existing.Owns,
                    RuntimeArtifactStoreBackend.Find(services) is { } repeatArtifacts ? repeatArtifacts.Owns : null,
                    RuntimeActivityExecutionStoreBackend.Find(services) is { } repeatActivity ? repeatActivity.Owns : null,
                    BookmarkStateStoreBackend.Find(services) is { } repeatBookmarks ? repeatBookmarks.Owns : null,
                    RuntimeWorkflowAlterationStoreBackend.Find(services) is { } repeatAlterations ? repeatAlterations.Owns : null,
                    WorkflowTestScopeStoreBackend.Find(services) is { } repeatScopes ? repeatScopes.Owns : null);
                return services;
            }
            RuntimeCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "workflow execution");
            if (existing is not null) existing.EnsureOwnsRegisteredContract(services);
            else
            {
                var explicitStore = services.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)).ToArray();
                if (explicitStore.Any(x => x.ImplementationType != typeof(Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowExecutionStateStore))) throw new InvalidOperationException("Runtime workflow execution EF persistence refuses to replace an unowned execution-state store.");
            }
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime workflow executions");
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, RuntimeWorkflowExecutionEfModule.DefaultSqliteConnectionString);
            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            var artifacts = RuntimeArtifactStoreBackend.Find(services); var activity = RuntimeActivityExecutionStoreBackend.Find(services); var bookmarks = BookmarkStateStoreBackend.Find(services); var alterations = RuntimeWorkflowAlterationStoreBackend.Find(services); var scopes = WorkflowTestScopeStoreBackend.Find(services); var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework) artifacts.EnsureOwnsRegisteredContracts(services);
            if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework) activity.EnsureOwnsRegisteredContracts(services);
            if (bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework) bookmarks.EnsureOwnsRegisteredAuxiliaryContracts(services);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(services, provider, "Runtime workflow executions", artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework ? artifacts.Owns : null, activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework ? activity.Owns : null, bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework ? bookmarks.Owns : null, alterations?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework ? alterations.Owns : null, scopes?.Name == WorkflowTestScopeStoreBackend.EntityFramework ? scopes.Owns : null, operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);
            var configured = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { Provider = options.Provider, ConnectionString = options.ConnectionString, ConnectionName = options.ConnectionName, RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey };
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(x => { if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey)) x.SigningKey = options.RecoveryContinuationSigningKey; x.AllowEphemeralDevelopmentKey = false; });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            services.AddSingleton(configured);
            var owned = new List<ServiceDescriptor> { services.Last() };
            if (artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(artifacts.Owns));
            else if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(activity.Owns));
            else if (bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(bookmarks.Owns));
            else if (alterations?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(alterations.Owns));
            else if (scopes?.Name == WorkflowTestScopeStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(scopes.Owns));
            else if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework) owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(operational.Owns));
            else owned.AddRange(AddContext(services, configured, provider));
            services.AddScoped<EfWorkflowExecutionStateStore>(); var concrete = services.Last(); owned.Add(concrete);
            services.RemoveAll<IWorkflowExecutionStateStore>();
            var contract = ServiceDescriptor.Scoped<IWorkflowExecutionStateStore>(p => p.GetRequiredService<EfWorkflowExecutionStateStore>()); services.Add(contract);
            WorkflowExecutionStateStoreBackend.Register(services, new WorkflowExecutionStateStoreBackend(WorkflowExecutionStateStoreBackend.EntityFramework, [contract, .. owned], collection => RemoveOwned(collection, owned)));
            commitExistingRemoval?.Invoke(services);
            return services;
        }
        catch { services.Clear(); foreach (var descriptor in snapshot) services.Add(descriptor); throw; }
    }
    public static IServiceCollection AddRuntimeWorkflowExecutionStateEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeWorkflowExecutionStateEfCore(this IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);

    private static bool OptionsEqual(RuntimeWorkflowExecutionEntityFrameworkCoreOptions a, RuntimeWorkflowExecutionEntityFrameworkCoreOptions b) => StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(a.Provider), EfRelationalProviderBinding.Normalize(b.Provider)) && a.ConnectionString == b.ConnectionString && a.ConnectionName == b.ConnectionName && a.RecoveryContinuationSigningKey == b.RecoveryContinuationSigningKey;
    private static IReadOnlyCollection<ServiceDescriptor> AddContext(IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options, string provider) => provider switch { "sqlite" => AddContext<BookmarkStateSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite), "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer), "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql), "mysql" => AddContext<BookmarkStateMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql), _ => throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider)) };
    private static IReadOnlyCollection<ServiceDescriptor> AddContext<T>(IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : BookmarkStateDbContext { var start = services.Count; services.AddDbContext<T>((p, b) => bind(b, Resolve(p, options), RuntimeWorkflowExecutionEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name)); services.TryAddScoped<BookmarkStateDbContext>(p => p.GetRequiredService<T>()); return services.Skip(start).ToArray(); }
    private static string Resolve(IServiceProvider provider, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options) { if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString!; var cfg = provider.GetService<IConfiguration>(); if (!string.IsNullOrWhiteSpace(options.ConnectionName)) return cfg?.GetConnectionString(options.ConnectionName!) ?? throw new InvalidOperationException($"Runtime workflow execution EF connection '{options.ConnectionName}' was not found."); var fallback = cfg?.GetConnectionString(RuntimeWorkflowExecutionEfModule.DefaultConnectionName); if (!string.IsNullOrWhiteSpace(fallback)) return fallback!; if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite") return RuntimeWorkflowExecutionEfModule.DefaultSqliteConnectionString; throw new InvalidOperationException("Runtime workflow execution EF requires ConnectionString or ConnectionName for a non-Sqlite provider."); }
    private static void RemoveOwned(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> owned) { foreach (var descriptor in owned.Where(x => services.Contains(x) && RuntimeArtifactStoreBackend.Find(services)?.Owns(x) != true && RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(x) != true && BookmarkStateStoreBackend.Find(services)?.Owns(x) != true && RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(x) != true && WorkflowTestScopeStoreBackend.Find(services)?.Owns(x) != true && RuntimeOperationalStateStoreBackend.Find(services)?.Owns(x) != true)) services.Remove(descriptor); }
}

public sealed class RuntimeWorkflowExecutionEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}
