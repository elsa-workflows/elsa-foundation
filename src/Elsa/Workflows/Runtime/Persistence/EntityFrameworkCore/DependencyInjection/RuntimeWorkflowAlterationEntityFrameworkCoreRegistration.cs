using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public sealed class RuntimeWorkflowAlterationEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}

public static class RuntimeWorkflowAlterationEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowAlterationEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowAlterationEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = RuntimeWorkflowAlterationStoreBackend.Find(services);
            if (existing?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services
                    .Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeWorkflowAlterationEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime alteration EF persistence is already registered with different provider options.");
                return services;
            }

            RuntimeCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "workflow alterations");

            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                EnsureOnlyCoreAlterationRegistration(services);

            var scopeBackend = WorkflowTestScopeStoreBackend.Find(services);
            var operationalBackend = RuntimeOperationalStateStoreBackend.Find(services);
            var owners = new Func<ServiceDescriptor, bool>?[]
            {
                RuntimeArtifactStoreBackend.Find(services) is { } artifactBackend ? artifactBackend.Owns : null,
                RuntimeActivityExecutionStoreBackend.Find(services) is { } activityBackend ? activityBackend.Owns : null,
                BookmarkStateStoreBackend.Find(services) is { } bookmarkBackend ? bookmarkBackend.Owns : null,
                WorkflowExecutionStateStoreBackend.Find(services) is { } executionBackend ? executionBackend.Owns : null,
                scopeBackend is { } testScopeBackend ? testScopeBackend.Owns : null,
                operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operationalBackend.Owns : null
            };
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime alterations");
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, RuntimeWorkflowAlterationEfModule.DefaultSqliteConnectionString);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(services, provider, "Runtime alterations", owners);
            var remove = existing?.PrepareRemoveOwnedArtifacts(services);
            if (existing is not null && existing.Name != RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                services.RemoveAll<WorkflowAlterationProviderRegistration>();
            services.ClaimWorkflowAlterationProvider(typeof(EfWorkflowAlterationStore));
            services.AddSingleton(new RuntimeWorkflowAlterationEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(x => { if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey)) x.SigningKey = options.RecoveryContinuationSigningKey; x.AllowEphemeralDevelopmentKey = false; }); services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>(); services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            var owned = new List<ServiceDescriptor> { services.Last() };
            var artifact = RuntimeArtifactStoreBackend.Find(services);
            var activity = RuntimeActivityExecutionStoreBackend.Find(services);
            var bookmark = BookmarkStateStoreBackend.Find(services);
            var execution = WorkflowExecutionStateStoreBackend.Find(services);
            var scope = WorkflowTestScopeStoreBackend.Find(services);
            if (artifact?.Name == RuntimeArtifactStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(artifact.Owns));
            else if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(activity.Owns));
            else if (bookmark?.Name == BookmarkStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(bookmark.Owns));
            else if (execution?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(execution.Owns));
            else if (scope?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(scope.Owns));
            else if (operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(operationalBackend.Owns));
            else
                owned.AddRange(AddContext(services, options, provider));

            services.AddScoped<EfWorkflowAlterationStore>();
            var concrete = services.Last();
            owned.Add(concrete);
            services.RemoveAll<IWorkflowAlterationStore>();
            var contract = ServiceDescriptor.Scoped<IWorkflowAlterationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfWorkflowAlterationStore>());
            services.Add(contract);
            owned.Add(contract);
            RuntimeWorkflowAlterationStoreBackend.Register(
                services,
                new(RuntimeWorkflowAlterationStoreBackend.EntityFramework, owned));
            remove?.Invoke(services);
            return services;
        }
        catch { services.Clear(); foreach (var descriptor in snapshot) services.Add(descriptor); throw; }
    }
    public static IServiceCollection AddRuntimeWorkflowAlterationsEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowAlterationEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeWorkflowAlterationStateEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowAlterationEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeWorkflowAlterationEfCore(this IServiceCollection services, RuntimeWorkflowAlterationEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(options);
    private static bool OptionsEqual(RuntimeWorkflowAlterationEntityFrameworkCoreOptions a, RuntimeWorkflowAlterationEntityFrameworkCoreOptions b) => StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(a.Provider), EfRelationalProviderBinding.Normalize(b.Provider)) && a.ConnectionString == b.ConnectionString && a.ConnectionName == b.ConnectionName && a.RecoveryContinuationSigningKey == b.RecoveryContinuationSigningKey;
    private static void EnsureOnlyCoreAlterationRegistration(IServiceCollection services)
    {
        var contracts = services.Where(x => x.ServiceType == typeof(IWorkflowAlterationStore)).ToArray();
        if (contracts.Length != 0 && (contracts.Length != 1 || contracts[0].ImplementationType != typeof(InMemoryWorkflowAlterationStore)))
            throw new InvalidOperationException("Runtime alteration EF persistence refuses to replace an unowned alteration store.");
    }
    private static IReadOnlyCollection<ServiceDescriptor> AddContext(IServiceCollection s, RuntimeWorkflowAlterationEntityFrameworkCoreOptions o, string p) => p switch { "sqlite" => AddContext<BookmarkStateSqliteDbContext>(s, o, EfRelationalProviderBinding.UseSqlite), "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(s, o, EfRelationalProviderBinding.UseSqlServer), "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(s, o, EfRelationalProviderBinding.UseNpgsql), "mysql" => AddContext<BookmarkStateMySqlDbContext>(s, o, EfRelationalProviderBinding.UseMySql), _ => throw new ArgumentException($"Unknown Runtime EF provider '{p}'.", nameof(p)) };
    private static IReadOnlyCollection<ServiceDescriptor> AddContext<T>(IServiceCollection s, RuntimeWorkflowAlterationEntityFrameworkCoreOptions o, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : BookmarkStateDbContext { var start = s.Count; s.AddDbContext<T>((sp, b) => bind(b, Resolve(sp, o), RuntimeWorkflowAlterationEfModule.HistoryModuleName, typeof(BookmarkStateDbContext).Assembly.GetName().Name)); s.TryAddScoped<BookmarkStateDbContext>(sp => sp.GetRequiredService<T>()); return s.Skip(start).ToArray(); }
    private static string Resolve(IServiceProvider sp, RuntimeWorkflowAlterationEntityFrameworkCoreOptions o) { if (!string.IsNullOrWhiteSpace(o.ConnectionString)) return o.ConnectionString!; var cfg = sp.GetService<IConfiguration>(); if (!string.IsNullOrWhiteSpace(o.ConnectionName)) return cfg?.GetConnectionString(o.ConnectionName!) ?? throw new InvalidOperationException($"Runtime alteration EF connection '{o.ConnectionName}' was not found."); var fallback = cfg?.GetConnectionString(RuntimeWorkflowAlterationEfModule.DefaultConnectionName); if (!string.IsNullOrWhiteSpace(fallback)) return fallback!; if (EfRelationalProviderBinding.Normalize(o.Provider) == "sqlite") return RuntimeWorkflowAlterationEfModule.DefaultSqliteConnectionString; throw new InvalidOperationException("Runtime alteration EF requires ConnectionString or ConnectionName for a non-Sqlite provider."); }
}
