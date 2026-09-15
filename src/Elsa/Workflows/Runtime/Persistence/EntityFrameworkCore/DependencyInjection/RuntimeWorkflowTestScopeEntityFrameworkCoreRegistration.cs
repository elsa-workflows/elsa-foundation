using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public sealed class RuntimeWorkflowTestScopeEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}

public static class RuntimeWorkflowTestScopeEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowTestScopeEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = WorkflowTestScopeStoreBackend.Find(services);
            if (existing?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services
                    .Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeWorkflowTestScopeEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime test-scope EF persistence is already registered with different provider options.");
                return services;
            }

            if (existing is not null) existing.EnsureOwnsRegisteredContracts(services);
            else EnsureOnlyCoreScopeRegistrations(services);
            var alteration = RuntimeWorkflowAlterationStoreBackend.Find(services);
            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime test scopes");
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, RuntimeWorkflowTestScopeEfModule.DefaultSqliteConnectionString);
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime test scopes",
                RuntimeArtifactStoreBackend.Find(services) is { } artifactBackend ? artifactBackend.Owns : null,
                RuntimeActivityExecutionStoreBackend.Find(services) is { } activityBackend ? activityBackend.Owns : null,
                BookmarkStateStoreBackend.Find(services) is { } bookmarkBackend ? bookmarkBackend.Owns : null,
                WorkflowExecutionStateStoreBackend.Find(services) is { } executionBackend ? executionBackend.Owns : null,
                alteration is { } alterationBackend ? alterationBackend.Owns : null,
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);
            var remove = existing?.PrepareRemoveOwnedArtifacts(services);
            if (existing is not null && existing.Name != WorkflowTestScopeStoreBackend.EntityFramework)
                services.RemoveAll<WorkflowTestScopeProviderRegistration>();
            services.ClaimWorkflowTestScopeProvider(typeof(EfWorkflowTestScopeStore));
            services.AddSingleton(new RuntimeWorkflowTestScopeEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            });
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(configuredOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey))
                    configuredOptions.SigningKey = options.RecoveryContinuationSigningKey;
                configuredOptions.AllowEphemeralDevelopmentKey = false;
            });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());

            var owned = new List<ServiceDescriptor> { services.Last() };
            var artifact = RuntimeArtifactStoreBackend.Find(services);
            var activity = RuntimeActivityExecutionStoreBackend.Find(services);
            var bookmark = BookmarkStateStoreBackend.Find(services);
            var execution = WorkflowExecutionStateStoreBackend.Find(services);
            if (artifact?.Name == RuntimeArtifactStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(artifact.Owns));
            else if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(activity.Owns));
            else if (bookmark?.Name == BookmarkStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(bookmark.Owns));
            else if (execution?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(execution.Owns));
            else if (alteration?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(alteration.Owns));
            else if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                owned.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(operational.Owns));
            else
                owned.AddRange(AddContext(services, options, provider));

            services.AddScoped<EfWorkflowTestScopeStore>();
            var concrete = services.Last();
            owned.Add(concrete);
            services.RemoveAll<IWorkflowTestScopeStore>();
            services.RemoveAll<IWorkflowTestScopeAdmissionStore>();
            services.RemoveAll<IWorkflowTestScopeCleanupStore>();
            var store = ServiceDescriptor.Scoped<IWorkflowTestScopeStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfWorkflowTestScopeStore>());
            var admission = ServiceDescriptor.Scoped<IWorkflowTestScopeAdmissionStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfWorkflowTestScopeStore>());
            services.Add(store);
            services.Add(admission);
            owned.AddRange([store, admission]);
            WorkflowTestScopeStoreBackend.Register(
                services,
                new(WorkflowTestScopeStoreBackend.EntityFramework, owned));
            remove?.Invoke(services);
            return services;
        }
        catch { services.Clear(); foreach (var descriptor in snapshot) services.Add(descriptor); throw; }
    }
    public static IServiceCollection AddRuntimeWorkflowTestScopesEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeWorkflowTestScopeStateEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeWorkflowTestScopeEfCore(this IServiceCollection services, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options) => services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(options);
    private static bool OptionsEqual(RuntimeWorkflowTestScopeEntityFrameworkCoreOptions a, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions b) => StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(a.Provider), EfRelationalProviderBinding.Normalize(b.Provider)) && a.ConnectionString == b.ConnectionString && a.ConnectionName == b.ConnectionName && a.RecoveryContinuationSigningKey == b.RecoveryContinuationSigningKey;
    private static void EnsureOnlyCoreScopeRegistrations(IServiceCollection services)
    {
        var contracts = services.Where(x => x.ServiceType == typeof(IWorkflowTestScopeStore) || x.ServiceType == typeof(IWorkflowTestScopeAdmissionStore) || x.ServiceType == typeof(IWorkflowTestScopeCleanupStore)).ToArray();
        if (contracts.Length == 0)
            return;
        var expected = new[] { typeof(IWorkflowTestScopeStore), typeof(IWorkflowTestScopeAdmissionStore), typeof(IWorkflowTestScopeCleanupStore) };
        if (contracts.Length != expected.Length || expected.Any(serviceType => contracts.Count(x => x.ServiceType == serviceType) != 1) || contracts.Any(x => !IsCoreFactory(x)))
            throw new InvalidOperationException("Runtime test-scope EF persistence refuses to replace an unowned test-scope registration.");
    }
    private static bool IsCoreFactory(ServiceDescriptor descriptor) => descriptor.ImplementationFactory is { } factory && (factory.Method.DeclaringType == typeof(RuntimeCoreServiceCollectionExtensions) || factory.Method.DeclaringType?.DeclaringType == typeof(RuntimeCoreServiceCollectionExtensions));
    private static IReadOnlyCollection<ServiceDescriptor> AddContext(IServiceCollection s, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions o, string p) => p switch { "sqlite" => AddContext<BookmarkStateSqliteDbContext>(s, o, EfRelationalProviderBinding.UseSqlite), "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(s, o, EfRelationalProviderBinding.UseSqlServer), "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(s, o, EfRelationalProviderBinding.UseNpgsql), "mysql" => AddContext<BookmarkStateMySqlDbContext>(s, o, EfRelationalProviderBinding.UseMySql), _ => throw new ArgumentException($"Unknown Runtime EF provider '{p}'.", nameof(p)) };
    private static IReadOnlyCollection<ServiceDescriptor> AddContext<T>(IServiceCollection s, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions o, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : BookmarkStateDbContext { var start = s.Count; s.AddDbContext<T>((sp, b) => bind(b, Resolve(sp, o), RuntimeWorkflowTestScopeEfModule.HistoryModuleName, typeof(BookmarkStateDbContext).Assembly.GetName().Name)); s.TryAddScoped<BookmarkStateDbContext>(sp => sp.GetRequiredService<T>()); return s.Skip(start).ToArray(); }
    private static string Resolve(IServiceProvider sp, RuntimeWorkflowTestScopeEntityFrameworkCoreOptions o) { if (!string.IsNullOrWhiteSpace(o.ConnectionString)) return o.ConnectionString!; var cfg = sp.GetService<IConfiguration>(); if (!string.IsNullOrWhiteSpace(o.ConnectionName)) return cfg?.GetConnectionString(o.ConnectionName!) ?? throw new InvalidOperationException($"Runtime test-scope EF connection '{o.ConnectionName}' was not found."); var fallback = cfg?.GetConnectionString(RuntimeWorkflowTestScopeEfModule.DefaultConnectionName); if (!string.IsNullOrWhiteSpace(fallback)) return fallback!; if (EfRelationalProviderBinding.Normalize(o.Provider) == "sqlite") return RuntimeWorkflowTestScopeEfModule.DefaultSqliteConnectionString; throw new InvalidOperationException("Runtime test-scope EF requires ConnectionString or ConnectionName for a non-Sqlite provider."); }
}
